using GosTek.Models;

namespace GosTek.Views;

public partial class MainPage : ContentPage
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

        // «Быстрый старт»: первые два шаблона каталога
        BindableLayout.SetItemsSource(QuickStartList, TemplateCatalog.Items.Take(2).ToList());

        // Данные удалили в настройках: обновить плашку профиля
        Settings.DataCleared += async () => await RefreshProfileCardAsync();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ShowRandomSubtitle();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
#if WINDOWS
        HookEscape();
#endif

        await RefreshProfileCardAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if WINDOWS
        UnhookEscape();
#endif
    }

    // ===== Настройки =====
    private async void OnSettingsTapped(object? sender, TappedEventArgs e)
    {
        await Settings.OpenAsync();
    }

    // «Назад» на Android: сначала закрываем панель, а не выходим с главной
    protected override bool OnBackButtonPressed()
    {
        if (Settings.IsShown) {
            _ = Settings.CloseAsync();
            return true;
        }

        return base.OnBackButtonPressed();
    }

#if WINDOWS
    // Esc на Windows закрывает панель. Горячая клавиша ставится на корень окна,
    // поэтому работает, даже если фокус не в панели
    private Microsoft.UI.Xaml.Input.KeyboardAccelerator? _escape;
    private Microsoft.UI.Xaml.UIElement? _escapeRoot;

    private void HookEscape()
    {
        if (_escape is not null)
            return;

        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not MauiWinUIWindow window
            || window.Content is not Microsoft.UI.Xaml.UIElement root)
            return;

        _escape = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = global::Windows.System.VirtualKey.Escape };
        _escape.Invoked += (sender, args) => {
            if (!Settings.IsShown)
                return;

            args.Handled = true;
            _ = Settings.CloseAsync();
        };

        root.KeyboardAccelerators.Add(_escape);
        _escapeRoot = root;
    }

    private void UnhookEscape()
    {
        if (_escape is not null && _escapeRoot is not null)
            _escapeRoot.KeyboardAccelerators.Remove(_escape);

        _escape = null;
        _escapeRoot = null;
    }
#endif

    // ===== Плашка «Мой профиль»: те же данные и правила, что на странице профиля =====
    private async Task RefreshProfileCardAsync()
    {
        var (profile, status) = await ProfileStorage.LoadWithStatusAsync();
        var summary = ProfileRules.Summarize(profile);

        ProfilePercentLabel.Text = $"{summary.Percent}%";
        ProfileProgress.Progress = summary.Progress;
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

    // ===== Заглушки (create, check, scan) =====
    private async void OnStubTapped(object? sender, TappedEventArgs e)
    {
        await Stubs.ShowAsync(e.Parameter as string);
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
        await Shell.Current.GoToAsync("//ProfilePage");
    }
}
