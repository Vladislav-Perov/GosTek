using GosTek.Models;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

/// <summary>
/// Панель настроек: выезжает слева поверх главной страницы.
/// ПК: 1/3 ширины окна (340–440 px). Телефон: ~85% ширины.
/// Закрывается по ✕, по затемнению, по «Назад» (Android) и Esc (Windows): их ловит MainPage.
/// </summary>
public partial class SettingsPanel : ContentView
{
    private const uint OpenMs = 200;
    private const uint CloseMs = 180;

    private double _panelWidth = 340;
    private bool _animating;

    // Обновляют вид переключателей, когда значения меняются
    private readonly List<Action> _syncs = new();

    /// <summary>Панель на экране (в том числе пока едет анимация).</summary>
    public bool IsShown => IsVisible;

    /// <summary>Пользователь удалил данные: страницам нужно перечитать профиль.</summary>
    public event Action? DataCleared;

    // Значение переключателя ↔ значение настройки
    private static readonly ConflictMode[] ConflictOrder =
        { ConflictMode.Number, ConflictMode.Overwrite, ConflictMode.Ask };

    private static readonly ThemeMode[] ThemeOrder =
        { ThemeMode.System, ThemeMode.Dark, ThemeMode.Light };

    private static bool IsWindows => DeviceInfo.Platform == DevicePlatform.WinUI;

    public SettingsPanel()
    {
        InitializeComponent();

        // Папку выбираем только на ПК; на Android она фиксированная
        FolderBlock.IsVisible = IsWindows;
        AndroidFolderBlock.IsVisible = !IsWindows;

        BuildSegment(NameHost,
            new[] { "Название шаблона", "Название + дата" },
            () => (int)SettingsService.Name,
            i => SettingsService.Name = (NameMode)i);

        BuildSegment(ConflictHost,
            new[] { "Добавить номер", "Заменить", "Спросить" },
            () => Math.Max(0, Array.IndexOf(ConflictOrder, SettingsService.Conflict)),
            i => SettingsService.Conflict = ConflictOrder[i]);

        // «Поделиться» осмысленно на телефоне, на ПК вместо него «Ничего»
        var afterOrder = IsWindows
            ? new[] { AfterSaveAction.Ask, AfterSaveAction.Open, AfterSaveAction.Nothing }
            : new[] { AfterSaveAction.Ask, AfterSaveAction.Open, AfterSaveAction.Share };

        BuildSegment(AfterSaveHost,
            IsWindows
                ? new[] { "Спросить", "Открыть", "Ничего" }
                : new[] { "Спросить", "Открыть", "Поделиться" },
            () => Math.Max(0, Array.IndexOf(afterOrder, SettingsService.AfterSave)),
            i => SettingsService.AfterSave = afterOrder[i]);

        BuildSegment(ThemeHost,
            new[] { "Системная", "Тёмная", "Светлая" },
            () => Array.IndexOf(ThemeOrder, ThemeService.Mode),
            i => ThemeService.SetMode(ThemeOrder[i]));

        VersionLabel.Text = $"GosTek, версия {AppInfo.Current.VersionString}";

        SettingsService.Changed += SyncAll;
        ThemeService.Changed += SyncAll;

        // Окно изменили на ПК: подогнать ширину панели
        SizeChanged += (_, _) => {
            if (IsVisible && !_animating)
                UpdateWidth();
        };

        SyncAll();
    }

    // =====================================================================
    //  ОТКРЫТИЕ / ЗАКРЫТИЕ
    // =====================================================================

    public async Task OpenAsync()
    {
        if (IsVisible || _animating)
            return;

        _animating = true;
        try {
            SyncAll();
            UpdateWidth();

            Scrim.Opacity = 0;
            Panel.TranslationX = -_panelWidth;
            IsVisible = true;

            await Task.WhenAll(
                Scrim.FadeToAsync(1, OpenMs, Easing.CubicOut),
                Panel.TranslateToAsync(0, 0, OpenMs, Easing.CubicOut));
        } finally {
            _animating = false;
        }
    }

    public async Task CloseAsync()
    {
        if (!IsVisible || _animating)
            return;

        _animating = true;
        try {
            await Task.WhenAll(
                Scrim.FadeToAsync(0, CloseMs, Easing.CubicIn),
                Panel.TranslateToAsync(-_panelWidth, 0, CloseMs, Easing.CubicIn));

            IsVisible = false;
        } finally {
            _animating = false;
        }
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e) => _ = CloseAsync();

    private void OnCloseTapped(object? sender, TappedEventArgs e) => _ = CloseAsync();

    // ПК: треть окна в пределах 340–440 px. Телефон: 85% ширины (не больше 420)
    private void UpdateWidth()
    {
        double total = Parent is VisualElement { Width: > 0 } parent
            ? parent.Width
            : DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;

        double width = IsWindows
            ? Math.Clamp(total / 3, 340, 440)
            : Math.Min(total * 0.85, 420);

        _panelWidth = Math.Min(width, total * 0.95);
        Panel.WidthRequest = _panelWidth;
    }

    // =====================================================================
    //  ПЕРЕКЛЮЧАТЕЛИ
    // =====================================================================

    // Ряд кнопок «один из вариантов»: выбранная подсвечена зелёным
    private void BuildSegment(Grid host, string[] labels, Func<int> get, Action<int> set)
    {
        host.ColumnSpacing = 6;

        var items = new List<(Border Box, Label Text)>();

        for (int i = 0; i < labels.Length; i++) {
            int index = i;

            var text = new Label {
                Text = labels[i],
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.WordWrap,
            };

            var box = new Border {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                StrokeThickness = 1,
                Padding = new Thickness(6, 10),
                Content = text,
            };

            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) => set(index);   // настройка вызовет Changed, и вид обновится
            box.GestureRecognizers.Add(tap);

            host.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            host.Add(box, i, 0);
            items.Add((box, text));
        }

        void Refresh()
        {
            int current = get();

            for (int i = 0; i < items.Count; i++) {
                bool on = i == current;
                items[i].Box.SetDynamicResource(VisualElement.BackgroundColorProperty, on ? "TabActiveBg" : "PageBg");
                items[i].Box.SetDynamicResource(Border.StrokeProperty, on ? "AccentGreen" : "CardStroke");
                items[i].Text.SetDynamicResource(Label.TextColorProperty, on ? "BrandGreen" : "TextSecondary");
            }
        }

        _syncs.Add(Refresh);
    }

    private void SyncAll()
    {
        foreach (var sync in _syncs)
            sync();

        FolderPathLabel.Text = SettingsService.SaveFolder;
        ResetFolderButton.IsVisible = !SettingsService.IsDefaultFolder;
    }

    // =====================================================================
    //  ПАПКА СОХРАНЕНИЯ (ПК)
    // =====================================================================

    private async void OnPickFolderTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        try {
            var picker = new Windows.Storage.Pickers.FolderPicker {
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            };
            picker.FileTypeFilter.Add("*");

            // Окну приложения нужно сообщить выбору папки, к кому он привязан
            if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not MauiWinUIWindow window)
                return;

            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is not null)
                SettingsService.SaveFolder = folder.Path;
        } catch (Exception ex) {
            await Alert("Не удалось выбрать папку", ex.Message);
        }
#else
        await Task.CompletedTask;
#endif
    }

    private void OnResetFolderTapped(object? sender, TappedEventArgs e)
        => SettingsService.SaveFolder = SettingsService.DefaultFolder;

    // =====================================================================
    //  ДАННЫЕ И ПРИВАТНОСТЬ
    // =====================================================================

    private async void OnClearTempTapped(object? sender, TappedEventArgs e)
    {
        int count = FileSaveService.ClearTemp();

        await Alert("Временные файлы",
            count == 0 ? "Временных файлов нет." : $"Удалено файлов: {count}.");
    }

    private async void OnDeleteProfileTapped(object? sender, TappedEventArgs e)
    {
        if (!await Confirm("Удалить профиль?",
                "Данные профиля (ФИО, паспорт, УНП и другие) будут стёрты с этого устройства.", "Удалить"))
            return;

        try {
            ProfileStorage.Clear();
            DataCleared?.Invoke();
            await Alert("Готово", "Профиль удалён.");
        } catch (Exception ex) {
            await Alert("Не удалось удалить", ex.Message);
        }
    }

    private async void OnDeleteAllTapped(object? sender, TappedEventArgs e)
    {
        if (!await Confirm("Удалить все данные?",
                "Будут удалены профиль, введённые в формы значения, список недавних шаблонов и временные файлы. " +
                "Уже сохранённые документы останутся на месте.", "Удалить всё"))
            return;

        try {
            ProfileStorage.Clear();
            DraftStore.Clear();
            TemplatesPage.ClearRecent();
            FileSaveService.ClearTemp();

            DataCleared?.Invoke();
            await Alert("Готово", "Данные удалены.");
        } catch (Exception ex) {
            await Alert("Не удалось удалить", ex.Message);
        }
    }

    // =====================================================================
    //  О ПРИЛОЖЕНИИ
    // =====================================================================

    private async void OnHelpTapped(object? sender, TappedEventArgs e)
    {
        await Alert("Как пользоваться",
            "1. Заполните «Мой профиль»: данные подставятся в документы.\n" +
            "2. Выберите шаблон и заполните поля.\n" +
            "3. Нажмите «Собрать документ» и сохраните файл Word.\n\n" +
            "Всё хранится только на вашем устройстве.");
    }

    // =====================================================================
    //  ДИАЛОГИ
    // =====================================================================

    private static Task Alert(string title, string text)
        => Shell.Current.DisplayAlertAsync(title, text, "Понятно");

    private static Task<bool> Confirm(string title, string text, string accept)
        => Shell.Current.DisplayAlertAsync(title, text, accept, "Отмена");
}
