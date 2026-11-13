/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Главный дашборд приложения со статусом профиля, быстрым доступом и шторкой настроек
 * СОДЕРЖИМОЕ: MainPage
 * ЗАВИСИМОСТИ: GosTek.Views, GosTek.SettingsService, GosTek.FileSaveService, GosTek.Helpers
 * ==============================================================================
 */
using GosTek.Controls;
using GosTek.Helpers;
using GosTek.Models;
using GosTek.Services;

namespace GosTek.Views;

public partial class MainPage : ContentPage, IQueryAttributable
{
    private int _lastSubtitleIndex = -1;

    private static readonly string[] Subtitles =
    {
        "Какой документ оформим сегодня?",
        "Что будем оформлять на этот раз?",
        "С какого шаблона начнем?",
        "Какой документ подготовить?",
        "Выберите шаблон — остальное сделаем мы.",
        "Начните оформление прямо сейчас.",
        "Создайте документ за пару кликов.",
        "Оформление документов по шаблонам.",
        "Подготовка документа к работе.",
        "Выбор шаблона для оформления.",
        "Ну что, оформим бумаги?",
        "Готовы создать новый документ?",
    };

    public MainPage()
    {
        InitializeComponent();

        // Данные удалили в настройках: обновить плашку профиля
        SettingsOverlay.DataCleared += async () => await RefreshProfileCardAsync();

        AttachHoverEffect(CheckCard);
        AttachHoverEffect(ScanCard);
    }

    private static void AttachHoverEffect(Border border)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            _ = border.ScaleToAsync(1.02, 100);
            if (Application.Current?.Resources.TryGetValue("AccentGreen", out var color) == true && color is Color c)
                border.Stroke = new SolidColorBrush(c);
        };
        pointer.PointerExited += (_, _) =>
        {
            _ = border.ScaleToAsync(1.0, 100);
            if (Application.Current?.Resources.TryGetValue("CardStroke", out var color) == true && color is Color c)
                border.Stroke = new SolidColorBrush(c);
        };
        border.GestureRecognizers.Add(pointer);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("openSettings", out var val) && val?.ToString() == "true")
        {
            Dispatcher.Dispatch(async () => await SettingsOverlay.ShowAsync(this));
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ShowRandomSubtitle();

        // «Быстрый старт»: первые три шаблона из папки шаблонов
        TemplateCatalog.Refresh();
        var quick = TemplateCatalog.Items.Take(3).ToList();
        BindableLayout.SetItemsSource(QuickStartList, quick);
        QuickStartSection.IsVisible = quick.Count > 0;   // нет шаблонов — нет и блока
        QuickStartDivider.IsVisible = quick.Count > 0;
        if (quick.Count > 0)
        {
            Dispatcher.Dispatch(async () =>
            {
                var rows = QuickStartList.Children.OfType<VisualElement>().ToList();
                if (rows.Count > 0)
                    await AnimationHelper.AnimateStaggeredEntranceAsync(rows);
            });
        }
        await RefreshProfileCardAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SettingsOverlay.Dismiss();
    }

    // ===== Настройки =====
    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
        await SettingsOverlay.ShowAsync(this);
    }

    // «Назад» на Android: сначала закрываем панель, а не выходим с главной
    protected override bool OnBackButtonPressed()
    {
        if (SettingsOverlay.IsOpen) {
            _ = SettingsOverlay.CloseAsync();
            return true;
        }

        return base.OnBackButtonPressed();
    }

    /// <summary>Флаг отображения шторки настроек.</summary>
    public bool IsSettingsShown => SettingsOverlay.IsOpen;

    /// <summary>Закрыть шторку настроек. Возвращает true, если панель была открыта.</summary>
    public async Task<bool> TryCloseSettingsAsync()
    {
        if (SettingsOverlay.IsOpen)
        {
            await SettingsOverlay.CloseAsync();
            return true;
        }
        return false;
    }

    // ===== Плашка «Мой профиль»: те же данные и правила, что на странице профиля =====
    private async Task RefreshProfileCardAsync()
    {
        var (profile, status) = await ProfileStorage.LoadWithStatusAsync();
        var summary = ProfileRules.Summarize(profile);

        // Профиль заполнен полностью: плашка и разделитель над ней не нужны
        bool complete = status != ProfileLoadStatus.Failed && summary.Percent >= 100;
        ProfileCard.IsVisible = !complete;
        ProfileDivider.IsVisible = !complete;

        ProfilePercentLabel.Text = $"{summary.Percent}%";
        _ = ProfileProgress.ProgressTo(summary.Progress, 800, Easing.CubicOut);
        ProfileHintLabel.Text = status == ProfileLoadStatus.Failed
            ? "Не удалось прочитать сохранённый профиль. Откройте его и заполните заново."
            : $"Заполнено на {summary.Percent}%. {summary.Hint}";
    }

    // ===== Подзаголовок: случайная фраза, не повторяющая предыдущую =====
    private void ShowRandomSubtitle()
    {
        int index;
        do {
            index = Random.Shared.Next(Subtitles.Length);
        }
        while (index == _lastSubtitleIndex);

        _lastSubtitleIndex = index;
        SubtitleLabel.Text = Subtitles[index];
    }

    // ===== «Проверить» ведёт на экран проверки документа =====
    private async void OnCheckTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
        await Shell.Current.GoToAsync("//CheckPage");
    }

    // ===== «Создать документ» ведёт в каталог шаблонов =====
    private async void OnCreateTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
        await Shell.Current.GoToAsync("//TemplatesPage");
    }

    // ===== Скан из «Быстрого старта» — настоящий экран, не заглушка =====
    private async void OnScanTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
        await Shell.Current.GoToAsync("//ScanPage");
    }

    // ===== Шаблон из «Быстрого старта» =====
    private async void OnTemplateTapped(object? sender, EventArgs e)
    {
        if (sender is ListRow { BindingContext: TemplateItem item })
            await TemplatesPage.OpenAsync(item);
    }

    // ===== Переход в профиль =====
    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        HapticHelper.Click();
        if (sender is VisualElement ve)
            await ve.AnimateBounceAsync();
        await Shell.Current.GoToAsync("//ProfilePage");
    }
}
