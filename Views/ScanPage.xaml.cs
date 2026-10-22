using GosTek.Services;

namespace GosTek.Views;

[QueryProperty(nameof(Mode), "mode")]
public partial class ScanPage : ContentPage
{
    private string? _mode;

    // "passport" — страница открыта через «Скан паспорта» в профиле: после сохранения
    // возвращаемся в профиль. Любое другое значение/отсутствие — обычная вкладка.
    public string? Mode
    {
        get => _mode;
        set => _mode = value;
    }

    public ScanPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
    }

    private async void OnCameraTapped(object? sender, TappedEventArgs e)
    {
        if (!MediaPicker.Default.IsCaptureSupported) {
            await DisplayAlertAsync("Нет камеры", "Это устройство не поддерживает съёмку фото.", "Понятно");
            return;
        }

        FileResult? photo;
        try {
            photo = await MediaPicker.Default.CapturePhotoAsync();
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось открыть камеру", ex.Message, "Понятно");
            return;
        }

        if (photo is not null)
            await ProcessPhotoAsync(photo);
    }

    private async void OnGalleryTapped(object? sender, TappedEventArgs e)
    {
        FileResult? photo;
        try {
            photo = await MediaPicker.Default.PickPhotoAsync();
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось открыть галерею", ex.Message, "Понятно");
            return;
        }

        if (photo is not null)
            await ProcessPhotoAsync(photo);
    }

    private async Task ProcessPhotoAsync(FileResult photo)
    {
        ErrorLabel.IsVisible = false;
        ResultSection.IsVisible = false;
        Loader.IsRunning = true;
        Loader.IsVisible = true;

        try {
            using var stream = await photo.OpenReadAsync();
            string text = await OcrService.RecognizeTextAsync(stream);

            if (string.IsNullOrWhiteSpace(text)) {
                ShowError("Текст на фото не распознан. Попробуйте снять при лучшем освещении, ровнее и ближе.");
                return;
            }

            var data = DocumentDataExtractor.Extract(text);

            RawTextEditor.Text = text;
            NameEntry.Text = data.FullName ?? "";
            PassportEntry.Text = data.Passport ?? "";
            ResultSection.IsVisible = true;
        } catch (Exception ex) {
            ShowError($"Не удалось распознать текст: {ex.Message}");
        } finally {
            Loader.IsRunning = false;
            Loader.IsVisible = false;
        }
    }

    private void ShowError(string text)
    {
        ErrorLabel.Text = text;
        ErrorLabel.IsVisible = true;
    }

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
            await DisplayAlertAsync("✅ Сохранено", "Данные добавлены в профиль.", "Понятно");

            if (_mode == "passport")
                await Shell.Current.GoToAsync("//ProfilePage");
        } catch (Exception ex) {
            await DisplayAlertAsync("Не удалось сохранить", ex.Message, "Понятно");
        }
    }
}
