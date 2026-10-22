using CommunityToolkit.Maui.Views;
using GosTek.Services;
using Microsoft.Maui.Controls.Shapes;

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

    private readonly List<(ScanKind Kind, Border Chip, Label Text)> _chips = new();

    private ScanKind _kind = ScanKind.Passport;
    private string? _lastText;   // последний распознанный текст: при смене типа его разбирают заново
    private string? _mode;
    private bool _busy;
    private int _copyVersion;

    // Живая камера в рамке: CameraView создаётся только после выдачи доступа к камере
    private CameraView? _camera;
    private bool _previewOn;
    private bool _previewStarting;
    private bool _cameraNoteShown;   // в ErrorLabel сейчас сообщение про камеру (а не про распознавание)

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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!ResultPanel.IsVisible)
            await StartPreviewAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // Камеру отпускаем при уходе с вкладки
        StopPreview();

        // Страница живёт между переходами: режим «из профиля» не должен прилипнуть.
        // Пока открыто окно выбора файла или идёт распознавание, его не трогаем.
        if (!_busy)
            _mode = null;
    }

    // «Назад» на Android: из результата — к рамке, иначе — на главную (или в профиль)
    protected override bool OnBackButtonPressed()
    {
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

    private async Task StartPreviewAsync()
    {
        if (_previewOn || _previewStarting || ResultPanel.IsVisible)
            return;

        _previewStarting = true;
        try {
            if (!await EnsureCameraPermissionAsync()) {
                ShowCameraNote("Нет доступа к камере. Разрешите её в настройках или выберите фото через «Галерею» или «Файл».");
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

            // Только что созданный CameraView получает нативный обработчик не сразу:
            // без ожидания StartCameraPreview падает с «Unable to retrieve Handler»
            for (int i = 0; i < 60 && _camera.Handler is null; i++)
                await Task.Delay(50);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await _camera.StartCameraPreview(cts.Token);

            _previewOn = true;

            if (_cameraNoteShown) {
                _cameraNoteShown = false;
                ErrorLabel.IsVisible = false;
            }
        } catch (Exception ex) {
            // Камеры нет или она занята: возвращаем линии «листа», сканировать можно из галереи и файла
            if (_camera is not null)
                _camera.IsVisible = false;
            LinesHost.IsVisible = true;

            ShowCameraNote($"Камера недоступна: {ex.Message}\nВыберите фото через «Галерею» или «Файл».");
        } finally {
            _previewStarting = false;
        }
    }

    private void StopPreview()
    {
        if (!_previewOn)
            return;

        _previewOn = false;
        try { _camera?.StopCameraPreview(); } catch (Exception) { /* камера уже остановлена */ }
    }

    private void ShowCameraNote(string text)
    {
        _cameraNoteShown = true;
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
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
        foreach (var (kind, title) in Kinds) {
            var label = new Label { Text = title, FontSize = 14 };
            var chip = new Border {
                StrokeShape = new RoundRectangle { CornerRadius = 18 },
                Padding = new Thickness(16, 9),
                Content = label,
            };

            var selected = kind;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => {
                _kind = selected;
                UpdateChips();

                if (_lastText is not null)
                    ApplyResult(_lastText);
            };
            chip.GestureRecognizers.Add(tap);

            KindChips.Children.Add(chip);
            _chips.Add((kind, chip, label));
        }

        UpdateChips();
    }

    // Активный чип — залитый, остальные — с рамкой (как категории на экране шаблонов)
    private void UpdateChips()
    {
        foreach (var (kind, chip, label) in _chips) {
            bool active = kind == _kind;

            chip.SetDynamicResource(VisualElement.BackgroundColorProperty, active ? "TextPrimary" : "CardBg");
            chip.SetDynamicResource(Border.StrokeProperty, "CardStroke");
            chip.StrokeThickness = active ? 0 : 1;

            label.SetDynamicResource(Label.TextColorProperty, active ? "PageBg" : "TextPrimary");
            label.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    private void SetLoading(bool on)
    {
        Loader.IsRunning = on;
        Loader.IsVisible = on;
    }

    // Результат скрывает рамку и выключает камеру; возврат к рамке включает её снова
    private void ShowResult(bool on)
    {
        ResultPanel.IsVisible = on;
        FramePanel.IsVisible = !on;

        if (on)
            StopPreview();
        else
            _ = StartPreviewAsync();
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
        if (_previewOn && _camera is { } camera) {
            await AcquireAndRecognizeAsync("Не удалось сделать снимок", async () => {
                SetLoading(true);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                return await camera.CaptureImage(cts.Token);
            }, pausePreview: false);
            return;
        }

        await AcquireAndRecognizeAsync("Не удалось открыть камеру", async () => {
            if (!MediaPicker.Default.IsCaptureSupported)
                throw new InvalidOperationException("Это устройство не поддерживает съёмку фото.");

            return await OpenAsync(await MediaPicker.Default.CapturePhotoAsync());
        }, pausePreview: true);
    }

    private async void OnGalleryTapped(object? sender, TappedEventArgs e)
        => await AcquireAndRecognizeAsync("Не удалось открыть галерею", async () => {
            var photos = await MediaPicker.Default.PickPhotosAsync();
            return await OpenAsync(photos?.FirstOrDefault());
        }, pausePreview: true);

    private async void OnFileTapped(object? sender, TappedEventArgs e)
        => await AcquireAndRecognizeAsync("Не удалось открыть файл", async () =>
            await OpenAsync(await FilePicker.Default.PickAsync(new PickOptions {
                PickerTitle = "Выберите изображение документа",
                FileTypes = FilePickerFileType.Images,
            })), pausePreview: true);

    private static async Task<Stream?> OpenAsync(FileResult? file)
        => file is null ? null : await file.OpenReadAsync();

    // ===== Получить изображение и распознать =====

    // pausePreview: перед окном выбора файла камеру в рамке выключаем, чтобы не держать её занятой
    private async Task AcquireAndRecognizeAsync(string errorTitle, Func<Task<Stream?>> acquire, bool pausePreview)
    {
        if (_busy)
            return;

        _busy = true;
        try {
            if (pausePreview)
                StopPreview();

            Stream? stream;
            try {
                stream = await acquire();
            } catch (Exception ex) {
                SetLoading(false);
                await Dialogs.AlertAsync(errorTitle, ex.Message, DialogKind.Danger);
                return;
            }

            if (stream is null)
                return;   // закрыли камеру или окно выбора

            await using (stream)
                await ProcessStreamAsync(stream);
        } finally {
            SetLoading(false);
            _busy = false;

            // Вернулись без результата (отмена, ошибка): камера в рамке снова работает
            if (!ResultPanel.IsVisible)
                _ = StartPreviewAsync();
        }
    }

    private async Task ProcessStreamAsync(Stream stream)
    {
        ErrorLabel.IsVisible = false;
        _cameraNoteShown = false;
        SetLoading(true);

        try {
            if (stream.CanSeek)
                stream.Position = 0;

            string text = await OcrService.RecognizeTextAsync(stream);

            if (string.IsNullOrWhiteSpace(text)) {
                ShowError("Текст на фото не распознан. Попробуйте снять при лучшем освещении, ровнее и ближе.");
                return;
            }

            _lastText = text;
            ApplyResult(text);
            ShowResult(true);
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
            await Dialogs.AlertAsync("Сохранено", "Данные добавлены в профиль.", DialogKind.Success);

            if (_mode == "passport")
                await Shell.Current.GoToAsync("//ProfilePage");
        } catch (Exception ex) {
            await Dialogs.AlertAsync("Не удалось сохранить", ex.Message, DialogKind.Danger);
        }
    }
}
