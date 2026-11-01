namespace Rastrear.Core;

/// <summary>
/// Un módulo que se mantiene vivo entre llamadas (el de visión mientras «Después» está pulsado): se arranca la primera
/// vez que hace falta, se reutiliza mientras siga vivo y se cierra al desechar la sesión. Si una llamada falla o el
/// proceso murió, se cierra lo que quede y la siguiente llamada arranca otro. Las llamadas van de una en una: la que
/// llega mientras otra corre espera (o se cancela) en vez de pisarla.
/// </summary>
/// <typeparam name="TModule">El proceso del módulo (<c>ModuleProcess</c> en la app; uno falso en las pruebas).</typeparam>
public sealed class ModuleSession<TModule> : IAsyncDisposable where TModule : class, IAsyncDisposable
{
    private readonly Func<CancellationToken, Task<TModule>> _start;
    private readonly Func<TModule, bool> _hasExited;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TModule? _module;
    private bool _disposed;

    /// <param name="start">Arranca el módulo (lanza si no se puede).</param>
    /// <param name="hasExited">Si el proceso ya murió (y hay que arrancar otro).</param>
    public ModuleSession(Func<CancellationToken, Task<TModule>> start, Func<TModule, bool> hasExited)
    {
        _start = start;
        _hasExited = hasExited;
    }

    /// <summary>Cuántas veces se arrancó el módulo: uno por sesión si nada falla.</summary>
    public int StartCount { get; private set; }

    /// <summary>Si hay un módulo arrancado y vivo ahora mismo.</summary>
    public bool IsAlive => _module is { } m && !_hasExited(m);

    /// <summary>
    /// Ejecuta <paramref name="work"/> con el módulo vivo, arrancándolo si hace falta. Una cancelación deja el módulo
    /// como está; cualquier otro fallo lo cierra para que la próxima llamada arranque de cero.
    /// </summary>
    public async Task<TResult> RunAsync<TResult>(Func<TModule, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_module is { } stale && _hasExited(stale))
            {
                await DropAsync().ConfigureAwait(false);
            }
            if (_module is null)
            {
                _module = await _start(cancellationToken).ConfigureAwait(false);
                StartCount++;
            }

            try
            {
                return await work(_module, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                await DropAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cierra el módulo si está arrancado; la sesión sigue sirviendo (arrancaría otro).</summary>
    public async Task CloseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await DropAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task DropAsync()
    {
        if (_module is not { } module) return;
        _module = null;
        try
        {
            await module.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Un módulo que ya murió o no responde al cerrar: no hay nada más que hacer con él.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await DropAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
