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
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        ShowRandomSubtitle();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара

        await RefreshProfileCardAsync();
    }

    // ===== Плашка «Мой профиль»: те же данные и правила, что на странице профиля =====
    private async Task RefreshProfileCardAsync()
    {
        var profile = await ProfileStorage.LoadAsync();
        var summary = ProfileRules.Summarize(profile);

        ProfilePercentLabel.Text = $"{summary.Percent}%";
        ProfileProgress.Progress = summary.Progress;
        ProfileHintLabel.Text = $"Заполнено на {summary.Percent}%. {summary.Hint}";
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
