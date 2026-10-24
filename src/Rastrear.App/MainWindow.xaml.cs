using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
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

    public MainWindow()
    {
        InitializeComponent();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Title = "Rastrear";

        _playbackTimer.Tick += OnPlaybackTick;

        // Medición de arranque en frío
        if (Program.MeasureStartup)
        {
            RecordStartup();
        }
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
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 229, 255)),
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(circle, px - 9);
                Canvas.SetTop(circle, py - 9);
                OverlayCanvas.Children.Add(circle);
            }
            else if (_selectedRegion is { } r)
            {
                UiRect rect = _transformer.VideoRectToViewport(new UiRect(r.X, r.Y, r.W, r.H));
                Rectangle box = new()
                {
                    Width = Math.Max(2, rect.Width),
                    Height = Math.Max(2, rect.Height),
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 229, 255)),
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
                        Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 255, 214, 0)),
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
                        Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 82, 82)),
                        Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
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
                    Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 0, 230, 118)),
                    StrokeThickness = 2,
                };
                Canvas.SetLeft(box, b.Rect.X);
                Canvas.SetTop(box, b.Rect.Y);
                OverlayCanvas.Children.Add(box);

                TextBlock label = new()
                {
                    Text = b.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255)),
                };
                Border badge = new()
                {
                    Background = new SolidColorBrush(Windows.UI.Color.FromArgb(200, 0, 150, 70)),
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
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 171, 0)),
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
            TaskProgressBar.Value = p.Fraction * 100.0;
            TaskProgressText.Text = $"{p.Fraction * 100:0}% - {p.Stage}";
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
        TaskProgressText.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
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
