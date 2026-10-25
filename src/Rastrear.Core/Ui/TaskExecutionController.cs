using System.Diagnostics;
using Rastrear.Core.Model;

namespace Rastrear.Core.Ui;

public enum ExecutionState
{
    Idle,
    Running,
    Cancelling,
    Completed,
    Cancelled,
    Faulted,
}

public sealed record UiProgressInfo(
    double Fraction,
    string StatusText,
    TimeSpan Elapsed,
    string FormattedProgress,
    string FormattedTime);

/// <summary>
/// Controlador de estado, cancelación rápida y formateo amigable de errores en español para tareas de análisis.
/// </summary>
public sealed class TaskExecutionController
{
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();

    public ExecutionState State { get; private set; } = ExecutionState.Idle;
    public double CurrentFraction { get; private set; }
    public string CurrentStatus { get; private set; } = string.Empty;
    public string? LastError { get; private set; }
    public TimeSpan LastCancelDuration { get; private set; }

    public bool IsBusy => State is ExecutionState.Running or ExecutionState.Cancelling;

    public CancellationToken BeginExecution()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _stopwatch.Restart();
        State = ExecutionState.Running;
        CurrentFraction = 0.0;
        CurrentStatus = "Iniciando...";
        LastError = null;
        LastCancelDuration = TimeSpan.Zero;
        return _cts.Token;
    }

    public void ReportProgress(double fraction, string status)
    {
        if (State != ExecutionState.Running)
        {
            return;
        }

        CurrentFraction = Math.Clamp(fraction, 0.0, 1.0);
        CurrentStatus = status;
    }

    public UiProgressInfo GetProgressInfo()
    {
        int pct = (int)Math.Round(CurrentFraction * 100);
        string formatted = $"{pct}% - {CurrentStatus}";
        return new UiProgressInfo(CurrentFraction, CurrentStatus, _stopwatch.Elapsed, formatted, FormatElapsedEstimate(_stopwatch.Elapsed, CurrentFraction));
    }

    /// <summary>«0:12 · quedan ~0:36»; sin estimación hasta tener un 2 % hecho, porque antes el cálculo baila.</summary>
    public static string FormatElapsedEstimate(TimeSpan elapsed, double fraction)
    {
        string done = FormatMinSec(elapsed);
        if (fraction < 0.02 || fraction >= 1.0)
        {
            return done;
        }

        TimeSpan remaining = elapsed / fraction - elapsed;
        return $"{done} · quedan ~{FormatMinSec(remaining)}";
    }

    public static string FormatMinSec(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    public async Task<bool> CancelAsync(TimeSpan timeout = default)
    {
        if (State != ExecutionState.Running || _cts is null)
        {
            return false;
        }

        if (timeout == default)
        {
            timeout = TimeSpan.FromSeconds(1.0);
        }

        State = ExecutionState.Cancelling;
        CurrentStatus = "Cancelando...";

        Stopwatch sw = Stopwatch.StartNew();
        _cts.Cancel();
        sw.Stop();
        LastCancelDuration = sw.Elapsed;

        // Espera no bloqueante breve para dar tiempo al cierre si se llama desde la UI
        await Task.Yield();
        return true;
    }

    public void MarkCompleted()
    {
        _stopwatch.Stop();
        State = ExecutionState.Completed;
        CurrentFraction = 1.0;
        CurrentStatus = "Completado con éxito.";
    }

    public void MarkCancelled()
    {
        _stopwatch.Stop();
        State = ExecutionState.Cancelled;
        CurrentStatus = "Operación cancelada.";
    }

    public void MarkFaulted(Exception ex)
    {
        _stopwatch.Stop();
        State = ExecutionState.Faulted;
        LastError = FormatErrorMessage(ex);
        CurrentStatus = $"Error: {LastError}";
    }

    public static string FormatErrorMessage(Exception ex)
    {
        return ex switch
        {
            OperationCanceledException => "Operación cancelada por el usuario.",
            RastrearUserException userEx => userEx.Message,
            RastrearModuleException modEx => modEx.Message,
            FileNotFoundException fnf => $"No se encontró el archivo requerido: {fnf.FileName ?? fnf.Message}",
            DirectoryNotFoundException dnf => $"No se encontró la carpeta: {dnf.Message}",
            _ => CleanMessage(ex.Message),
        };
    }

    private static string CleanMessage(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Se produjo un error inesperado al procesar el video.";
        }

        // Si contiene salto de línea o traza, tomamos la primera línea significativa
        string firstLine = raw.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)[0].Trim();
        return firstLine;
    }
}
