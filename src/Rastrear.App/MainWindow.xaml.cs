using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Rastrear.Core;
using Rastrear.Core.Model;
using Rastrear.Core.Stabilization;
using Rastrear.Core.Tracking;
using Rastrear.Core.Ui;
using Rastrear.Core.Video;
using Rastrear.Core.Vision;
using Rodaje.Modules.Protocol;
using Windows.Graphics;
using Windows.UI;
using WinRT;
using static TerraFX.Interop.Windows.Windows;

namespace Rastrear.App;

public sealed partial class MainWindow : Window
{
    private readonly nint _hwnd;
    private string? _currentVideoPath;
    private VideoInfo? _videoInfo;
    private PlayerTimeModel? _timeModel;
    private ViewportCoordinateTransformer? _transformer;
    private PlayerFrameBuffer? _frameBuffer;

    private WriteableBitmap? _bitmap;
    private readonly DispatcherTimer _playbackTimer = new();
    private bool _isPlaying;
    private bool _isScrubbing;

    // Estado de selección de objetivo para 'Seguir'
    private PointTargetSpec? _selectedPoint;
    private RegionTargetSpec? _selectedRegion;
    private bool _isDraggingSelection;
    private double _dragStartVx;
    private double _dragStartVy;

    // Resultados de análisis
    private FollowResult? _followResult;
    private DetectionResult? _detectionResult;
    private string? _lastOutputFile;

    // Controlador de tareas y cancelación rápida
    private readonly TaskExecutionController _taskController = new();

    // Última carpeta, última herramienta y sus parámetros (preferencias.json)
    private readonly UserPreferences _prefs = UserPreferences.Load(UserPreferences.DefaultPath);
    private bool _applyingPreferences;

    // Aviso de versión nueva: GitHub sin token, como mucho una vez cada 24 h (UpdateChecker)
    private readonly GitHubReleaseFeed _releaseFeed = new(UpdateChecker.CurrentVersion);
    private ReleaseInfo? _offeredRelease;
    private string? _launcherPath;

    // Cola de videos soltados: se procesan uno tras otro con la misma herramienta
    private readonly BatchQueue _queue = new();

    // Vista previa «después» del primer cuadro: el módulo de visión queda vivo mientras «Después» esté pulsado
    private WriteableBitmap? _previewBitmap;
    private CancellationTokenSource? _previewCts;
    private ModuleSession<ModuleProcess>? _previewSession;
    private bool PreviewOn => BtnVistaPrevia.IsChecked == true;

    // Ventana: mínimo en DIP y últimos límites sin maximizar (lo que se guarda en ventana.json)
    private const int MinWidthDip = 960;
    private const int MinHeightDip = 600;
    private RectInt32 _restoredBounds;
    private bool _maximized;

    // Paleta «radar» de la superposición
    private static readonly SolidColorBrush AccentBrush = new(Color.FromArgb(255, 0x4D, 0xE8, 0xC6));
    private static readonly SolidColorBrush TrailBrush = new(Color.FromArgb(190, 0x4D, 0xE8, 0xC6));
    private static readonly SolidColorBrush ReticleBrush = new(Color.FromArgb(56, 0x4D, 0xE8, 0xC6));
    private static readonly SolidColorBrush MarkerFillBrush = new(Color.FromArgb(255, 0xEA, 0xF1, 0xEF));
    private static readonly SolidColorBrush BadgeBrush = new(Color.FromArgb(220, 0x0E, 0x12, 0x11));
    private static readonly SolidColorBrush GuideBrush = new(Color.FromArgb(210, 0xEA, 0xF1, 0xEF));

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = "Rastrear";
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "rastrear.ico"));
        AppWindow.TitleBar.PreferredTheme = TitleBarTheme.UseDefaultAppMode;
        RestorePlacement();
        ApplyPreferences();
        AppWindow.Changed += OnAppWindowChanged;
        Closed += async (_, _) =>
        {
            // Cancelar arranca el kill de ffmpeg y el módulo antes de guardar; se espera al final para no dejarlos vivos.
            // El módulo de la vista previa se cierra a la vez, con un tope: la ventana no se queda esperando a un módulo colgado.
            Task cancel = _taskController.CancelAsync();
            Task preview = ClosePreviewSessionAsync();
            SavePlacement();
            SavePreferences();
            await cancel;
            await ModuleProcess.WaitForCloseAsync(preview, TimeSpan.FromSeconds(3));
        };

        _playbackTimer.Tick += OnPlaybackTick;
        AboutVersionText.Text = "Versión " + UpdateChecker.Format(UpdateChecker.CurrentVersion);

        // Medición de arranque en frío
        if (Program.MeasureStartup)
        {
            RecordStartup();
        }

        // Al arrancar, sin bloquear la ventana; con el aviso apagado o antes de 24 h no consulta
        _ = CheckForUpdatesAsync(manual: false);
    }

    private async void OnAcercaDeClick(object sender, RoutedEventArgs e)
    {
        AboutDialog.XamlRoot = Content.XamlRoot;
        await AboutDialog.ShowAsync();
    }

    private void RecordStartup()
    {
        double ms = Program.StartupMs;
        string msg = string.Format(CultureInfo.InvariantCulture, "Arranque en frío: {0:0.##} ms", ms);
        try
        {
            File.WriteAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "arranque.txt"), msg);
        }
        catch { }
    }

    #region Tamaño y posición de la ventana

    private double DpiScale => GetDpiForWindow((TerraFX.Interop.Windows.HWND)_hwnd) / 96.0;

    // Con Native AOT, «Presenter is OverlappedPresenter» no acierta (el RCW no sale con el tipo derivado): se pregunta por QueryInterface.
    private OverlappedPresenter? Overlapped =>
        AppWindow.Presenter is { Kind: AppWindowPresenterKind.Overlapped } p ? p.As<OverlappedPresenter>() : null;

    private void RestorePlacement()
    {
        double scale = DpiScale;
        int minW = (int)(MinWidthDip * scale);
        int minH = (int)(MinHeightDip * scale);
        if (Overlapped is { } presenter)
        {
            presenter.PreferredMinimumWidth = minW;
            presenter.PreferredMinimumHeight = minH;
        }

        WindowPlacement? saved = WindowPlacement.Load(WindowPlacement.DefaultPath);
        RectInt32 wanted = saved is null
            ? new RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height)
            : new RectInt32(saved.X, saved.Y, saved.Width, saved.Height);
        RectInt32 area = DisplayArea.GetFromRect(wanted, DisplayAreaFallback.Nearest).WorkArea;
        WindowPlacement? fit = saved?.FitInto(area.X, area.Y, area.Width, area.Height, minW, minH);
        if (fit is null)
        {
            AppWindow.Resize(new SizeInt32((int)(1280 * scale), (int)(800 * scale)));
            return;
        }

        AppWindow.MoveAndResize(new RectInt32(fit.X, fit.Y, fit.Width, fit.Height));
        if (fit.Maximized && Overlapped is { } p)
        {
            p.Maximize();
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (Overlapped is not { } presenter)
        {
            return;
        }

        _maximized = presenter.State == OverlappedPresenterState.Maximized;
        if (presenter.State == OverlappedPresenterState.Restored)
        {
            _restoredBounds = new RectInt32(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        }
    }

    private void SavePlacement()
    {
        // Se guarda lo visto en Changed: al cerrar, AppWindow ya puede no responder.
        if (_restoredBounds.Width <= 0)
        {
            return;
        }

        try
        {
            new WindowPlacement(_restoredBounds.X, _restoredBounds.Y, _restoredBounds.Width, _restoredBounds.Height, _maximized).Save(WindowPlacement.DefaultPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sin permiso o disco lleno: se abre con el tamaño por defecto la próxima vez.
        }
    }

    #endregion

    #region Preferencias (última carpeta, herramienta y parámetros)

    private void ApplyPreferences()
    {
        _applyingPreferences = true;
        try
        {
            ApplyPreferencesCore();
        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    private void ApplyPreferencesCore()
    {
        UserPreferences p = _prefs;
        TaskSelector.SelectedIndex = p.ToolIndex;

        if (p.FollowMode == "region") RbSeguirRegion.IsChecked = true; else RbSeguirPunto.IsChecked = true;
        SliderPatternSize.Value = p.FollowPatternSize;

        SwitchFondoModo.IsOn = p.BackgroundMask;
        ComboCalidadFondo.SelectedIndex = p.BackgroundQuality == "alta" ? 1 : 0;
        ComboDeviceFondo.SelectedIndex = DeviceIndex(p.BackgroundDevice);

        ComboTargetDetectar.SelectedIndex = p.DetectTarget == "personas" ? 1 : 0;
        ComboDeviceDetectar.SelectedIndex = DeviceIndex(p.DetectDevice);

        switch (p.ReframeAspect)
        {
            case "1:1": RbAspect11.IsChecked = true; break;
            case "4:5": RbAspect45.IsChecked = true; break;
            default: RbAspect916.IsChecked = true; break;
        }
        SliderSmoothing.Value = p.ReframeSmoothing;

        SliderIntensity.Value = p.StabilizeIntensity;
        ChkAutoCrop.IsChecked = p.StabilizeAutoCrop;

        SwitchAvisarVersion.IsOn = p.NotifyNewVersion;
        ApplyOutputFolder();
    }

    /// <summary>La carpeta de salida no es un control con valor: se muestra y se guarda tal cual está en las preferencias.</summary>
    private void ApplyOutputFolder()
    {
        OutputFolderText.Text = _prefs.OutputFolderText;
        ToolTipService.SetToolTip(OutputFolderText, _prefs.OutputFolder is { } f ? f : "Cada resultado se deja en la carpeta de su video");
        BtnCarpetaSalidaJunto.IsEnabled = _prefs.OutputFolder is not null;
    }

    private void OnElegirCarpetaSalidaClick(object sender, RoutedEventArgs e)
    {
        string? folder = FileDialogs.ChooseFolder(_hwnd, "Carpeta de salida");
        if (string.IsNullOrEmpty(folder)) return;
        _prefs.OutputFolder = UserPreferences.NormalizeFolder(folder);
        ApplyOutputFolder();
        SavePreferences();
    }

    private void OnCarpetaSalidaJuntoClick(object sender, RoutedEventArgs e)
    {
        _prefs.OutputFolder = null;
        ApplyOutputFolder();
        SavePreferences();
    }

    private void CollectPreferences()
    {
        UserPreferences p = _prefs;
        p.ToolIndex = TaskSelector.SelectedIndex;
        p.FollowMode = RbSeguirRegion.IsChecked == true ? "region" : "punto";
        p.FollowPatternSize = (int)SliderPatternSize.Value;
        p.BackgroundMask = SwitchFondoModo.IsOn;
        p.BackgroundQuality = ComboCalidadFondo.SelectedIndex == 1 ? "alta" : "normal";
        p.BackgroundDevice = DeviceName(ComboDeviceFondo.SelectedIndex);
        p.DetectTarget = ComboTargetDetectar.SelectedIndex == 1 ? "personas" : "rostros";
        p.DetectDevice = DeviceName(ComboDeviceDetectar.SelectedIndex);
        p.ReframeAspect = CurrentAspectPreset();
        p.ReframeSmoothing = SliderSmoothing.Value;
        p.StabilizeIntensity = SliderIntensity.Value;
        p.StabilizeAutoCrop = ChkAutoCrop.IsChecked == true;
        p.NotifyNewVersion = SwitchAvisarVersion.IsOn;
        p.Normalize();
    }

    private void SavePreferences()
    {
        CollectPreferences();
        _prefs.TrySave(UserPreferences.DefaultPath);
    }

    private static int DeviceIndex(string device) => device switch { "gpu" => 1, "cpu" => 2, _ => 0 };
    private static string DeviceName(int index) => index switch { 1 => "gpu", 2 => "cpu", _ => "auto" };
    private string CurrentAspectPreset() => RbAspect916.IsChecked == true ? "9:16" : (RbAspect11.IsChecked == true ? "1:1" : "4:5");

    #endregion

    private void ShowNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        NotificationBar.ActionButton = null;
        NotificationBar.Message = message;
        NotificationBar.Severity = severity;
        NotificationBar.IsOpen = true;
    }

    /// <summary>Falta el módulo de visión: mensaje claro y, si el Lanzador está en el PC, un botón que lo abre.</summary>
    private void ShowModuleMissing()
    {
        string? launcher = LauncherLocator.Find();
        ShowNotification(VisionModuleMissingException.UserMessage(launcherFound: launcher is not null), InfoBarSeverity.Warning);
        if (launcher is null) return;

        Button open = new() { Content = "Abrir el Lanzador" };
        open.Click += (_, _) => TryOpenLauncher(launcher);
        NotificationBar.ActionButton = open;
    }

    private void TryOpenLauncher(string launcher)
    {
        try
        {
            Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            ShowNotification($"No se pudo abrir el Lanzador ({launcher}): {ex.Message}", InfoBarSeverity.Error);
        }
    }

    #region Versión nueva

    /// <summary>
    /// Consulta la última release (<see cref="UpdateChecker"/>) sin bloquear la ventana. Al arrancar (<paramref name="manual"/> false)
    /// solo si el aviso está activado y pasaron 24 h, y cualquier fallo es silencio; desde «Buscar actualizaciones» siempre,
    /// y el resultado se dice bajo el enlace, también cuando no hay nada nuevo.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (manual)
        {
            BtnBuscarActualizaciones.IsEnabled = false;
            UpdateStatusText.Text = "Consultando GitHub…";
        }

        UpdateCheckResult result;
        try
        {
            result = await UpdateChecker.CheckAsync(_releaseFeed, UpdateChecker.CurrentVersion, _prefs, DateTimeOffset.Now, force: manual);
        }
        catch (Exception)
        {
            // Nada de esto puede tumbar la app: sin aviso y ya está.
            result = UpdateCheckResult.Failed;
        }

        if (result.Outcome != UpdateCheckOutcome.Skipped)
        {
            SavePreferences(); // La hora de la consulta
        }

        if (manual)
        {
            BtnBuscarActualizaciones.IsEnabled = true;
            UpdateStatusText.Text = UpdateChecker.ManualCheckText(result, UpdateChecker.CurrentVersion);
        }

        if (result.Outcome == UpdateCheckOutcome.NewVersion && result.Release is { } release)
        {
            ShowUpdateBanner(release);
        }
    }

    private void ShowUpdateBanner(ReleaseInfo release)
    {
        _offeredRelease = release;
        _launcherPath = LauncherLocator.Find();
        UpdateBannerTitle.Text = release.BannerText;
        UpdateBannerDetail.Text = UpdateChecker.BannerDetail(UpdateChecker.CurrentVersion, launcherFound: _launcherPath is not null);
        BtnVerNovedades.NavigateUri = new Uri(release.Url);
        BtnAbrirLanzador.Visibility = _launcherPath is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void OnAbrirLanzadorClick(object sender, RoutedEventArgs e)
    {
        if (_launcherPath is { } launcher) TryOpenLauncher(launcher);
    }

    private void OnMasTardeClick(object sender, RoutedEventArgs e)
    {
        _prefs.DismissedVersion = _offeredRelease?.Tag;
        UpdateBanner.Visibility = Visibility.Collapsed;
        SavePreferences();
    }

    private void OnAvisarVersionToggled(object sender, RoutedEventArgs e)
    {
        if (_applyingPreferences || BtnEjecutar is null) return;
        SavePreferences();
    }

    private void OnBuscarActualizacionesClick(object sender, RoutedEventArgs e)
    {
        if (!BtnBuscarActualizaciones.IsEnabled) return;
        _ = CheckForUpdatesAsync(manual: true);
    }

    #endregion

    #region Atajos de teclado

    private void OnShortcutInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        bool ctrl = sender.Modifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control);
        ShortcutAction action = KeyboardShortcuts.Resolve(sender.Key.ToString(), ctrl, _taskController.IsBusy, _videoInfo is not null);
        switch (action)
        {
            case ShortcutAction.OpenVideo:
                OnElegirVideoClick(sender, new RoutedEventArgs());
                break;
            case ShortcutAction.Run:
                OnEjecutarClick(sender, new RoutedEventArgs());
                break;
            case ShortcutAction.Cancel:
                OnCancelarClick(sender, new RoutedEventArgs());
                break;
            default:
                return;
        }
        args.Handled = true;
    }

    #endregion

    private void ClearNotification()
    {
        NotificationBar.IsOpen = false;
    }

    #region Carga de Video y Drag & Drop

    private void OnVideoDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void OnVideoDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        if (_taskController.IsBusy)
        {
            ShowNotification("Espera a que termine o cancela antes de soltar más videos.", InfoBarSeverity.Warning);
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        List<string> videos = BatchQueue.FilterDroppedPaths(items.OfType<StorageFile>().Select(f => f.Path));
        if (videos.Count == 0)
        {
            ShowNotification("Lo soltado no es un video (mp4, mov, webm, mkv, avi, ts…).", InfoBarSeverity.Warning);
            return;
        }

        _queue.Clear();
        int queued = _queue.Enqueue(videos, TaskSelector.SelectedIndex);
        RefreshQueueList();
        await OpenVideoFileAsync(videos[0]);
        if (videos.Count > 1 && queued == 1)
        {
            // «Seguir» pide un clic en cada video: la cola no la admite
            ShowNotification(BatchQueue.NotQueuedText(videos.Count, System.IO.Path.GetFileName(videos[0])), InfoBarSeverity.Warning);
        }
        else if (videos.Count > 1)
        {
            ShowNotification($"{videos.Count} videos en cola. «Ejecutar» aplica la herramienta a todos, uno tras otro, con el resultado {WhereResultsGo()}.", InfoBarSeverity.Informational);
        }
    }

    private async void OnElegirVideoClick(object sender, RoutedEventArgs e)
    {
        string? path = FileDialogs.OpenSingle(_hwnd, "Elegir archivo de video", _prefs.LastFolderIfExists, FileDialogs.VideoFilter, FileDialogs.AllFilter);
        if (!string.IsNullOrEmpty(path))
        {
            if (!_taskController.IsBusy)
            {
                _queue.Clear();
                RefreshQueueList();
            }
            await OpenVideoFileAsync(path);
        }
    }

    public async Task OpenVideoFileAsync(string videoPath)
    {
        ClearNotification();
        Pause();

        try
        {
            VideoInfo info = await FfmpegTool.ProbeAsync(videoPath);
            _currentVideoPath = videoPath;
            _videoInfo = info;
            _prefs.RememberFile(videoPath);
            _prefs.TrySave(UserPreferences.DefaultPath);

            _timeModel = new PlayerTimeModel(info.EstimatedFrames, info.Fps, info.DurationSeconds);
            _transformer = new ViewportCoordinateTransformer(info.Width, info.Height);
            _transformer.UpdateViewport(ViewportContainer.ActualWidth, ViewportContainer.ActualHeight);

            _frameBuffer?.Dispose();
            _frameBuffer = new PlayerFrameBuffer(videoPath, info);

            // Actualizar interfaz
            VideoMetaText.Text = $"{System.IO.Path.GetFileName(videoPath)} ({info.Width}×{info.Height} · {info.Fps:0.##} fps · {PlayerTimeModel.FormatTime(info.DurationSeconds)})";
            EmptyStatePrompt.Visibility = Visibility.Collapsed;
            VideoDisplay.Visibility = Visibility.Visible;

            TimeSlider.Minimum = 0;
            TimeSlider.Maximum = Math.Max(1, _timeModel.TotalFrames - 1);
            TimeSlider.Value = 0;

            // Limpiar resultados anteriores
            _followResult = null;
            _detectionResult = null;
            _lastOutputFile = null;
            _selectedPoint = null;
            _selectedRegion = null;
            SeguirTargetLabel.Text = "Objetivo: Sin seleccionar";
            BtnExportar.IsEnabled = false;
            UpdateResultButtons();
            BtnVistaPrevia.IsEnabled = true;
            if (PreviewOn) BtnVistaPrevia.IsChecked = false;

            // Precargar y mostrar primer cuadro
            await _frameBuffer.PreloadRangeAsync(0, Math.Min(30, info.EstimatedFrames));
            await ShowFrameAsync(0);

            _playbackTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(16.0, 1000.0 / info.Fps));
        }
        catch (Exception ex)
        {
            ShowNotification(TaskExecutionController.FormatErrorMessage(ex), InfoBarSeverity.Error);
        }
    }

    #endregion

    #region Reproductor de Cuadros

    private async Task ShowFrameAsync(int frameIndex)
    {
        if (_frameBuffer is null || _timeModel is null || _videoInfo is null) return;

        _timeModel.SeekFrame(frameIndex);
        TimeText.Text = _timeModel.FormatPositionString();

        if (!_isScrubbing)
        {
            TimeSlider.Value = _timeModel.CurrentFrame;
        }

        byte[]? frameBytes = await _frameBuffer.GetFrameAsync(frameIndex);
        if (frameBytes is not null)
        {
            RenderFrameToBitmap(frameBytes, _videoInfo.Width, _videoInfo.Height);
        }

        UpdateOverlay();
    }

    private void RenderFrameToBitmap(byte[] bgra, int width, int height)
    {
        if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
        {
            _bitmap = new WriteableBitmap(width, height);
            VideoDisplay.Source = _bitmap;
        }

        // Como en Rodaje: la extensión de CsWinRT copia al IBuffer sin COM clásico, que Native AOT no admite.
        bgra.CopyTo(_bitmap.PixelBuffer);
        _bitmap.Invalidate();
    }

    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_isPlaying) Pause();
        else Play();
    }

    private void Play()
    {
        if (_timeModel is null) return;
        if (!_timeModel.CanStepForward)
        {
            _timeModel.SeekFrame(0);
        }
        _isPlaying = true;
        BtnPlayPause.Content = "⏸";
        _playbackTimer.Start();
    }

    private void Pause()
    {
        _isPlaying = false;
        BtnPlayPause.Content = "▶";
        _playbackTimer.Stop();
    }

    private async void OnPlaybackTick(object? sender, object e)
    {
        if (_timeModel is null) return;

        if (_timeModel.CanStepForward)
        {
            int next = _timeModel.StepForward();
            await ShowFrameAsync(next);
        }
        else
        {
            Pause();
        }
    }

    private async void OnStepBackClick(object sender, RoutedEventArgs e)
    {
        Pause();
        if (_timeModel is not null && _timeModel.CanStepBackward)
        {
            await ShowFrameAsync(_timeModel.StepBackward());
        }
    }

    private async void OnStepForwardClick(object sender, RoutedEventArgs e)
    {
        Pause();
        if (_timeModel is not null && _timeModel.CanStepForward)
        {
            await ShowFrameAsync(_timeModel.StepForward());
        }
    }

    private async void OnTimeSliderValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_timeModel is null || _isPlaying) return;

        int targetFrame = (int)Math.Round(e.NewValue);
        if (targetFrame != _timeModel.CurrentFrame)
        {
            _isScrubbing = true;
            await ShowFrameAsync(targetFrame);
            _isScrubbing = false;
        }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_transformer is not null && e.NewSize.Width > 0 && e.NewSize.Height > 0)
        {
            _transformer.UpdateViewport(e.NewSize.Width, e.NewSize.Height);
            UpdateOverlay();
        }
    }

    #endregion

    #region Superposiciones y Selección de Objetivo

    private void OnOverlayPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_transformer is null || _videoInfo is null) return;

        var pt = e.GetCurrentPoint(OverlayCanvas).Position;
        if (!_transformer.TryViewportToVideo(pt.X, pt.Y, out double vx, out double vy)) return;

        if (TaskSelector.SelectedIndex == 0) // Seguir
        {
            if (RbSeguirPunto.IsChecked == true)
            {
                _selectedPoint = new PointTargetSpec(vx, vy);
                _selectedRegion = null;
                SeguirTargetLabel.Text = string.Format(CultureInfo.InvariantCulture, "Objetivo: Punto (X: {0:0}, Y: {1:0})", vx, vy);
                UpdateOverlay();
            }
            else
            {
                _isDraggingSelection = true;
                _dragStartVx = pt.X;
                _dragStartVy = pt.Y;
                OverlayCanvas.CapturePointer(e.Pointer);
            }
        }
    }

    private void OnOverlayPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDraggingSelection || _transformer is null) return;

        var pt = e.GetCurrentPoint(OverlayCanvas).Position;
        if (_transformer.TryViewportDragToVideoRect(_dragStartVx, _dragStartVy, pt.X, pt.Y, out UiRect vRect))
        {
            _selectedRegion = new RegionTargetSpec(vRect.X, vRect.Y, vRect.Width, vRect.Height);
            _selectedPoint = null;
            SeguirTargetLabel.Text = string.Format(CultureInfo.InvariantCulture,
                "Objetivo: Región (X: {0:0}, Y: {1:0}, {2:0}×{3:0})", vRect.X, vRect.Y, vRect.Width, vRect.Height);
            UpdateOverlay();
        }
    }

    private void OnOverlayPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDraggingSelection)
        {
            _isDraggingSelection = false;
            OverlayCanvas.ReleasePointerCapture(e.Pointer);
            UpdateOverlay();
        }
    }

    private void UpdateOverlay()
    {
        OverlayCanvas.Children.Clear();
        if (_transformer is null || _videoInfo is null || _timeModel is null) return;

        int curFrame = _timeModel.CurrentFrame;

        // 0. Retícula fina sobre el cuadro (sextos), solo aspecto
        UiRect content = _transformer.VideoContentRect;
        for (int i = 1; i < 6; i++)
        {
            double x = content.X + content.Width * i / 6;
            double y = content.Y + content.Height * i / 6;
            OverlayCanvas.Children.Add(new Line { X1 = x, Y1 = content.Y, X2 = x, Y2 = content.Bottom, Stroke = ReticleBrush, StrokeThickness = 1 });
            OverlayCanvas.Children.Add(new Line { X1 = content.X, Y1 = y, X2 = content.Right, Y2 = y, Stroke = ReticleBrush, StrokeThickness = 1 });
        }

        // 1. Dibujar objetivo seleccionado para Seguir
        if (TaskSelector.SelectedIndex == 0)
        {
            if (_selectedPoint is { } p)
            {
                (double px, double py) = _transformer.VideoToViewport(p.X, p.Y);
                // Círculo y punto central
                Ellipse circle = new()
                {
                    Width = 18,
                    Height = 18,
                    Stroke = AccentBrush,
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(circle, px - 9);
                Canvas.SetTop(circle, py - 9);
                OverlayCanvas.Children.Add(circle);
                // Cruz de mira alrededor del círculo
                OverlayCanvas.Children.Add(new Line { X1 = px - 16, Y1 = py, X2 = px - 10, Y2 = py, Stroke = AccentBrush, StrokeThickness = 2 });
                OverlayCanvas.Children.Add(new Line { X1 = px + 10, Y1 = py, X2 = px + 16, Y2 = py, Stroke = AccentBrush, StrokeThickness = 2 });
                OverlayCanvas.Children.Add(new Line { X1 = px, Y1 = py - 16, X2 = px, Y2 = py - 10, Stroke = AccentBrush, StrokeThickness = 2 });
                OverlayCanvas.Children.Add(new Line { X1 = px, Y1 = py + 10, X2 = px, Y2 = py + 16, Stroke = AccentBrush, StrokeThickness = 2 });
            }
            else if (_selectedRegion is { } r)
            {
                UiRect rect = _transformer.VideoRectToViewport(new UiRect(r.X, r.Y, r.W, r.H));
                Rectangle box = new()
                {
                    Width = Math.Max(2, rect.Width),
                    Height = Math.Max(2, rect.Height),
                    Stroke = AccentBrush,
                    StrokeThickness = 2,
                    StrokeDashArray = [4, 2],
                };
                Canvas.SetLeft(box, rect.X);
                Canvas.SetTop(box, rect.Y);
                OverlayCanvas.Children.Add(box);
            }

            // Trayectoria seguida tras ejecutar
            if (_followResult is not null)
            {
                (List<OverlayPoint> pts, OverlayPoint? cur) = OverlayGeometry.BuildTrajectoryOverlay(_followResult.Cuadros, curFrame, _transformer);
                if (pts.Count > 1)
                {
                    PointCollection points = [];
                    foreach (var pt in pts) points.Add(new Windows.Foundation.Point(pt.X, pt.Y));
                    Polyline polyline = new()
                    {
                        Points = points,
                        Stroke = TrailBrush,
                        StrokeThickness = 2,
                    };
                    OverlayCanvas.Children.Add(polyline);
                }

                if (cur is { } cp)
                {
                    Ellipse curMarker = new()
                    {
                        Width = 14,
                        Height = 14,
                        Fill = MarkerFillBrush,
                        Stroke = AccentBrush,
                        StrokeThickness = 2,
                    };
                    Canvas.SetLeft(curMarker, cp.X - 7);
                    Canvas.SetTop(curMarker, cp.Y - 7);
                    OverlayCanvas.Children.Add(curMarker);
                }
            }
        }
        // 2. Detecciones de rostros / personas
        else if (TaskSelector.SelectedIndex == 2 && _detectionResult is not null)
        {
            List<OverlayBox> boxes = OverlayGeometry.BuildDetectionsOverlay(_detectionResult, curFrame, _transformer);
            foreach (var b in boxes)
            {
                Rectangle box = new()
                {
                    Width = Math.Max(2, b.Rect.Width),
                    Height = Math.Max(2, b.Rect.Height),
                    Stroke = AccentBrush,
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(box, b.Rect.X);
                Canvas.SetTop(box, b.Rect.Y);
                OverlayCanvas.Children.Add(box);

                TextBlock label = new()
                {
                    Text = b.Label,
                    FontSize = 11,
                    FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
                    Foreground = AccentBrush,
                };
                Border badge = new()
                {
                    Background = BadgeBrush,
                    Padding = new Thickness(4, 2, 4, 2),
                    CornerRadius = new CornerRadius(3),
                    Child = label,
                };
                Canvas.SetLeft(badge, b.Rect.X);
                Canvas.SetTop(badge, Math.Max(0, b.Rect.Y - 20));
                OverlayCanvas.Children.Add(badge);
            }
        }
        // 3. Guía de Reencuadre
        else if (TaskSelector.SelectedIndex == 3)
        {
            double aspect = OverlayGeometry.AspectRatioFromPreset(CurrentAspectPreset());
            UiRect cropVideo = OverlayGeometry.ComputeCropRect(_videoInfo.Width, _videoInfo.Height, aspect);
            UiRect cropView = _transformer.VideoRectToViewport(cropVideo);

            Rectangle guide = new()
            {
                Width = Math.Max(2, cropView.Width),
                Height = Math.Max(2, cropView.Height),
                Stroke = GuideBrush,
                StrokeThickness = 2,
                StrokeDashArray = [6, 3],
            };
            Canvas.SetLeft(guide, cropView.X);
            Canvas.SetTop(guide, cropView.Y);
            OverlayCanvas.Children.Add(guide);
        }
    }

    #endregion

    #region Tareas y Ejecución

    private void OnTaskSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelSeguir is null) return;

        int idx = TaskSelector.SelectedIndex;
        PanelSeguir.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelQuitarFondo.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        PanelDetectar.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
        PanelReencuadrar.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
        PanelEstabilizar.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

        // Con «Seguir» (que pide un clic) la cola se queda solo con el video cargado
        if (!_taskController.IsBusy && _queue.TrimToTool(idx, _currentVideoPath))
        {
            RefreshQueueList();
            ShowNotification(BatchQueue.TrimmedText, InfoBarSeverity.Informational);
        }

        UpdateOverlay();
        _ = RefreshPreviewAsync();
    }

    private void OnSeguirModeChanged(object sender, RoutedEventArgs e)
    {
        _selectedPoint = null;
        _selectedRegion = null;
        if (SeguirTargetLabel != null) SeguirTargetLabel.Text = "Objetivo: Sin seleccionar";
        UpdateOverlay();
    }

    private void OnAspectChanged(object sender, RoutedEventArgs e)
    {
        UpdateOverlay();
        _ = RefreshPreviewAsync();
    }

    private void OnFondoModoToggled(object sender, RoutedEventArgs e) => _ = RefreshPreviewAsync();

    private void OnFondoParamChanged(object sender, SelectionChangedEventArgs e) => _ = RefreshPreviewAsync();

    #region Vista previa antes/después

    private async void OnVistaPreviaToggled(object sender, RoutedEventArgs e)
    {
        if (PreviewOn && _timeModel is not null)
        {
            Pause();
            await ShowFrameAsync(0);
        }
        await RefreshPreviewAsync();
    }

    /// <summary>
    /// Con «Después» pulsado, muestra el primer cuadro con la herramienta aplicada (<see cref="FramePreview"/>): el recorte
    /// del reencuadre al instante; el fondo quitado pidiendo la máscara de ese cuadro al módulo, con la petición anterior
    /// cancelada. El módulo (<see cref="_previewSession"/>) se arranca con la primera máscara y sigue vivo, aunque se cambie
    /// de herramienta o de parámetro, hasta que se suelta «Después».
    /// </summary>
    private async Task RefreshPreviewAsync()
    {
        if (BtnVistaPrevia is null || PreviewStatusText is null || TaskSelector is null) return;

        _previewCts?.Cancel();
        _previewCts = null;
        if (!PreviewOn)
        {
            _ = ClosePreviewSessionAsync();
        }

        int taskIdx = TaskSelector.SelectedIndex;
        bool show = PreviewOn && _frameBuffer is not null && _videoInfo is not null;
        if (!show || !FramePreview.SupportsPreview(taskIdx))
        {
            PreviewStatusText.Text = show ? FramePreview.NotSupportedText : string.Empty;
            SetPreviewVisible(false);
            return;
        }

        byte[]? frame = await _frameBuffer!.GetFrameAsync(0);
        if (frame is null || !PreviewOn)
        {
            SetPreviewVisible(false);
            return;
        }

        int w = _videoInfo!.Width;
        int h = _videoInfo.Height;
        if (taskIdx == 3)
        {
            RenderPreview(FramePreview.CropForAspect(frame, w, h, CurrentAspectPreset()));
            PreviewStatusText.Text = $"Después: reencuadre {CurrentAspectPreset()} centrado";
            SetPreviewVisible(true);
            return;
        }

        CancellationTokenSource cts = new();
        _previewCts = cts;
        PreviewStatusText.Text = "Calculando la vista previa del fondo…";
        try
        {
            _previewSession ??= VisionService.CreateSession();
            byte[] mask = await VisionService.MatteFrameAsync(
                _previewSession, frame, w, h,
                quality: ComboCalidadFondo.SelectedIndex == 1 ? "alta" : "normal",
                device: DeviceName(ComboDeviceFondo.SelectedIndex),
                cancellationToken: cts.Token);
            if (cts.IsCancellationRequested || !PreviewOn) return;
            RenderPreview(FramePreview.ApplyMask(frame, mask, w, h, asMask: SwitchFondoModo.IsOn));
            PreviewStatusText.Text = SwitchFondoModo.IsOn ? "Después: máscara en grises" : "Después: fondo quitado";
            SetPreviewVisible(true);
        }
        catch (OperationCanceledException)
        {
            // Cambió otro parámetro antes de acabar: la petición nueva manda.
        }
        catch (Exception ex)
        {
            PreviewStatusText.Text = TaskExecutionController.FormatErrorMessage(ex);
            SetPreviewVisible(false);
        }
    }

    /// <summary>Cierra el módulo de la vista previa (al soltar «Después», al cerrar la ventana); la próxima máscara arrancaría otro.</summary>
    private async Task ClosePreviewSessionAsync()
    {
        if (_previewSession is not { } session) return;
        _previewSession = null;
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception ex) when (ex is ModuleException or IOException or InvalidOperationException)
        {
            // Un módulo que no cierra bien: ya no se usa; el Job Object y «Cancelar» cubren lo que quede.
        }
    }

    private void SetPreviewVisible(bool visible)
    {
        PreviewDisplay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        VideoDisplay.Visibility = visible || _videoInfo is null ? Visibility.Collapsed : Visibility.Visible;
        OverlayCanvas.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void RenderPreview(PreviewFrame preview)
    {
        if (_previewBitmap is null || _previewBitmap.PixelWidth != preview.Width || _previewBitmap.PixelHeight != preview.Height)
        {
            _previewBitmap = new WriteableBitmap(preview.Width, preview.Height);
            PreviewDisplay.Source = _previewBitmap;
        }
        preview.Bgra.CopyTo(_previewBitmap.PixelBuffer);
        _previewBitmap.Invalidate();
    }

    #endregion

    private async void OnEjecutarClick(object sender, RoutedEventArgs e)
    {
        if (_currentVideoPath is null || _videoInfo is null)
        {
            ShowNotification("Debes elegir o soltar un archivo de video primero.", InfoBarSeverity.Warning);
            return;
        }

        int taskIdx = TaskSelector.SelectedIndex;
        if (taskIdx == 0 && _selectedPoint is null && _selectedRegion is null)
        {
            ShowNotification("Selecciona un punto con un clic o una región arrastrando sobre el video.", InfoBarSeverity.Warning);
            return;
        }

        if (taskIdx is 1 or 2 or 3 && !VisionService.IsModuleInstalled())
        {
            ShowModuleMissing();
            return;
        }

        ClearNotification();
        Pause();
        SavePreferences();

        // Al reanudar, el cancelado a medias se repite. Sin pendientes: con varios en cola se repite la cola entera; si no, el video cargado es la cola.
        if (_queue.Count > 1) _queue.RequeueCancelled();
        if (!_queue.HasPending)
        {
            if (_queue.Count > 1)
            {
                _queue.Reset();
            }
            else
            {
                _queue.Clear();
                _queue.Enqueue([_currentVideoPath]);
            }
        }

        CancellationToken ct = _taskController.BeginExecution();
        SetRunningState(true);

        Progress<VideoProgress> progress = new(p =>
        {
            _queue.ReportProgress(p.Fraction, p.Stage);
            _taskController.ReportProgress(_queue.OverallFraction, _queue.ProgressText);
            UiProgressInfo info = _taskController.GetProgressInfo();
            TaskProgressBar.Value = info.Fraction * 100.0;
            TaskProgressText.Text = info.StatusText;
            TaskTimeText.Text = info.FormattedTime;
            RefreshQueueList();
        });

        bool cancelled = false;
        while (_queue.StartNext() is { } item)
        {
            RefreshQueueList();
            try
            {
                string output = await RunToolAsync(item.Path, taskIdx, progress, ct);
                _queue.MarkDone(output);
                _lastOutputFile = output;
            }
            catch (OperationCanceledException)
            {
                if (_taskController.CancelTask is { } cancelTask) await cancelTask; // hasta que el kill termine y LeftAliveAfterCancel esté puesto
                _queue.MarkCancelled();
                cancelled = true;
                break;
            }
            catch (VisionModuleMissingException)
            {
                // Desapareció entre la comprobación y el arranque: no tiene sentido seguir con la cola.
                _queue.MarkFailed("falta el módulo de visión");
                RefreshQueueList();
                _taskController.MarkFaulted(new RastrearUserException(VisionModuleMissingException.UserMessage(launcherFound: false)));
                SetRunningState(false);
                ShowModuleMissing();
                return;
            }
            catch (Exception ex)
            {
                _queue.MarkFailed(TaskExecutionController.FormatErrorMessage(ex));
            }
            RefreshQueueList();
        }

        if (cancelled)
        {
            _taskController.MarkCancelled();
            string alive = _taskController.LeftAliveAfterCancel.Count == 0
                ? string.Empty
                : $" Seguía vivo: {string.Join(", ", _taskController.LeftAliveAfterCancel)}.";
            ShowNotification($"Operación cancelada por el usuario.{alive} {_queue.Summary}.", InfoBarSeverity.Informational);
        }
        else
        {
            bool anyFailed = _queue.Items.Any(i => i.State == BatchItemState.Failed);
            if (_queue.Items.All(i => i.State == BatchItemState.Failed))
            {
                _taskController.MarkFaulted(new RastrearUserException(_queue.Count == 1
                    ? _queue.Items[0].Error ?? "Error durante la ejecución."
                    : $"Ningún video salió bien: {_queue.Summary}. Primero: {_queue.Items[0].Error}"));
                ShowNotification(_taskController.LastError ?? "Error durante la ejecución.", InfoBarSeverity.Error);
            }
            else
            {
                _taskController.MarkCompleted();
                string where = _lastOutputFile is { } o ? $" Resultado en: {System.IO.Path.GetFileName(o)}" : string.Empty;
                ShowNotification($"{ToolDoneText(taskIdx)}: {_queue.Summary}.{where}", anyFailed ? InfoBarSeverity.Warning : InfoBarSeverity.Success);
            }
        }

        BtnExportar.IsEnabled = _followResult is not null || _detectionResult is not null || (_lastOutputFile is not null && File.Exists(_lastOutputFile));
        UpdateResultButtons();
        SetRunningState(false);
        UpdateOverlay();
    }

    /// <summary>«junto a cada video» o «en <carpeta>», para los avisos.</summary>
    private string WhereResultsGo() => _prefs.OutputFolder is { } f ? $"en {f}" : "junto a cada video";

    private static string ToolDoneText(int taskIdx) => taskIdx switch
    {
        0 => "Seguimiento completado",
        1 => "Fondo procesado",
        2 => "Detección completada",
        3 => "Reencuadre completado",
        _ => "Estabilización completada",
    };

    /// <summary>
    /// Ejecuta la herramienta sobre un video y deja el resultado junto a él (<see cref="OutputNaming"/>): CSV del seguimiento,
    /// JSON de las detecciones o el video procesado. Si es el video cargado, también guarda el resultado para la superposición.
    /// </summary>
    private async Task<string> RunToolAsync(string videoPath, int taskIdx, IProgress<VideoProgress> progress, CancellationToken ct)
    {
        bool isDisplayed = string.Equals(videoPath, _currentVideoPath, StringComparison.OrdinalIgnoreCase);
        string output = OutputNaming.OutputPathFor(videoPath, taskIdx, SwitchFondoModo.IsOn, outputFolder: _prefs.OutputFolder);

        try
        {
            // La carpeta de salida elegida puede haberse ido (una unidad extraíble): el error queda en ese archivo de la cola
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output)!);
            await RunToolCoreAsync(videoPath, output, taskIdx, isDisplayed, progress, ct);
        }
        catch
        {
            // Cancelado o fallido: el archivo a medias no se queda junto al video como si fuera un resultado.
            try { File.Delete(output); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
        return output;
    }

    private async Task RunToolCoreAsync(string videoPath, string output, int taskIdx, bool isDisplayed, IProgress<VideoProgress> progress, CancellationToken ct)
    {
        switch (taskIdx)
        {
            case 0: // Seguir: el mismo punto o región en todos los videos de la cola
                FollowResult follow = await FollowService.FollowAsync(
                    videoPath, _selectedPoint, _selectedRegion,
                    patternSize: (int)SliderPatternSize.Value, progress: progress, cancellationToken: ct);
                using (StreamWriter sw = new(output))
                {
                    FollowService.WriteCsv(follow, sw);
                }
                if (isDisplayed) _followResult = follow;
                break;

            case 1: // Quitar fondo
                await VisionService.RemoveBackgroundAsync(
                    videoPath, output,
                    format: SwitchFondoModo.IsOn ? "mascara" : "alfa",
                    quality: ComboCalidadFondo.SelectedIndex == 1 ? "alta" : "normal",
                    device: DeviceName(ComboDeviceFondo.SelectedIndex),
                    progress: progress, cancellationToken: ct);
                break;

            case 2: // Detectar
                DetectionResult detection = await VisionService.DetectAsync(
                    videoPath,
                    target: ComboTargetDetectar.SelectedIndex == 1 ? "personas" : "rostros",
                    device: DeviceName(ComboDeviceDetectar.SelectedIndex),
                    progress: progress, cancellationToken: ct);
                await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(detection, RastrearJsonContext.Default.DetectionResult), ct);
                if (isDisplayed) _detectionResult = detection;
                break;

            case 3: // Reencuadrar
                await VisionService.ReframeAsync(
                    videoPath, output,
                    targetAspect: OverlayGeometry.AspectRatioFromPreset(CurrentAspectPreset()),
                    smoothing: SliderSmoothing.Value,
                    progress: progress, cancellationToken: ct);
                break;

            case 4: // Estabilizar
                await StabilizeService.StabilizeAsync(
                    videoPath, output,
                    intensity: SliderIntensity.Value,
                    autoCrop: ChkAutoCrop.IsChecked == true,
                    progress: progress, cancellationToken: ct);
                break;
        }
    }

    #region Cola de archivos

    private void RefreshQueueList()
    {
        bool show = _queue.Count > 1;
        QueuePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;

        QueueTitle.Text = $"Cola: {_queue.Count} videos";
        QueueList.Children.Clear();
        foreach (BatchItem item in _queue.Items)
        {
            Grid row = new() { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            TextBlock name = new()
            {
                Text = item.FileName,
                FontSize = 12,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = (Brush)Application.Current.Resources[item.State == BatchItemState.Running ? "TextBrush" : "TextMutedBrush"],
            };
            TextBlock status = new()
            {
                Text = item.StatusText,
                FontSize = 12,
                FontFamily = (FontFamily)Application.Current.Resources["MonoFont"],
                Foreground = (Brush)Application.Current.Resources[item.State switch
                {
                    BatchItemState.Done => "AccentTextBrush",
                    BatchItemState.Running => "TextBrush",
                    _ => "TextMutedBrush",
                }],
            };
            Grid.SetColumn(status, 1);
            row.Children.Add(name);
            row.Children.Add(status);
            QueueList.Children.Add(row);
        }
        BtnVaciarCola.IsEnabled = !_taskController.IsBusy;
    }

    private void OnVaciarColaClick(object sender, RoutedEventArgs e)
    {
        if (_taskController.IsBusy) return;
        _queue.Clear();
        RefreshQueueList();
    }

    #endregion

    private async void OnCancelarClick(object sender, RoutedEventArgs e)
    {
        BtnCancelar.IsEnabled = false;
        await _taskController.CancelAsync();
    }

    private void SetRunningState(bool running)
    {
        BtnEjecutar.IsEnabled = !running;
        BtnCancelar.IsEnabled = running;
        TaskSelector.IsEnabled = !running;
        BtnElegirVideo.IsEnabled = !running;
        BtnVaciarCola.IsEnabled = !running;
        BtnElegirCarpetaSalida.IsEnabled = !running;
        BtnCarpetaSalidaJunto.IsEnabled = !running && _prefs.OutputFolder is not null;
        BtnVistaPrevia.IsEnabled = !running && _videoInfo is not null;
        if (running && PreviewOn) BtnVistaPrevia.IsChecked = false;

        TaskProgressBar.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        TaskTimeText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        bool done = !running && _taskController.State == ExecutionState.Completed;
        TaskProgressText.Visibility = running || done ? Visibility.Visible : Visibility.Collapsed;
        DoneMark.Visibility = done ? Visibility.Visible : Visibility.Collapsed;
        if (done)
        {
            TaskProgressText.Text = $"Hecho en {TaskExecutionController.FormatMinSec(_taskController.GetProgressInfo().Elapsed)}";
            DoneStoryboard.Begin();
        }
    }

    #endregion

    #region Exportación

    private void UpdateResultButtons()
    {
        bool has = _lastOutputFile is not null && File.Exists(_lastOutputFile);
        BtnAbrirCarpeta.IsEnabled = has;
        BtnCopiarRuta.IsEnabled = has;
    }

    private void OnAbrirCarpetaClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!ResultActions.OpenFolder(_lastOutputFile))
            {
                ShowNotification("El resultado ya no está donde se dejó.", InfoBarSeverity.Warning);
                UpdateResultButtons();
            }
        }
        catch (Exception ex)
        {
            ShowNotification(TaskExecutionController.FormatErrorMessage(ex), InfoBarSeverity.Error);
        }
    }

    private void OnCopiarRutaClick(object sender, RoutedEventArgs e)
    {
        if (_lastOutputFile is null) return;
        DataPackage package = new();
        package.SetText(ResultActions.ClipboardText(_lastOutputFile));
        Clipboard.SetContent(package);
        ShowNotification($"Ruta copiada: {_lastOutputFile}", InfoBarSeverity.Success);
    }

    /// <summary>Copia el resultado donde se eligió; elegir el propio resultado no es un error, solo no hay nada que copiar.</summary>
    private static void CopyResult(string result, string savePath)
    {
        if (string.Equals(System.IO.Path.GetFullPath(result), System.IO.Path.GetFullPath(savePath), StringComparison.OrdinalIgnoreCase)) return;
        File.Copy(result, savePath, overwrite: true);
    }

    /// <summary>
    /// «Exportar resultado…»: una copia del archivo resultante donde se elija. El resultado sigue junto al video (o en la
    /// carpeta de salida): «Abrir carpeta» y «Copiar ruta» no cambian. El seguimiento se puede exportar también como JSON
    /// (se escribe desde el resultado en memoria); si el archivo ya no está, el seguimiento y las detecciones se vuelven a
    /// escribir desde memoria.
    /// </summary>
    private void OnExportarClick(object sender, RoutedEventArgs e)
    {
        int taskIdx = TaskSelector.SelectedIndex;
        string? result = _lastOutputFile is { } r && File.Exists(r) ? r : null;

        try
        {
            string? savePath;
            if (taskIdx == 0 && _followResult is not null)
            {
                savePath = FileDialogs.SaveSingle(_hwnd, "Exportar seguimiento", System.IO.Path.GetFileName(result ?? "seguimiento.csv"), _prefs.LastFolderIfExists, FileDialogs.CsvFilter, FileDialogs.JsonFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                if (savePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using StreamWriter sw = new(savePath);
                    FollowService.WriteJson(_followResult, sw);
                }
                else if (result is not null)
                {
                    CopyResult(result, savePath);
                }
                else
                {
                    using StreamWriter sw = new(savePath);
                    FollowService.WriteCsv(_followResult, sw);
                }
            }
            else if (taskIdx == 2 && _detectionResult is not null)
            {
                savePath = FileDialogs.SaveSingle(_hwnd, "Exportar detecciones", System.IO.Path.GetFileName(result ?? "detecciones.json"), _prefs.LastFolderIfExists, FileDialogs.JsonFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                if (result is not null)
                {
                    CopyResult(result, savePath);
                }
                else
                {
                    File.WriteAllText(savePath, System.Text.Json.JsonSerializer.Serialize(_detectionResult, RastrearJsonContext.Default.DetectionResult));
                }
            }
            else if (result is not null)
            {
                string ext = System.IO.Path.GetExtension(result);
                savePath = FileDialogs.SaveSingle(_hwnd, "Exportar resultado", System.IO.Path.GetFileName(result), _prefs.LastFolderIfExists, (ext.ToUpperInvariant(), $"*{ext}"), FileDialogs.AllFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                CopyResult(result, savePath);
            }
            else
            {
                ShowNotification("El resultado ya no está donde se dejó.", InfoBarSeverity.Warning);
                UpdateResultButtons();
                return;
            }

            _prefs.RememberFile(savePath);
            ShowNotification($"Copia exportada a: {savePath}", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowNotification(TaskExecutionController.FormatErrorMessage(ex), InfoBarSeverity.Error);
        }
    }

    #endregion
}
