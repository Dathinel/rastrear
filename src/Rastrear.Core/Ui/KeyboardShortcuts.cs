namespace Rastrear.Core.Ui;

public enum ShortcutAction
{
    None,
    OpenVideo,
    Run,
    Cancel,
}

/// <summary>Atajos de la ventana: <c>Ctrl+O</c> abrir, <c>Ctrl+Enter</c> empezar, <c>Esc</c> cancelar; cada uno solo cuando tiene sentido.</summary>
public static class KeyboardShortcuts
{
    /// <summary>
    /// Qué hacer con una tecla (<paramref name="key"/>: «O», «Enter», «Escape», como las nombra VirtualKey) y Ctrl.
    /// Abrir no vale mientras algo corre; empezar necesita un video y nada corriendo; cancelar, algo corriendo.
    /// </summary>
    public static ShortcutAction Resolve(string key, bool ctrl, bool running, bool hasVideo)
    {
        if (string.IsNullOrEmpty(key)) return ShortcutAction.None;
        switch (key.ToUpperInvariant())
        {
            case "O" when ctrl:
                return running ? ShortcutAction.None : ShortcutAction.OpenVideo;
            case "ENTER" when ctrl:
                return !running && hasVideo ? ShortcutAction.Run : ShortcutAction.None;
            case "ESCAPE" when !ctrl:
                return running ? ShortcutAction.Cancel : ShortcutAction.None;
            default:
                return ShortcutAction.None;
        }
    }
}
