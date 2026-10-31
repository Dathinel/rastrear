namespace Rastrear.Core.Ui;

public enum BatchItemState
{
    Pending,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Un video en la cola: su estado, su progreso (0..1 mientras corre) y el archivo que produjo o el error.</summary>
public sealed class BatchItem(string path)
{
    public string Path { get; } = path;
    public string FileName => System.IO.Path.GetFileName(Path);
    public BatchItemState State { get; internal set; } = BatchItemState.Pending;
    public double Fraction { get; internal set; }
    public string Stage { get; internal set; } = string.Empty;
    public string? OutputPath { get; internal set; }
    public string? Error { get; internal set; }

    /// <summary>Lo que se muestra junto al nombre: «pendiente», «40 % · Siguiendo», «hecho → clip-fondo.webm», «error: …».</summary>
    public string StatusText => State switch
    {
        BatchItemState.Pending => "pendiente",
        BatchItemState.Running => Stage.Length > 0 ? $"{Fraction * 100:0} % · {Stage}" : $"{Fraction * 100:0} %",
        BatchItemState.Done => OutputPath is { } o ? $"hecho → {System.IO.Path.GetFileName(o)}" : "hecho",
        BatchItemState.Failed => $"error: {Error}",
        BatchItemState.Cancelled => "cancelado",
        _ => string.Empty,
    };
}

/// <summary>
/// Cola de varios videos para la misma herramienta: se procesan uno a uno, con progreso por archivo y global;
/// cancelar deja el actual como cancelado y los pendientes sin tocar.
/// </summary>
public sealed class BatchQueue
{
    private readonly List<BatchItem> _items = [];

    public IReadOnlyList<BatchItem> Items => _items;
    public int Count => _items.Count;
    public BatchItem? Current { get; private set; }
    public bool HasPending => _items.Any(i => i.State == BatchItemState.Pending);

    /// <summary>Añade los que no estén ya (misma ruta, sin distinguir mayúsculas) y devuelve cuántos entraron.</summary>
    public int Enqueue(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (_items.Any(i => string.Equals(i.Path, p, StringComparison.OrdinalIgnoreCase))) continue;
            _items.Add(new BatchItem(p));
            added++;
        }
        return added;
    }

    /// <summary>Vacía la cola (solo cuando nada corre).</summary>
    public void Clear()
    {
        if (Current is { State: BatchItemState.Running })
        {
            throw new InvalidOperationException("No se puede vaciar la cola mientras se procesa un archivo.");
        }
        _items.Clear();
        Current = null;
    }

    /// <summary>Vuelve a dejar todos pendientes para repetir la cola con otros parámetros (solo cuando nada corre).</summary>
    public void Reset()
    {
        if (Current is { State: BatchItemState.Running })
        {
            throw new InvalidOperationException("No se puede reiniciar la cola mientras se procesa un archivo.");
        }
        foreach (BatchItem i in _items)
        {
            i.State = BatchItemState.Pending;
            i.Fraction = 0;
            i.Stage = string.Empty;
            i.OutputPath = null;
            i.Error = null;
        }
        Current = null;
    }

    /// <summary>Toma el siguiente pendiente y lo marca en curso; null si no queda ninguno.</summary>
    public BatchItem? StartNext()
    {
        BatchItem? next = _items.FirstOrDefault(i => i.State == BatchItemState.Pending);
        if (next is null)
        {
            Current = null;
            return null;
        }
        next.State = BatchItemState.Running;
        next.Fraction = 0;
        next.Stage = string.Empty;
        Current = next;
        return next;
    }

    public void ReportProgress(double fraction, string stage)
    {
        if (Current is { State: BatchItemState.Running } c)
        {
            c.Fraction = Math.Clamp(fraction, 0, 1);
            c.Stage = stage;
        }
    }

    public void MarkDone(string? outputPath)
    {
        if (Current is { } c)
        {
            c.State = BatchItemState.Done;
            c.Fraction = 1;
            c.OutputPath = outputPath;
        }
    }

    public void MarkFailed(string error)
    {
        if (Current is { } c)
        {
            c.State = BatchItemState.Failed;
            c.Error = error;
        }
    }

    /// <summary>El actual queda cancelado; los pendientes siguen pendientes para poder reanudar.</summary>
    public void MarkCancelled()
    {
        if (Current is { } c)
        {
            c.State = BatchItemState.Cancelled;
        }
    }

    /// <summary>Progreso global: archivos acabados (hechos, fallidos o cancelados) más la fracción del actual.</summary>
    public double OverallFraction
    {
        get
        {
            if (_items.Count == 0) return 0;
            double done = _items.Count(i => i.State is BatchItemState.Done or BatchItemState.Failed or BatchItemState.Cancelled);
            double running = Current is { State: BatchItemState.Running } c ? c.Fraction : 0;
            return Math.Clamp((done + running) / _items.Count, 0, 1);
        }
    }

    /// <summary>Posición del actual (1..N) para «Archivo 2 de 5».</summary>
    public int CurrentPosition => Current is null ? 0 : _items.IndexOf(Current) + 1;

    /// <summary>«Archivo 2 de 5 · 40 % · Siguiendo» (con un solo archivo, solo el progreso).</summary>
    public string ProgressText
    {
        get
        {
            if (Current is null) return string.Empty;
            string own = Current.StatusText;
            return _items.Count > 1 ? $"Archivo {CurrentPosition} de {_items.Count} · {own}" : own;
        }
    }

    /// <summary>Resumen al acabar: «3 hechos, 1 con error, 1 cancelado».</summary>
    public string Summary
    {
        get
        {
            int done = _items.Count(i => i.State == BatchItemState.Done);
            int failed = _items.Count(i => i.State == BatchItemState.Failed);
            int cancelled = _items.Count(i => i.State == BatchItemState.Cancelled);
            int pending = _items.Count(i => i.State == BatchItemState.Pending);
            List<string> parts = [];
            if (done > 0) parts.Add(done == 1 ? "1 hecho" : $"{done} hechos");
            if (failed > 0) parts.Add(failed == 1 ? "1 con error" : $"{failed} con error");
            if (cancelled > 0) parts.Add(cancelled == 1 ? "1 cancelado" : $"{cancelled} cancelados");
            if (pending > 0) parts.Add(pending == 1 ? "1 pendiente" : $"{pending} pendientes");
            return string.Join(", ", parts);
        }
    }

    /// <summary>Extensiones de video que se aceptan al soltar; lo demás (subtítulos, imágenes) se ignora.</summary>
    public static readonly string[] VideoExtensions = [".mp4", ".mov", ".webm", ".mkv", ".avi", ".ts", ".m4v", ".mts", ".m2ts", ".mpg", ".mpeg", ".wmv"];

    public static bool IsVideoFile(string path) =>
        Array.IndexOf(VideoExtensions, System.IO.Path.GetExtension(path).ToLowerInvariant()) >= 0;

    /// <summary>De lo soltado, solo los videos, en el orden en que llegaron y sin repetidos.</summary>
    public static List<string> FilterDroppedPaths(IEnumerable<string> paths)
    {
        List<string> videos = [];
        foreach (string p in paths)
        {
            if (IsVideoFile(p) && !videos.Contains(p, StringComparer.OrdinalIgnoreCase))
            {
                videos.Add(p);
            }
        }
        return videos;
    }
}
