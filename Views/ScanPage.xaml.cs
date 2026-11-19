/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Экран сканера документов (живая камера, съёмка, выбор фото и OCR-распознавание)
 * СОДЕРЖИМОЕ: ScanPage
 * ЗАВИСИМОСТИ: CommunityToolkit.Maui.Camera, GosTek.Services, GosTek.Models, GosTek.Views.Controls
 * ==============================================================================
 */
using CommunityToolkit.Maui.Views;
using GosTek.Models;
using GosTek.Services;
using GosTek.Views.Controls;

namespace GosTek.Views;

/// <summary>
/// Главный экран оптического сканера документов.
/// Что делает: отображает живой видоискатель камеры, делает снимок документа, 
/// позволяет загрузить фотографию из галереи или памяти устройства и запускает распознавание текста.
/// С чем взаимодействует: с CameraView (камера устройства), OcrService (оптическое распознавание),
/// DocumentDataExtractor (извлечение реквизитов) и хранилищем черновиков DraftStore.
/// Зачем это нужно: чтобы быстро сфотографировать любой документ и перенести распознанные данные в шаблон.
/// </summary>
[QueryProperty(nameof(Mode), "mode")]
public partial class ScanPage : ContentPage
{
    private string? _mode;
    private bool _busy;
    private int _copyVersion;
    private CancellationTokenSource? _processCts;

    // Живая камера в рамке: CameraView создаётся только после выдачи доступа к камере
    private CameraView? _camera;
    private readonly CameraLifecycleController _cameraController = new();
    private bool _previewOn;
    private bool _previewStarting;
    private bool _cameraNoteShown;   // в ErrorLabel сейчас сообщение про камеру (а не про распознавание)
    private int _visibilityToken;
    private bool _isPageVisible;
    private readonly List<string> _tempFilesToClean = new();

    private void RegisterTempFile(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            lock (_tempFilesToClean)
            {
                _tempFilesToClean.Add(path);
            }
        }
    }

    private void CleanupTempFiles()
    {
        lock (_tempFilesToClean)
        {
            foreach (var path in _tempFilesToClean)
            {
                try
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Не удалось удалить временный файл {path}: {ex.Message}");
                }
            }
            _tempFilesToClean.Clear();
        }
    }

    // "passport" — открыто из профиля («Заполнить по фото паспорта»):
    // после сохранения и по кнопке ✕ возвращаемся в профиль
    public string? Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            UpdateModeUi();
        }
    }

    public ScanPage()
    {
        InitializeComponent();
        BuildScanLines();
        ScanLoading.CancelRequested += (_, _) => CancelProcessing();
        ViewfinderArea.SizeChanged += (_, _) => UpdateViewfinderGeometry();
        this.Loaded += (_, _) => UpdateViewfinderGeometry();
#if WINDOWS
        DesktopDropZone.HandlerChanged += (_, _) => EnableDesktopDrop();
        EnableDesktopDrop();
#endif
    }

    private void UpdateModeUi()
    {
        bool isPassport = string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase);

        if (PageTitleLabel != null)
            PageTitleLabel.Text = IsDesktop ? "Оцифровка документов" : isPassport ? "Сканирование паспорта" : "Сканер документов";

        if (ViewfinderHintLabel != null)
            ViewfinderHintLabel.Text = isPassport
                ? "Поместите разворот паспорта или ID-карту с зоной MRZ в рамку."
                : "Поместите документ целиком в рамку при хорошем освещении.";

        UpdateViewfinderGeometry();
    }

    private void UpdateViewfinderGeometry()
    {
        if (ViewfinderContainer == null)
            return;

        bool isPassport = string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase);

        double availableW = ViewfinderArea?.Width > 50 ? ViewfinderArea.Width : (this.Width > 50 ? this.Width - 40 : 500);
        double availableH = ViewfinderArea?.Height > 50 ? ViewfinderArea.Height : (this.Height > 150 ? this.Height - 200 : 360);

        if (availableW > 600) availableW = 600;
        if (availableH > 400) availableH = 400;

        double targetW, targetH;
        if (isPassport)
        {
            // Пропорция паспорта / ID-карты (~1.42)
            targetW = Math.Min(availableW, 480);
            targetH = Math.Round(targetW / 1.42);
            if (targetH > availableH)
            {
                targetH = availableH;
                targetW = Math.Round(targetH * 1.42);
            }
        }
        else
        {
            // Стандартный кадр камеры 4:3 (~1.33)
            targetW = Math.Min(availableW, 520);
            targetH = Math.Round(targetW / 1.33);
            if (targetH > availableH)
            {
                targetH = availableH;
                targetW = Math.Round(targetH * 1.33);
            }
        }

        if (targetW > 0 && targetH > 0)
        {
            ViewfinderContainer.WidthRequest = targetW;
            ViewfinderContainer.HeightRequest = targetH;

            if (_camera is not null)
            {
                _camera.WidthRequest = targetW;
                _camera.HeightRequest = targetH;
            }
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        _isPageVisible = true;
        int token = ++_visibilityToken;

        UpdateModeUi();

        if (!IsDesktop && !ResultPanel.IsVisible)
        {
            await StartPreviewAsync(token);
            if (_isPageVisible && _visibilityToken == token)
                StartLaserAnimation();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        TryCloseDesktopImage();
        SettingsOverlay.Dismiss();
        _isPageVisible = false;
        ++_visibilityToken;

        // Камеру отпускаем при уходе с вкладки
        StopLaserAnimation();
        CancelProcessing();
        StopPreview();

        CleanupTempFiles();
        // Страница живёт между переходами: режим «из профиля» не должен прилипнуть.
        // Пока открыто окно выбора файла или идёт распознавание, его не трогаем.
        if (!_busy)
        {
            _mode = null;
            UpdateModeUi();
        }
    }

    // «Назад» на Android: из результата — к рамке, иначе — на главную (или в профиль)
    protected override bool OnBackButtonPressed()
    {
        if (_busy || ScanLoading.IsLoading) {
            StopLaserAnimation();
            CancelProcessing();
            return true;
        }

        if (ResultPanel.IsVisible) {
            ShowResult(false);
            return true;
        }

        string route = ReturnRoute();
        if (!_busy)
        {
            _mode = null;
            UpdateModeUi();
        }
        _ = Shell.Current.GoToAsync(route);
        return true;
    }

    private string ReturnRoute() => string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase) ? "//ProfilePage" : "//MainPage";

    // ===== Камера в рамке =====

    private static async Task<bool> EnsureCameraPermissionAsync()
    {
        try {
            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            return status == PermissionStatus.Granted;
        } catch (Exception) {
            // Платформа без запроса доступа (например, Windows): пробуем включить камеру как есть
            return true;
        }
    }

    private async Task StartPreviewAsync(int? expectedToken = null)
    {
        int token = expectedToken ?? _visibilityToken;

        if (_previewOn || _previewStarting || ResultPanel.IsVisible || !_isPageVisible || _visibilityToken != token)
            return;

        if (!_cameraController.TryBeginStart(token))
            return;

        _previewStarting = true;
        try {
            if (!await EnsureCameraPermissionAsync()) {
                _cameraController.Stop();
                if (_isPageVisible && _visibilityToken == token)
                    ShowCameraNote("Нет доступа к камере. Разрешите её в настройках или выберите фото через «Галерею» или «Файл».");
                return;
            }

            if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible)
            {
                _cameraController.Stop();
                return;
            }

            // Защита Windows/WinUI: даем WinUI 3 завершить первичное измерение и компоновку страницы
            await Task.Delay(100);

            // Ожидаем готовности компоновки контейнера видоискателя
            for (int i = 0; i < 40; i++)
            {
                if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible)
                {
                    _cameraController.Stop();
                    return;
                }

                UpdateViewfinderGeometry();

                double currentW = ViewfinderContainer?.WidthRequest > 0
                    ? ViewfinderContainer.WidthRequest
                    : (ViewfinderContainer?.Width > 0 ? ViewfinderContainer.Width : 0);
                double currentH = ViewfinderContainer?.HeightRequest > 0
                    ? ViewfinderContainer.HeightRequest
                    : (ViewfinderContainer?.Height > 0 ? ViewfinderContainer.Height : 0);

                if (currentW > 0 && currentH > 0 && (CameraHost.Width > 0 || ViewfinderArea.Width > 0 || this.Width > 0))
                    break;

                await Task.Delay(50);
            }

            if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible)
            {
                _cameraController.Stop();
                return;
            }

            double targetW = ViewfinderContainer?.WidthRequest > 0
                ? ViewfinderContainer.WidthRequest
                : (ViewfinderContainer?.Width > 0 ? ViewfinderContainer.Width : 480);
            double targetH = ViewfinderContainer?.HeightRequest > 0
                ? ViewfinderContainer.HeightRequest
                : (ViewfinderContainer?.Height > 0 ? ViewfinderContainer.Height : 360);

            if (targetW <= 0 || targetH <= 0)
            {
                _cameraController.Stop();
                return;
            }

            if (_camera is null) {
                _camera = new CameraView {
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                    WidthRequest = targetW,
                    HeightRequest = targetH,
                    IsVisible = false // Скрыт до завершения инициализации и проверки геометрии
                };
                CameraHost.Children.Add(_camera);
            }
            else
            {
                _camera.WidthRequest = targetW;
                _camera.HeightRequest = targetH;
                _camera.IsVisible = false;
            }

            // Ожидаем подключения нативного обработчика Handler
            for (int i = 0; i < 60 && _camera.Handler is null; i++)
            {
                if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible)
                {
                    _cameraController.Stop();
                    return;
                }
                await Task.Delay(50);
            }

            if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible)
            {
                _cameraController.Stop();
                return;
            }

            // Гарантируем ненулевые размеры CameraView перед запуском камеры
            if (_camera.WidthRequest <= 0 || _camera.HeightRequest <= 0)
            {
                _camera.WidthRequest = targetW;
                _camera.HeightRequest = targetH;
            }

            // Проверяем актуальные размеры элемента CameraView и контейнера (защита от NullReferenceException)
            double measuredW = _camera.Width > 0 ? _camera.Width : _camera.WidthRequest;
            double measuredH = _camera.Height > 0 ? _camera.Height : _camera.HeightRequest;
            if (measuredW <= 0 || measuredH <= 0)
            {
                _cameraController.Stop();
                return;
            }

            // Активируем видимость только когда все размеры проверены и заданы
            _camera.IsVisible = true;
            await Task.Delay(100);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _camera.StartCameraPreview(cts.Token);

            if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible || !_cameraController.CompleteStart(token))
            {
                _cameraController.Stop();
                try { _camera.StopCameraPreview(); } catch { }
                return;
            }

            _previewOn = true;
            LinesHost.IsVisible = false;

            if (_cameraNoteShown) {
                _cameraNoteShown = false;
                ErrorLabel.IsVisible = false;
            }
        } catch (OperationCanceledException) {
            _cameraController.Stop();
            return;
        } catch (Exception ex) {
            _cameraController.Stop();
            if (_camera is not null)
                _camera.IsVisible = false;
            LinesHost.IsVisible = true;

            if (_isPageVisible && _visibilityToken == token)
                ShowCameraNote($"Камера недоступна: {ex.Message}\nВыберите фото через «Галерею» или «Файл».");
        } finally {
            _previewStarting = false;
        }
    }

    private void StopPreview()
    {
        _cameraController.Stop();
        _previewOn = false;
        _previewStarting = false;
        try { _camera?.StopCameraPreview(); } catch (Exception) { /* камера уже остановлена */ }
        if (_camera is not null)
            _camera.IsVisible = false;
        LinesHost.IsVisible = true;
    }

    private void ShowCameraNote(string text)
    {
        _cameraNoteShown = true;
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    // ===== Лазерная анимация сканирования видоискателя =====

    private void StartLaserAnimation()
    {
        StopLaserAnimation();
        if (!_isPageVisible || ResultPanel.IsVisible || LaserLine is null)
            return;

        double h = ViewfinderContainer?.HeightRequest > 0
            ? ViewfinderContainer.HeightRequest
            : (ViewfinderContainer?.Height > 0 ? ViewfinderContainer.Height : 320);
        double startY = 16;
        double endY = Math.Max(startY + 40, h - 24);

        LaserLine.TranslationY = startY;
        LaserLine.Opacity = 0.85;

        var anim = new Animation();
        // Плавное опускание сверху вниз за 2000 мс
        anim.Add(0.0, 0.80, new Animation(v => LaserLine.TranslationY = v, startY, endY, Easing.SinInOut));
        // Быстрое затухание внизу
        anim.Add(0.80, 0.88, new Animation(v => LaserLine.Opacity = v, 0.85, 0.1));
        // Возврат наверх пока невидима
        anim.Add(0.88, 0.92, new Animation(v => LaserLine.TranslationY = v, endY, startY));
        // Появление вверху
        anim.Add(0.92, 1.0, new Animation(v => LaserLine.Opacity = v, 0.1, 0.85));

        anim.Commit(this, "LaserAnimation", rate: 16, length: 2400, repeat: () => _isPageVisible && !ResultPanel.IsVisible && !_busy);
    }

    private void StopLaserAnimation()
    {
        this.AbortAnimation("LaserAnimation");
        if (LaserLine is not null)
        {
            LaserLine.CancelAnimations();
            LaserLine.Opacity = 0;
            LaserLine.TranslationY = 16;
        }
    }

    // ===== Внешний вид =====

    // Тонкие линии внутри рамки, как «лист» на макете (видны, пока камера выключена)
    private void BuildScanLines()
    {
        const int count = 20;

        for (int i = 0; i < count; i++) {
            LinesHost.RowDefinitions.Add(new RowDefinition(GridLength.Star));

            var line = new BoxView { HeightRequest = 1.5, VerticalOptions = LayoutOptions.Center, Opacity = 0.7 };
            line.SetDynamicResource(BoxView.ColorProperty, "CardStroke");
            Grid.SetRow(line, i);
            LinesHost.Children.Add(line);
        }
    }

    private void CancelProcessing()
    {
        if (_processCts is { IsCancellationRequested: false }) {
            try { _processCts.Cancel(); } catch { }
        }
        _processCts?.Dispose();
        _processCts = null;

        SetLoading(false);
        _busy = false;

        if (!IsDesktop && !ResultPanel.IsVisible)
        {
            StartLaserAnimation();
            _ = StartPreviewAsync();
        }
    }

    private void SetLoading(bool on, string status = "Загрузка...", string? substatus = null, bool canCancel = true)
    {
        Loader.IsRunning = on;
        Loader.IsVisible = on;
        if (on)
            ScanLoading.Show(status, substatus, canCancel);
        else
            ScanLoading.Hide();
    }

    // Результат скрывает рамку и выключает камеру; возврат к рамке включает её снова
    private void ShowResult(bool on)
    {
        ResultPanel.IsVisible = on;
        FramePanel.IsVisible = !IsDesktop && !on;
        if (IsDesktop)
        {
            Grid.SetColumnSpan(DesktopDropZone, on ? 1 : 2);
            DesktopRecognizeButton.IsVisible = !on;
            return;
        }

        if (on)

        {
            StopLaserAnimation();
            StopPreview();
        }
        else
        {
            StartLaserAnimation();
            _ = StartPreviewAsync();
        }
    }

    private void ShowError(string text)
    {
        ShowResult(false);

        if (IsDesktop)
        {
            DesktopError.Text = text;
            DesktopError.IsVisible = true;
            return;
        }

        _cameraNoteShown = false;
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    // ===== Кнопки =====

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        StopLaserAnimation();
        CancelProcessing();
        StopPreview();
        CleanupTempFiles();
        string route = ReturnRoute();
        if (!_busy)
        {
            _mode = null;
            UpdateModeUi();
        }
        await Shell.Current.GoToAsync(route);
    }

    private async void OnRetakeTapped(object? sender, TappedEventArgs e)
    {
        if (IsDesktop)
            await ChooseDesktopImageAsync();
        else
            ShowResult(false);
    }

    // Затвор: съемка через системную камеру устройства для максимального оптического качества и автофокуса
    private async void OnShutterTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        await AcquireAndRecognizeAsync("Не удалось открыть камеру", async token => {
            if (!MediaPicker.Default.IsCaptureSupported)
                throw new InvalidOperationException("Это устройство не поддерживает съёмку фото.");

            // Проверяем и запрашиваем разрешения перед запуском MediaPicker
            var cameraStatus = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (cameraStatus != PermissionStatus.Granted)
            {
                cameraStatus = await Permissions.RequestAsync<Permissions.Camera>();
                if (cameraStatus != PermissionStatus.Granted)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        await Dialogs.AlertAsync("Доступ к камере отключён",
                            "Для съёмки документа необходимо разрешение на использование камеры. Вы можете включить его в настройках устройства или выбрать готовый файл через «Галерею» или «Файл».",
                            DialogKind.Info);
                    });
                    return null;
                }
            }

            try
            {
#if ANDROID
                if (Android.OS.Build.VERSION.SdkInt < Android.OS.BuildVersionCodes.Tiramisu)
                {
                    var storageStatus = await Permissions.CheckStatusAsync<Permissions.StorageWrite>();
                    if (storageStatus != PermissionStatus.Granted)
                    {
                        await Permissions.RequestAsync<Permissions.StorageWrite>();
                    }
                }
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScanPage] Storage permission check skipped: {ex.Message}");
            }

            SetLoading(true, "Открытие камеры...", "Съёмка документа с высоким разрешением");
            var photo = await MediaPicker.Default.CapturePhotoAsync();
            RegisterTempFile(photo?.FullPath);
            return await OpenAsync(photo);
        }, pausePreview: true);
    }

    private async void OnGalleryTapped(object? sender, TappedEventArgs e)
        => await AcquireAndRecognizeAsync("Не удалось открыть галерею", async token => {
            SetLoading(true, "Открытие галереи...", "Выбор файла фотографии");
            var photos = await MediaPicker.Default.PickPhotosAsync();
            return await OpenAsync(photos?.FirstOrDefault());
        }, pausePreview: true);

    private async void OnFileTapped(object? sender, TappedEventArgs e)
        => await AcquireAndRecognizeAsync("Не удалось открыть файл", async token => {
            SetLoading(true, "Открытие проводника...", "Выбор файла документа");
            return await OpenAsync(await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Выберите изображение документа",
                FileTypes = FilePickerFileType.Images,
            }));
        }, pausePreview: true);

    private async Task<Stream?> OpenAsync(FileResult? file)
    {
        if (file is null)
            return null;

        return await file.OpenReadAsync();
    }




    // ===== Получить изображение и распознать =====

    // pausePreview: перед окном выбора файла камеру в рамке выключаем, чтобы не держать её занятой
    private async Task AcquireAndRecognizeAsync(string errorTitle, Func<CancellationToken, Task<Stream?>> acquire, bool pausePreview)
    {
        if (_busy)
            return;

        _busy = true;
        _processCts?.Dispose();
        _processCts = new CancellationTokenSource();
        var token = _processCts.Token;
        try {
            if (pausePreview)
    
        {
            StopLaserAnimation();
            StopPreview();
        }

            Stream? stream;
            try {
                stream = await acquire(token);
            } catch (OperationCanceledException) {
                return;

            } catch (Exception ex) {
                SetLoading(false);
                await Dialogs.AlertAsync(errorTitle, ex.Message, DialogKind.Danger);
                return;
            }

            if (stream is null)
                return;   // закрыли камеру или окно выбора

            await using (stream)
            {
                if (token.IsCancellationRequested)
                    return;

                await ProcessStreamAsync(stream, token);
            }
        } catch (OperationCanceledException) {
            // Прервано пользователем
        } finally {
            SetLoading(false);
            _busy = false;

            CleanupTempFiles();
            // Вернулись без результата (отмена, ошибка): камера в рамке снова работает
            if (!IsDesktop && !ResultPanel.IsVisible)
            {
            StartLaserAnimation();
            _ = StartPreviewAsync();
        }
        }
    }

    private async Task ProcessStreamAsync(Stream stream, CancellationToken token)
    {
        ErrorLabel.IsVisible = false;
        DesktopError.IsVisible = false;
        _cameraNoteShown = false;
        SetLoading(true, "Улучшаем изображение...", "Выравнивание наклона и бинаризация");

        try {
            if (stream.CanSeek)
                stream.Position = 0;

            ScanLoading.UpdateStatus("Распознаем текст...", "Распознавание символов OCR");
            token.ThrowIfCancellationRequested();
#if WINDOWS
            string text = await OcrService.RecognizeDocumentAsync(stream, token);
#else
            string text = await OcrService.RecognizeTextAsync(stream, token);
#endif
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(text)) {
                ShowError(IsDesktop
                    ? "Текст не распознан. Выберите более чёткое изображение, на котором документ виден целиком."
                    : "Текст на фото не распознан. Попробуйте снять при лучшем освещении, ровнее и ближе.");
                return;
            }

            ApplyResult(text);
            ShowResult(true);
        } catch (OperationCanceledException) {
            // Распознавание прервано пользователем
            return;
            } catch (Exception ex) {
            ShowError($"Не удалось распознать текст: {ex.Message}");
        } finally {
            SetLoading(false);
        }
    }

    // Показать результат под выбранный тип документа
    private async void ApplyResult(string text)
    {
        RawTextEditor.Text = text;

        bool isPassport = string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase);
        ExtractedSection.IsVisible = isPassport;

        if (isPassport) {
            var profile = await ProfileStorage.LoadAsync();
            OldNameLabel.Text = string.IsNullOrWhiteSpace(profile.FullName) ? "Было: —" : $"Было: {profile.FullName}";
            OldPassportLabel.Text = string.IsNullOrWhiteSpace(profile.Passport) ? "Было: —" : $"Было: {profile.Passport}";
            profile.CustomFields.TryGetValue("Идентификационный номер", out var oldPersonal);
            OldPersonalLabel.Text = string.IsNullOrWhiteSpace(oldPersonal) ? "Было: —" : $"Было: {oldPersonal}";

            var data = DocumentDataExtractor.Extract(text);
            NameEntry.Text = data.FullName ?? "";
            PassportEntry.Text = data.Passport ?? "";
            PersonalEntry.Text = data.PersonalNumber ?? "";

            ApplyNameCheck.IsChecked = !string.IsNullOrWhiteSpace(data.FullName);
            ApplyPassportCheck.IsChecked = !string.IsNullOrWhiteSpace(data.Passport);
            ApplyPersonalCheck.IsChecked = !string.IsNullOrWhiteSpace(data.PersonalNumber);

            if (data.IsMrz)
            {
                MrzStatusBadge.IsVisible = true;
                if (data.IsValid)
                {
                    MrzStatusBadge.SetDynamicResource(VisualElement.BackgroundColorProperty, "IconBoxBg");
                    MrzStatusLabel.SetDynamicResource(Label.TextColorProperty, "BrandGreen");
                    MrzStatusLabel.Text = "MRZ проверена · ICAO 9303";
                }
                else
                {
                    MrzStatusBadge.SetDynamicResource(VisualElement.BackgroundColorProperty, "WarnSoftBg");
                    MrzStatusLabel.SetDynamicResource(Label.TextColorProperty, "WarnColor");
                    MrzStatusLabel.Text = "Контрольная сумма не сошлась";
                }
            }
            else
            {
                MrzStatusBadge.IsVisible = false;
            }
        }

        // Обновляем визуальный предпросмотр А4 по ГОСТ
        if (IsDesktop)
            ShowTextEditor();
        else
            UpdateA4Preview();
    }

    // ===== Переключение табов: Предпросмотр А4 / Редактирование =====
    private void OnTabPreviewTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        A4PreviewSheet.IsVisible = true;
        TextEditorContainer.IsVisible = false;

        TabPreviewBtn.BackgroundColor = (Color)Application.Current!.Resources["CardBg"];
        TabPreviewLabel.FontAttributes = FontAttributes.Bold;
        TabPreviewLabel.TextColor = (Color)Application.Current!.Resources["BrandGreen"];

        TabEditBtn.BackgroundColor = Colors.Transparent;
        TabEditLabel.FontAttributes = FontAttributes.None;
        TabEditLabel.TextColor = (Color)Application.Current!.Resources["TextSecondary"];

        UpdateA4Preview();
    }

    private void OnTabEditTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        ShowTextEditor();
    }

    private void ShowTextEditor()
    {
        A4PreviewSheet.IsVisible = false;
        TextEditorContainer.IsVisible = true;

        TabEditBtn.BackgroundColor = (Color)Application.Current!.Resources["CardBg"];
        TabEditLabel.FontAttributes = FontAttributes.Bold;
        TabEditLabel.TextColor = (Color)Application.Current!.Resources["BrandGreen"];

        TabPreviewBtn.BackgroundColor = Colors.Transparent;
        TabPreviewLabel.FontAttributes = FontAttributes.None;
        TabPreviewLabel.TextColor = (Color)Application.Current!.Resources["TextSecondary"];
    }

    private void OnRawTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (A4PreviewSheet.IsVisible)
        {
            UpdateA4Preview();
        }
    }

    private void OnFormattingOptionChanged(object? sender, EventArgs e)
    {
        UpdateA4Preview();
    }

    // Динамическая генерация визуального листа А4 по ГОСТ
    private void UpdateA4Preview()
    {
        if (A4DocumentContent == null || RawTextEditor == null)
            return;

        A4DocumentContent.Children.Clear();

        string text = RawTextEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            A4DocumentContent.Children.Add(new Label
            {
                Text = "Документ пуст",
                TextColor = Color.FromArgb("#94A3B8"),
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 40)
            });
            return;
        }

        var options = BuildGostFormattingOptions();

        // Настройка визуальных полей листа А4
        double leftPadding = Math.Max(12, options.MarginLeftMm * 0.7);
        double rightPadding = Math.Max(10, options.MarginRightMm * 0.7);
        A4PreviewSheet.Padding = new Thickness(leftPadding, 20, rightPadding, 20);

        // Универсальный структурный парсер
        var blocks = UniversalDocumentParser.Parse(text);

        // Буфер для накопления последовательных строк шапки (адресата)
        VerticalStackLayout? headerContainer = null;

        foreach (var block in blocks)
        {
            if (block.Type == DocumentBlockType.HeaderRight)
            {
                if (headerContainer == null)
                {
                    headerContainer = new VerticalStackLayout
                    {
                        HorizontalOptions = LayoutOptions.End,
                        Spacing = 2,
                        Margin = new Thickness(0, 0, 0, 8),
                        WidthRequest = 185 // половина листа для адресата
                    };
                    A4DocumentContent.Children.Add(headerContainer);
                }

                headerContainer.Children.Add(new Label
                {
                    Text = block.Text,
                    FontFamily = options.FontFamily,
                    FontSize = Math.Max(10.5, options.FontSizePt - 2.5),
                    TextColor = Color.FromArgb("#0F172A"),
                    HorizontalTextAlignment = TextAlignment.Start,
                    LineBreakMode = LineBreakMode.WordWrap
                });
                continue;
            }
            else
            {
                headerContainer = null;
            }

            if (block.Type == DocumentBlockType.EmptyLine)
            {
                A4DocumentContent.Children.Add(new BoxView { HeightRequest = 4, Color = Colors.Transparent });
                continue;
            }

            if (block.Type == DocumentBlockType.Title)
            {
                A4DocumentContent.Children.Add(new Label
                {
                    Text = block.Text,
                    FontFamily = options.FontFamily,
                    FontSize = options.FontSizePt + 1.5,
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Color.FromArgb("#0F172A"),
                    HorizontalTextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 6, 0, 8),
                    LineBreakMode = LineBreakMode.WordWrap
                });
                continue;
            }

            if (block.Type == DocumentBlockType.SignatureRow)
            {
                var sigGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new ColumnDefinition(GridLength.Star),
                        new ColumnDefinition(new GridLength(80)),
                        new ColumnDefinition(GridLength.Star)
                    },
                    Margin = new Thickness(0, 10, 0, 4)
                };

                sigGrid.Children.Add(new Label
                {
                    Text = block.Col1 ?? block.Text,
                    FontFamily = options.FontFamily,
                    FontSize = Math.Max(10.5, options.FontSizePt - 2.0),
                    TextColor = Color.FromArgb("#0F172A"),
                    HorizontalTextAlignment = TextAlignment.Start
                });

                sigGrid.Children.Add(new Label
                {
                    Text = block.Col2 ?? "__________",
                    FontFamily = options.FontFamily,
                    FontSize = Math.Max(10.5, options.FontSizePt - 2.0),
                    TextColor = Color.FromArgb("#64748B"),
                    HorizontalTextAlignment = TextAlignment.Center
                });
                Grid.SetColumn((BindableObject)sigGrid.Children[1], 1);

                if (!string.IsNullOrEmpty(block.Col3))
                {
                    sigGrid.Children.Add(new Label
                    {
                        Text = block.Col3,
                        FontFamily = options.FontFamily,
                        FontSize = Math.Max(10.5, options.FontSizePt - 2.0),
                        TextColor = Color.FromArgb("#0F172A"),
                        HorizontalTextAlignment = TextAlignment.End
                    });
                    Grid.SetColumn((BindableObject)sigGrid.Children[2], 2);
                }

                A4DocumentContent.Children.Add(sigGrid);
                continue;
            }

            // Основной абзац или список
            var pLabel = new Label
            {
                Text = block.Text,
                FontFamily = options.FontFamily,
                FontSize = options.FontSizePt - 1.0,
                TextColor = Color.FromArgb("#0F172A"),
                LineBreakMode = LineBreakMode.WordWrap,
                LineHeight = Math.Max(1.0, options.LineSpacing * 0.85)
            };

            if (block.Type == DocumentBlockType.ListItem)
            {
                pLabel.HorizontalTextAlignment = TextAlignment.Start;
                pLabel.Margin = new Thickness(20, 0, 0, 3);
            }
            else
            {
                pLabel.HorizontalTextAlignment = options.Alignment.Equals("Left", StringComparison.OrdinalIgnoreCase)
                    ? TextAlignment.Start
                    : TextAlignment.Justify;

                double indentPixels = options.FirstLineIndentCm * 16.0;
                pLabel.Margin = new Thickness(indentPixels, 0, 0, 3);
            }

            A4DocumentContent.Children.Add(pLabel);
        }
    }

    // ===== Сброс параметров оформления ГОСТ на значения по умолчанию =====
    private void OnResetGostOptionsTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        FontPicker.SelectedIndex = 0;       // Times New Roman
        FontSizePicker.SelectedIndex = 2;   // 14 пт (ГОСТ)
        LineSpacingPicker.SelectedIndex = 2;// 1.5 (ГОСТ)
        IndentPicker.SelectedIndex = 2;     // 1.25 см (ГОСТ)
        AlignPicker.SelectedIndex = 0;      // По ширине (ГОСТ)
        MarginsPicker.SelectedIndex = 0;    // ГОСТ (30-15-20-20 мм)
        Toast.Show("Параметры сброшены на ГОСТ", "settings");
    }

    // ===== Экспорт оцифрованного текста в настраиваемый ГОСТ DOCX =====
    private async void OnExportGostDocxTapped(object? sender, EventArgs e)
    {
        if (IsDesktop)
        {
            await ExportDesktopScanAsync(pdf: false);
            return;
        }

        HapticHelper.Click();
        string text = RawTextEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Show("Текст документа пуст", "alert");
            return;
        }

        try
        {
            var options = BuildGostFormattingOptions();

            string defaultTitle = ScanTemplateBuilder.DetectDocumentTitle(text);
            string safeTitle = string.Join("_", defaultTitle.Split(Path.GetInvalidFileNameChars()))
                .Replace(" ", "_");
            if (string.IsNullOrWhiteSpace(safeTitle))
                safeTitle = "Документ";

            string fileName = $"{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.docx";
            string documentsDir = FileSystem.AppDataDirectory;
            string targetPath = Path.Combine(documentsDir, fileName);

            ScanLoading.Show("Форматирование DOCX...", "Оформление абзацев, отступов и стилей по ГОСТ", canCancel: false);
            await Task.Run(() => GostDocxExporter.ExportToDocx(targetPath, text, options));
            ScanLoading.Hide();

            HapticHelper.Success();

            string choice = await DisplayActionSheetAsync(
                "Документ по ГОСТ сформирован!",
                "Отмена",
                null,
                "Открыть в Word / Офисе",
                "Поделиться файлом",
                "Скопировать путь к файлу");

            if (choice == "Открыть в Word / Офисе")
            {
                await Launcher.Default.OpenAsync(new OpenFileRequest
                {
                    Title = "Открыть документ",
                    File = new ReadOnlyFile(targetPath)
                });
            }
            else if (choice == "Поделиться файлом")
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = "Поделиться документом DOCX",
                    File = new ShareFile(targetPath)
                });
            }
            else if (choice == "Скопировать путь к файлу")
            {
                await Clipboard.Default.SetTextAsync(targetPath);
                Toast.Show("Путь к файлу скопирован", "copy");
            }
        }
        catch (Exception ex)
        {
            ScanLoading.Hide();
            await Dialogs.AlertAsync("Ошибка экспорта", ex.Message, DialogKind.Danger);
        }
    }

    private GostFormattingOptions BuildGostFormattingOptions()
    {
        var options = new GostFormattingOptions();

        // 1. Шрифт
        if (FontPicker?.SelectedItem is string font && !string.IsNullOrWhiteSpace(font))
            options.FontFamily = font;

        // 2. Размер
        options.FontSizePt = FontSizePicker?.SelectedIndex switch
        {
            0 => 12.0,
            1 => 13.0,
            _ => 14.0
        };

        // 3. Интервал
        options.LineSpacing = LineSpacingPicker?.SelectedIndex switch
        {
            0 => 1.0,
            1 => 1.15,
            _ => 1.5
        };

        // 4. Красная строка
        options.FirstLineIndentCm = IndentPicker?.SelectedIndex switch
        {
            0 => 0.0,
            1 => 1.0,
            3 => 1.5,
            _ => 1.25
        };

        // 5. Выравнивание
        options.Alignment = AlignPicker?.SelectedIndex == 1 ? "Left" : "Justify";

        // 6. Поля
        if (MarginsPicker?.SelectedIndex == 1)
        {
            options.MarginLeftMm = 20.0;
            options.MarginRightMm = 20.0;
            options.MarginTopMm = 20.0;
            options.MarginBottomMm = 20.0;
        }
        else if (MarginsPicker?.SelectedIndex == 2)
        {
            options.MarginLeftMm = 30.0;
            options.MarginRightMm = 20.0;
            options.MarginTopMm = 20.0;
            options.MarginBottomMm = 20.0;
        }
        else
        {
            options.MarginLeftMm = 30.0;
            options.MarginRightMm = 15.0;
            options.MarginTopMm = 20.0;
            options.MarginBottomMm = 20.0;
        }

        return options;
    }

    // ===== Копирование текста =====
    private async void OnCopyTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        Toast.Show("Текст скопирован в буфер", "copy");
        await Clipboard.Default.SetTextAsync(RawTextEditor.Text ?? "");

        int version = ++_copyVersion;
        CopyLabel.Text = "Скопировано";

        await Task.Delay(1500);
        if (version == _copyVersion)
            CopyLabel.Text = "Копировать";
    }

    // ===== Создание нового ГОСТ-шаблона из распознанного текста =====
    private async void OnCreateTemplateFromOcrTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        string rawText = RawTextEditor.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawText))
        {
            Toast.Show("Текст документа пуст", "alert");
            return;
        }

        try
        {
            string defaultTitle = ScanTemplateBuilder.DetectDocumentTitle(rawText);
            string? promptResult = await DisplayPromptAsync(
                "Создание ГОСТ-шаблона",
                "Введите название для нового шаблона документа:",
                initialValue: defaultTitle,
                accept: "Создать",
                cancel: "Отмена",
                maxLength: 60);

            if (string.IsNullOrWhiteSpace(promptResult))
                return;

            ScanLoading.Show("Создаем ГОСТ-документ...", "Разметка абзацев, отступов и полей по ГОСТ", canCancel: false);
            var result = await Task.Run(() => ScanTemplateBuilder.BuildAndSaveTemplate(rawText, promptResult));
            ScanLoading.Hide();

            HapticHelper.Success();
            Toast.Show($"Шаблон «{result.Title}» успешно создан!", "templates");

            // Ищем созданный шаблон в обновленном каталоге и сразу открываем его
            var createdTemplate = TemplateCatalog.Items.FirstOrDefault(t =>
                string.Equals(TemplateCatalog.Find(t.Id)?.FilePath, result.FilePath, StringComparison.Ordinal));
            if (createdTemplate != null)
            {
                await TemplatesPage.OpenAsync(createdTemplate);
            }
            else
            {
                await Shell.Current.GoToAsync("//TemplatesPage");
            }
        }
        catch (Exception ex)
        {
            ScanLoading.Hide();
            Toast.Show($"Ошибка создания шаблона: {ex.Message}", "alert");
        }
    }

    // ===== Перенос распознанного текста в шаблон =====
    private async void OnApplyToTemplateTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        var text = RawTextEditor.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            Toast.Show("Текст документа пуст", "alert");
            return;
        }

        DraftStore.PendingOcrText = text;
        Toast.Show("Текст готов для вставки в шаблон", "filetext");
        await Shell.Current.GoToAsync("//TemplatesPage");
    }

    // ===== Сохранение в профиль =====
    private async void OnSaveToProfileTapped(object? sender, TappedEventArgs e)
    {
        var profile = await ProfileStorage.LoadAsync();

        string? newName = (ApplyNameCheck.IsChecked && !string.IsNullOrWhiteSpace(NameEntry.Text))
            ? ProfileRules.Normalize(ProfileField.FullName, NameEntry.Text)
            : profile.FullName;

        string? newPassport = (ApplyPassportCheck.IsChecked && !string.IsNullOrWhiteSpace(PassportEntry.Text))
            ? ProfileRules.Normalize(ProfileField.Passport, PassportEntry.Text)
            : profile.Passport;

        var customFields = new Dictionary<string, string>(profile.CustomFields, StringComparer.OrdinalIgnoreCase);
        if (ApplyPersonalCheck.IsChecked && !string.IsNullOrWhiteSpace(PersonalEntry.Text))
        {
            customFields["Идентификационный номер"] = PersonalEntry.Text.Trim();
        }

        var updated = profile with {
            FullName = newName,
            Passport = newPassport,
            CustomFields = customFields
        };

        try {
            await ProfileStorage.SaveAsync(updated);
            HapticHelper.Success();
            Toast.Show("Выбранные данные сохранены в профиль", "user");

            if (string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase))
                await Shell.Current.GoToAsync("//ProfilePage");
        } catch (OperationCanceledException) {
            return;
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
        }
    }
}
