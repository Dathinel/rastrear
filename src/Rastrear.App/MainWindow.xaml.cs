using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
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
        AppWindow.Changed += OnAppWindowChanged;
        Closed += (_, _) => SavePlacement();

        _playbackTimer.Tick += OnPlaybackTick;
        AboutVersionText.Text = "Versión " + (typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "desconocida");

        // Medición de arranque en frío
        if (Program.MeasureStartup)
        {
            RecordStartup();
        }
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

    private void ShowNotification(string message, InfoBarSeverity severity = InfoBarSeverity.Error)
    {
        NotificationBar.Message = message;
        NotificationBar.Severity = severity;
        NotificationBar.IsOpen = true;
    }

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

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count > 0 && items[0] is StorageFile file)
        {
            await OpenVideoFileAsync(file.Path);
        }
    }

    private async void OnElegirVideoClick(object sender, RoutedEventArgs e)
    {
        string? path = FileDialogs.OpenSingle(_hwnd, "Elegir archivo de video", FileDialogs.VideoFilter, FileDialogs.AllFilter);
        if (!string.IsNullOrEmpty(path))
        {
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
            string preset = RbAspect916.IsChecked == true ? "9:16" : (RbAspect11.IsChecked == true ? "1:1" : "4:5");
            double aspect = OverlayGeometry.AspectRatioFromPreset(preset);
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

        UpdateOverlay();
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
    }

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

        ClearNotification();
        Pause();

        CancellationToken ct = _taskController.BeginExecution();
        SetRunningState(true);

        Progress<VideoProgress> progress = new(p =>
        {
            _taskController.ReportProgress(p.Fraction, p.Stage);
            UiProgressInfo info = _taskController.GetProgressInfo();
            TaskProgressBar.Value = info.Fraction * 100.0;
            TaskProgressText.Text = $"{info.Fraction * 100:0} % · {info.StatusText}";
            TaskTimeText.Text = info.FormattedTime;
        });

        try
        {
            switch (taskIdx)
            {
                case 0: // Seguir
                    int patSize = (int)SliderPatternSize.Value;
                    _followResult = await FollowService.FollowAsync(
                        _currentVideoPath,
                        _selectedPoint,
                        _selectedRegion,
                        patternSize: patSize,
                        progress: progress,
                        cancellationToken: ct);
                    _taskController.MarkCompleted();
                    ShowNotification($"Seguimiento completado: {_followResult.Cuadros.Count} cuadros analizados.", InfoBarSeverity.Success);
                    BtnExportar.IsEnabled = true;
                    break;

                case 1: // Quitar fondo
                    bool esMascara = SwitchFondoModo.IsOn;
                    string extFondo = esMascara ? ".mp4" : ".webm";
                    string outFondo = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rastrear_fondo_{DateTime.Now:HHmmss}{extFondo}");
                    string formatFondo = esMascara ? "mascara" : "alfa";
                    string qualityFondo = ComboCalidadFondo.SelectedIndex == 1 ? "alta" : "normal";
                    string deviceFondo = ComboDeviceFondo.SelectedIndex switch { 1 => "gpu", 2 => "cpu", _ => "auto" };

                    await VisionService.RemoveBackgroundAsync(
                        _currentVideoPath,
                        outFondo,
                        format: formatFondo,
                        quality: qualityFondo,
                        device: deviceFondo,
                        progress: progress,
                        cancellationToken: ct);
                    _lastOutputFile = outFondo;
                    _taskController.MarkCompleted();
                    ShowNotification($"Fondo procesado con éxito en: {System.IO.Path.GetFileName(outFondo)}", InfoBarSeverity.Success);
                    BtnExportar.IsEnabled = true;
                    break;

                case 2: // Detectar
                    string targetDet = ComboTargetDetectar.SelectedIndex == 1 ? "personas" : "rostros";
                    string deviceDet = ComboDeviceDetectar.SelectedIndex switch { 1 => "gpu", 2 => "cpu", _ => "auto" };

                    _detectionResult = await VisionService.DetectAsync(
                        _currentVideoPath,
                        target: targetDet,
                        device: deviceDet,
                        progress: progress,
                        cancellationToken: ct);
                    _taskController.MarkCompleted();
                    ShowNotification($"Detección completada: {_detectionResult.Cuadros.Count} cuadros analizados.", InfoBarSeverity.Success);
                    BtnExportar.IsEnabled = true;
                    break;

                case 3: // Reencuadrar
                    string presetAspect = RbAspect916.IsChecked == true ? "9:16" : (RbAspect11.IsChecked == true ? "1:1" : "4:5");
                    double targetAspect = OverlayGeometry.AspectRatioFromPreset(presetAspect);
                    string outReframe = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rastrear_reencuadre_{DateTime.Now:HHmmss}.mp4");

                    await VisionService.ReframeAsync(
                        _currentVideoPath,
                        outReframe,
                        targetAspect: targetAspect,
                        smoothing: SliderSmoothing.Value,
                        progress: progress,
                        cancellationToken: ct);
                    _lastOutputFile = outReframe;
                    _taskController.MarkCompleted();
                    ShowNotification($"Reencuadre completado en: {System.IO.Path.GetFileName(outReframe)}", InfoBarSeverity.Success);
                    BtnExportar.IsEnabled = true;
                    break;

                case 4: // Estabilizar
                    string outStab = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rastrear_estabilizado_{DateTime.Now:HHmmss}.mp4");
                    await StabilizeService.StabilizeAsync(
                        _currentVideoPath,
                        outStab,
                        intensity: SliderIntensity.Value,
                        autoCrop: ChkAutoCrop.IsChecked == true,
                        progress: progress,
                        cancellationToken: ct);
                    _lastOutputFile = outStab;
                    _taskController.MarkCompleted();
                    ShowNotification($"Estabilización completada en: {System.IO.Path.GetFileName(outStab)}", InfoBarSeverity.Success);
                    BtnExportar.IsEnabled = true;
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            _taskController.MarkCancelled();
            ShowNotification("Operación cancelada por el usuario.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            _taskController.MarkFaulted(ex);
            ShowNotification(_taskController.LastError ?? "Error durante la ejecución.", InfoBarSeverity.Error);
        }
        finally
        {
            SetRunningState(false);
            UpdateOverlay();
        }
    }

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

    private void OnExportarClick(object sender, RoutedEventArgs e)
    {
        int taskIdx = TaskSelector.SelectedIndex;

        try
        {
            if (taskIdx == 0 && _followResult is not null)
            {
                string? savePath = FileDialogs.SaveSingle(_hwnd, "Guardar seguimiento", "seguimiento.csv", FileDialogs.CsvFilter, FileDialogs.JsonFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                using StreamWriter sw = new(savePath);
                if (savePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    FollowService.WriteJson(_followResult, sw);
                }
                else
                {
                    FollowService.WriteCsv(_followResult, sw);
                }
                ShowNotification($"Seguimiento exportado a: {savePath}", InfoBarSeverity.Success);
            }
            else if (taskIdx == 2 && _detectionResult is not null)
            {
                string? savePath = FileDialogs.SaveSingle(_hwnd, "Guardar detecciones", "detecciones.json", FileDialogs.JsonFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                string json = System.Text.Json.JsonSerializer.Serialize(_detectionResult, RastrearJsonContext.Default.DetectionResult);
                File.WriteAllText(savePath, json);
                ShowNotification($"Detecciones exportadas a: {savePath}", InfoBarSeverity.Success);
            }
            else if (_lastOutputFile is not null && File.Exists(_lastOutputFile))
            {
                string defName = System.IO.Path.GetFileName(_lastOutputFile);
                string ext = System.IO.Path.GetExtension(_lastOutputFile);
                string? savePath = FileDialogs.SaveSingle(_hwnd, "Exportar video resultante", defName, (ext.ToUpperInvariant(), $"*{ext}"), FileDialogs.AllFilter);
                if (string.IsNullOrEmpty(savePath)) return;

                File.Copy(_lastOutputFile, savePath, overwrite: true);
                ShowNotification($"Video exportado a: {savePath}", InfoBarSeverity.Success);
            }
        }
        catch (Exception ex)
        {
            ShowNotification(TaskExecutionController.FormatErrorMessage(ex), InfoBarSeverity.Error);
        }
    }

    #endregion
}
