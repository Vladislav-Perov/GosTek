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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ShowRandomSubtitle();
        SyncToggleVisual(animated: false);   // подхватить тему, выбранную на другой странице
        BottomTabs.ResetHover();             // сбросить «залипшее» наведение таббара

        await RefreshProfileCardAsync();     // обновить плашку профиля
    }
    // ===== Плашка «Мой профиль»: те же данные и правила, что на странице профиля =====
    private async Task RefreshProfileCardAsync()
    {
        var profile = await ProfileStorage.LoadAsync();
        var summary = ProfileRules.Summarize(ProfileRules.ToValues(profile));

        ProfilePercentLabel.Text = $"{summary.Percent}%";
        ProfileProgress.Progress = summary.Progress;
        ProfileHintLabel.Text = $"Заполнено на {summary.Percent}%. {summary.Hint}";
    }
    // ===== Подзаголовок =====
    // Случайная фраза, не повторяющая предыдущую
    private void ShowRandomSubtitle()
    {
        int index;
        do {
            index = Random.Shared.Next(Subtitles.Length);
        }
        while (index == _lastSubtitleIndex && Subtitles.Length > 1);

        _lastSubtitleIndex = index;
        SubtitleLabel.Text = Subtitles[index];
    }

    // ===== Заглушки =====
    private async void OnStubTapped(object? sender, TappedEventArgs e)
    {
        await Stubs.ShowAsync(e.Parameter as string);
    }
    // ===== Открыть шаблон из «Быстрого старта» =====
    private async void OnTemplateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string id)
            return;

        TemplatesPage.AddRecent(id);
        await Shell.Current.GoToAsync($"FieldsPage?id={id}");
    }
    // ===== Переход в профиль =====
    private async void OnProfileTapped(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//ProfilePage");
    }
    // ===== Переключатель темы =====
    private void OnThemeToggleTapped(object? sender, TappedEventArgs e)
    {
        ThemeService.Toggle();
        SyncToggleVisual(animated: true);
    }

    private void SyncToggleVisual(bool animated)
    {
        bool dark = ThemeService.IsDark;
        ThemeIcon.Text = dark ? "🌙" : "☀️";

        double target = dark ? 0 : 18;

        if (animated)
            _ = ToggleThumb.TranslateToAsync(target, 0, 150, Easing.CubicInOut);
        else
            ToggleThumb.TranslationX = target;
    }
}