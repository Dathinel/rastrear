using System.Diagnostics;

namespace Rastrear.Core.Video;

/// <summary>
/// Los procesos hijos vivos (ffmpeg, ffprobe y el módulo de visión) para que «Cancelar» no deje ninguno:
/// cada servicio los apunta al arrancarlos y los quita al cerrarlos; <see cref="KillAllAsync"/> mata los que queden
/// y espera a que salgan.
/// </summary>
public static class ChildProcessRegistry
{
    private sealed class Entry(string name, Action kill, Func<bool> hasExited, Func<TimeSpan, Task<bool>> waitExit)
    {
        public string Name { get; } = name;
        public Action Kill { get; } = kill;
        public Func<bool> HasExited { get; } = hasExited;
        public Func<TimeSpan, Task<bool>> WaitExit { get; } = waitExit;
    }

    private static readonly Lock s_lock = new();
    private static readonly Dictionary<Registration, Entry> s_entries = [];

    /// <summary>Lo que devuelve <see cref="Track(Process, string)"/>: se desecha al cerrar el proceso.</summary>
    public sealed class Registration : IDisposable
    {
        public void Dispose()
        {
            lock (s_lock)
            {
                s_entries.Remove(this);
            }
        }
    }

    /// <summary>Cuántos procesos apuntados siguen vivos.</summary>
    public static int LiveCount
    {
        get
        {
            lock (s_lock)
            {
                return s_entries.Values.Count(e => !SafeHasExited(e));
            }
        }
    }

    /// <summary>Apunta un <see cref="Process"/> ya arrancado (ffmpeg, ffprobe).</summary>
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

    /// <summary>Apunta cualquier cosa que se pueda matar (el módulo de visión por <c>ModuleProcess</c>).</summary>
    public static Registration Track(string name, Action kill, Func<bool> hasExited, Func<TimeSpan, Task<bool>> waitExit)
    {
        Registration reg = new();
        lock (s_lock)
        {
            s_entries[reg] = new Entry(name, kill, hasExited, waitExit);
        }
        return reg;
    }

    /// <summary>
    /// Mata todo lo apuntado que siga vivo y espera, hasta <paramref name="timeout"/>, a que salga.
    /// Devuelve los nombres de lo que seguía vivo al vencer el tope (vacío: no queda nada).
    /// </summary>
    public static async Task<IReadOnlyList<string>> KillAllAsync(TimeSpan timeout)
    {
        List<Entry> live;
        lock (s_lock)
        {
            live = s_entries.Values.Where(e => !SafeHasExited(e)).ToList();
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

    private static bool SafeHasExited(Entry e)
    {
        try { return e.HasExited(); }
        catch (InvalidOperationException) { return true; }
    }
}
