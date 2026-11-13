/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Экран видоискателя камеры, захвата кадра, вызова OCR, предобработки и автоопределения типа документа
 * СОДЕРЖИМОЕ: ScanPage
 * ЗАВИСИМОСТИ: CommunityToolkit.Maui.Camera, GosTek.Services, GosTek.Models, GosTek.Views.Controls
 * ==============================================================================
 */
using CommunityToolkit.Maui.Views;
using GosTek.Models;
using GosTek.Services;
using GosTek.Views.Controls;

namespace GosTek.Views;

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
    }

    private void UpdateModeUi()
    {
        bool isPassport = string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase);

        if (PageTitleLabel != null)
            PageTitleLabel.Text = isPassport ? "Сканирование паспорта" : "Сканер документов";

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

        ViewfinderContainer.WidthRequest = targetW;
        ViewfinderContainer.HeightRequest = targetH;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageVisible = true;
        int token = ++_visibilityToken;

        UpdateModeUi();

        if (!ResultPanel.IsVisible)
        {
            await StartPreviewAsync(token);
            if (_isPageVisible && _visibilityToken == token)
                StartLaserAnimation();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
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

            if (_camera is null) {
                _camera = new CameraView {
                    HorizontalOptions = LayoutOptions.Fill,
                    VerticalOptions = LayoutOptions.Fill,
                };
                CameraHost.Children.Add(_camera);
            }

            _camera.IsVisible = true;
            LinesHost.IsVisible = false;

            // Только что созданный CameraView получает нативный обработчик не сразу
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

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _camera.StartCameraPreview(cts.Token);

            if (!_isPageVisible || _visibilityToken != token || ResultPanel.IsVisible || !_cameraController.CompleteStart(token))
            {
                _cameraController.Stop();
                try { _camera.StopCameraPreview(); } catch { }
                return;
            }

            _previewOn = true;

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

        if (!ResultPanel.IsVisible)
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
        FramePanel.IsVisible = !on;

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

    private void OnRetakeTapped(object? sender, TappedEventArgs e) => ShowResult(false);

    // Затвор: снимок прямо с камеры в рамке; если камеры в рамке нет — системная камера
    private async void OnShutterTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (_previewOn && _camera is { } camera) {
            await AcquireAndRecognizeAsync("Не удалось сделать снимок", async token => {
                SetLoading(true, "Снимок кадра...", "Захват фото с камеры");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(10));
                return await camera.CaptureImage(cts.Token);
            }, pausePreview: false);
            return;
        }

        await AcquireAndRecognizeAsync("Не удалось открыть камеру", async token => {
            if (!MediaPicker.Default.IsCaptureSupported)
                throw new InvalidOperationException("Это устройство не поддерживает съёмку фото.");

            return await OpenAsync(await MediaPicker.Default.CapturePhotoAsync());
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

        if (!string.IsNullOrWhiteSpace(file.FullPath))
            RegisterTempFile(file.FullPath);

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

            if (stream is null || token.IsCancellationRequested)
                return;   // закрыли камеру или окно выбора

            await using (stream)
                await ProcessStreamAsync(stream, token);
        } catch (OperationCanceledException) {
            // Прервано пользователем
        } finally {
            SetLoading(false);
            _busy = false;

            CleanupTempFiles();
            // Вернулись без результата (отмена, ошибка): камера в рамке снова работает
            if (!ResultPanel.IsVisible)
            {
            StartLaserAnimation();
            _ = StartPreviewAsync();
        }
        }
    }

    private async Task ProcessStreamAsync(Stream stream, CancellationToken token)
    {
        ErrorLabel.IsVisible = false;
        _cameraNoteShown = false;
        SetLoading(true, "Улучшаем изображение...", "Выравнивание наклона и бинаризация");

        try {
            if (stream.CanSeek)
                stream.Position = 0;

            ScanLoading.UpdateStatus("Распознаем текст...", "Распознавание символов OCR");
            token.ThrowIfCancellationRequested();
            string text = await OcrService.RecognizeTextAsync(stream, token);
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(text)) {
                ShowError("Текст на фото не распознан. Попробуйте снять при лучшем освещении, ровнее и ближе.");
                return;
            }

            ApplyResult(text);
            ShowResult(true);

            // Интеллектуальное автоопределение типа документа (только для обычного скана, не для паспорта)
            bool isPassport = string.Equals(_mode, "passport", StringComparison.OrdinalIgnoreCase);
            if (!isPassport)
            {
                var classification = DocumentClassifier.Instance.Classify(text);
                if (classification.IsDetected) {
                    string detectedCategory = !string.IsNullOrWhiteSpace(classification.Category)
                        ? classification.Category
                        : classification.DocumentType;

                    bool apply = await Dialogs.ConfirmAsync(
                        "Определение документа",
                        $"Я определил, что это {detectedCategory}. Применить этот шаблон?",
                        accept: "Да",
                        kind: DialogKind.Info,
                        cancel: "Выбрать другой"
                    );

                    if (apply) {
                        TemplateCatalog.Refresh();
                        var matching = TemplateCatalog.Items.FirstOrDefault(t =>
                            string.Equals(t.Category, detectedCategory, StringComparison.OrdinalIgnoreCase) ||
                            t.Category.Contains(detectedCategory, StringComparison.OrdinalIgnoreCase) ||
                            detectedCategory.Contains(t.Category, StringComparison.OrdinalIgnoreCase) ||
                            t.Title.Contains(classification.DocumentType, StringComparison.OrdinalIgnoreCase) ||
                            classification.DocumentType.Contains(t.Title, StringComparison.OrdinalIgnoreCase));

                        if (matching != null) {
                            await Shell.Current.GoToAsync($"FieldsPage?id={matching.Id}");
                        } else {
                            await Shell.Current.GoToAsync("//TemplatesPage");
                        }
                    } else {
                        await Shell.Current.GoToAsync("//TemplatesPage");
                    }
                }
            }
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
                    MrzStatusBadge.BackgroundColor = Color.FromArgb("#1510B981");
                    MrzStatusLabel.TextColor = Color.FromArgb("#10B981");
                    MrzStatusLabel.Text = "✓ ICAO 9303 MRZ проверена";
                }
                else
                {
                    MrzStatusBadge.BackgroundColor = Color.FromArgb("#15F59E0B");
                    MrzStatusLabel.TextColor = Color.FromArgb("#F59E0B");
                    MrzStatusLabel.Text = "⚠ Контрольная сумма не сошлась";
                }
            }
            else
            {
                MrzStatusBadge.IsVisible = false;
            }
        }
    }

    // ===== Копирование текста =====
    private async void OnCopyTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        Toast.Show("Текст скопирован в буфер", "copy");
        await Clipboard.Default.SetTextAsync(RawTextEditor.Text ?? "");

        int version = ++_copyVersion;
        CopyLabel.Text = "Скопировано ✓";

        await Task.Delay(1500);
        if (version == _copyVersion)
            CopyLabel.Text = "Копировать";
    }

    // ===== Перенос распознанного текста в шаблон =====
    private async void OnApplyToTemplateTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        DraftStore.PendingOcrText = RawTextEditor.Text;
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
