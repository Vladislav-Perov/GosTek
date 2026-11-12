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
    // Тип документа: «Паспорт / ID» разбирает ФИО и паспорт для профиля,
    // остальные типы пока просто показывают весь распознанный текст
    private enum ScanKind { Passport, Certificate, Any }

    private static readonly (ScanKind Kind, string Title)[] Kinds =
    {
        (ScanKind.Passport, "Паспорт / ID"),
        (ScanKind.Certificate, "Свидетельство"),
        (ScanKind.Any, "Любой документ"),
    };

    private ScanKind _kind = ScanKind.Passport;
    private string? _lastText;   // последний распознанный текст: при смене типа его разбирают заново
    private string? _mode;
    private bool _busy;
    private int _copyVersion;
    private CancellationTokenSource? _processCts;
    private CancellationTokenSource? _laserCts;

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
        set => _mode = value;
    }

    public ScanPage()
    {
        InitializeComponent();
        BuildScanLines();
        BuildChips();
        ScanLoading.CancelRequested += (_, _) => CancelProcessing();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _isPageVisible = true;
        int token = ++_visibilityToken;

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
            _mode = null;
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

        _ = Shell.Current.GoToAsync(ReturnRoute());
        return true;
    }

    private string ReturnRoute() => _mode == "passport" ? "//ProfilePage" : "//MainPage";

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
        if (!_isPageVisible || ResultPanel.IsVisible)
            return;

        _laserCts = new CancellationTokenSource();
        var ct = _laserCts.Token;
        int token = _visibilityToken;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && _isPageVisible && _visibilityToken == token)
            {
                try
                {
                    bool finished = false;
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (ct.IsCancellationRequested || !_isPageVisible || _visibilityToken != token || LaserLine is null)
                            return;

                        double h = ViewfinderArea?.Height ?? 0;
                        if (h <= 0) h = 340;
                        double targetY = Math.Max(80, h - 36);

                        LaserLine.TranslationY = 16;
                        LaserLine.Opacity = 0.85;

                        // Плавное опускание сверху вниз за 2200 мс с ускорением и замедлением
                        await LaserLine.TranslateToAsync(0, targetY, 2200, Easing.SinInOut);
                        if (ct.IsCancellationRequested)
                            return;

                        // Быстрое затухание внизу и мгновенный возврат наверх
                        await LaserLine.FadeToAsync(0.1, 150);
                        LaserLine.TranslationY = 16;
                        await LaserLine.FadeToAsync(0.85, 150);
                        finished = true;
                    });

                    if (!finished && ct.IsCancellationRequested)
                        break;

                    await Task.Delay(100, ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    break;
                }
            }
        }, ct);
    }

    private void StopLaserAnimation()
    {
        _laserCts?.Cancel();
        _laserCts?.Dispose();
        _laserCts = null;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LaserLine?.CancelAnimations();
            if (LaserLine is not null)
                LaserLine.Opacity = 0;
        });
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

    private void BuildChips()
    {
        KindChips.SetItems(Kinds.Select(k => k.Title).ToList());
        KindChips.SelectionChanged += (_, index) => {
            _kind = Kinds[index].Kind;

            if (_lastText is not null)
                ApplyResult(_lastText);
        };
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
        _lastText = null;
        ShowResult(false);

        _cameraNoteShown = false;
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

    // ===== Кнопки =====

    private async void OnCloseTapped(object? sender, TappedEventArgs e)
        => await Shell.Current.GoToAsync(ReturnRoute());

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

            _lastText = text;
            ApplyResult(text);
            ShowResult(true);

            // Интеллектуальное автоопределение типа документа
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
    private void ApplyResult(string text)
    {
        RawTextEditor.Text = text;

        bool passport = _kind == ScanKind.Passport;
        ExtractedSection.IsVisible = passport;

        if (passport) {
            var data = DocumentDataExtractor.Extract(text);
            NameEntry.Text = data.FullName ?? "";
            PassportEntry.Text = data.Passport ?? "";
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

    // ===== Сохранение в профиль =====
    private async void OnSaveToProfileTapped(object? sender, TappedEventArgs e)
    {
        var profile = await ProfileStorage.LoadAsync();

        var updated = profile with {
            FullName = string.IsNullOrWhiteSpace(NameEntry.Text)
                ? profile.FullName
                : ProfileRules.Normalize(ProfileField.FullName, NameEntry.Text),
            Passport = string.IsNullOrWhiteSpace(PassportEntry.Text)
                ? profile.Passport
                : ProfileRules.Normalize(ProfileField.Passport, PassportEntry.Text),
        };

        try {
            await ProfileStorage.SaveAsync(updated);
            HapticHelper.Success();
            Toast.Show("Данные добавлены в профиль", "user");

            if (_mode == "passport")
                await Shell.Current.GoToAsync("//ProfilePage");
        } catch (OperationCanceledException) {
            // Распознавание прервано пользователем
            return;
            } catch (Exception ex) {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
        }
    }
}
