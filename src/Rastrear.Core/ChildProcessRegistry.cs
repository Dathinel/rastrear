using System.Diagnostics;

namespace Rastrear.Core;

/// <summary>
/// Los procesos hijos vivos (ffmpeg, ffprobe y el módulo de visión) de cada ejecución, para que «Cancelar» no deje
/// ninguno. Un <see cref="Scope"/> se abre con <see cref="BeginScope"/> y viaja con el flujo asíncrono
/// (<see cref="AsyncLocal{T}"/>): todo lo que se arranca dentro queda apuntado en él, y <see cref="Scope.KillAllAsync"/>
/// mata lo que siga vivo y espera a que salga. Lo arrancado fuera de todo ámbito (el reproductor, las pruebas de otro
/// hilo) no se toca.
/// </summary>
public static class ChildProcessRegistry
{
    internal sealed class Entry(string name, Action kill, Func<bool> hasExited, Func<TimeSpan, Task<bool>> waitExit)
    {
        public string Name { get; } = name;
        public Action Kill { get; } = kill;
        public Func<bool> HasExited { get; } = hasExited;
        public Func<TimeSpan, Task<bool>> WaitExit { get; } = waitExit;
    }

    private static readonly AsyncLocal<Scope?> s_current = new();

    /// <summary>El ámbito del flujo actual, o null fuera de toda ejecución.</summary>
    public static Scope? Current => s_current.Value;

    /// <summary>Abre un ámbito nuevo para el flujo actual (lo que arranque de aquí en adelante en este flujo asíncrono se apunta en él).</summary>
    public static Scope BeginScope()
    {
        Scope scope = new();
        s_current.Value = scope;
        return scope;
    }

    /// <summary>Lo que devuelve <see cref="Track(Process, string)"/>: se desecha al cerrar el proceso.</summary>
    public sealed class Registration : IDisposable
    {
        private readonly Scope? _scope;
        internal Registration(Scope? scope) => _scope = scope;
        public void Dispose() => _scope?.Remove(this);
    }

    /// <summary>Los procesos de una ejecución.</summary>
    public sealed class Scope
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<Registration, Entry> _entries = [];

        internal Scope() { }

        /// <summary>Cuántos procesos apuntados siguen vivos.</summary>
        public int LiveCount
        {
            get
            {
                lock (_lock)
                {
                    return _entries.Values.Count(e => !SafeHasExited(e));
                }
            }
        }

        internal Registration Add(Entry entry)
        {
            Registration reg = new(this);
            lock (_lock)
            {
                _entries[reg] = entry;
            }
            return reg;
        }

        internal void Remove(Registration reg)
        {
            lock (_lock)
            {
                _entries.Remove(reg);
            }
        }

        /// <summary>
        /// Mata todo lo apuntado que siga vivo y espera, hasta <paramref name="timeout"/>, a que salga.
        /// Devuelve los nombres de lo que seguía vivo al vencer el tope (vacío: no queda nada).
        /// </summary>
        public async Task<IReadOnlyList<string>> KillAllAsync(TimeSpan timeout)
        {
            List<Entry> live;
            lock (_lock)
            {
                live = _entries.Values.Where(e => !SafeHasExited(e)).ToList();
            }

            foreach (Entry e in live)
            {
                try { e.Kill(); } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }

            List<string> survivors = [];
            foreach (Entry e in live)
            {
                bool exited;
                try { exited = await e.WaitExit(timeout).ConfigureAwait(false); }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { exited = true; }
                if (!exited)
                {
                    survivors.Add(e.Name);
                }
            }

            return survivors;
        }
    }

    /// <summary>Apunta un <see cref="Process"/> ya arrancado (ffmpeg, ffprobe) en el ámbito actual; fuera de un ámbito no hace nada.</summary>
    public static Registration Track(Process process, string name) => Track(
        name,
        () => { if (!process.HasExited) process.Kill(entireProcessTree: true); },
        () => process.HasExited,
        async timeout =>
        {
            using CancellationTokenSource cts = new(timeout);
            try
            {
                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException)
            {
                return process.HasExited;
            }
        });

    /// <summary>Apunta un proceso recién arrancado y lo quita solo cuando sale (para ffmpeg y ffprobe, que nadie cierra a mano).</summary>
    public static void TrackUntilExit(Process process, string name)
    {
        if (s_current.Value is null) return;
        Registration reg = Track(process, name);
        try
        {
            process.EnableRaisingEvents = true;
            process.Exited += (_, _) => reg.Dispose();
        }
        catch (InvalidOperationException)
        {
            // Ya salió o no se pudo observar: la entrada se filtra por HasExited.
        }
    }

    /// <summary>Apunta cualquier cosa que se pueda matar (el módulo de visión por <c>ModuleProcess</c>) en el ámbito actual.</summary>
    public static Registration Track(string name, Action kill, Func<bool> hasExited, Func<TimeSpan, Task<bool>> waitExit)
    {
        Scope? scope = s_current.Value;
        return scope is null ? new Registration(null) : scope.Add(new Entry(name, kill, hasExited, waitExit));
    }

    private static bool SafeHasExited(Entry e)
    {
        try { return e.HasExited(); }
        catch (InvalidOperationException) { return true; }
    }
}
