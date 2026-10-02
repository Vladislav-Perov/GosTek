namespace GosTek.Views;

public partial class MainPage : ContentPage
{
    private int _lastSubtitleIndex = -1;

    // ===== Случайные подзаголовки под GosTek =====
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

    // Ключ -> (заголовок, что здесь будет)
    private static readonly Dictionary<string, (string Title, string Text)> Stubs = new() {
        ["create"] = ("📄 Создание документа",
            "Скоро здесь появится выбор шаблона и форма для заполнения полей. На выходе вы получите готовый файл Word или PDF."),

        ["check"] = ("✔️ Проверка документа",
            "Скоро здесь можно будет загрузить документ и получить список ошибок оформления: поля, шрифты, реквизиты и отступы."),

        ["scan"] = ("📷 Сканирование",
            "Скоро здесь появится сканирование документа камерой с распознаванием текста и автозаполнением данных."),

        ["profile"] = ("👤 Мой профиль",
            "Скоро здесь будут ваши данные и реквизиты, в том числе расчётный счёт, который сейчас не заполнен."),

        ["templates"] = ("📋 Шаблоны",
            "Скоро здесь появится каталог шаблонов документов с поиском и категориями."),

        ["explanatory"] = ("📄 Объяснительная записка",
            "Скоро здесь будет готовый шаблон: причина опоздания, нарушения срока или отсутствия."),

        ["ip"] = ("📄 Заявление о регистрации ИП",
            "Скоро здесь будет шаблон заявления для подачи в регистрирующий орган."),
    };

    public MainPage()
    {
        InitializeComponent();
        SyncToggleVisual(animated: false);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ShowRandomSubtitle();
        SyncToggleVisual(animated: false);
    }

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

    // ===== Общая заглушка "Скоро будет добавлено" =====
    private async void OnStubTapped(object? sender, TappedEventArgs e)
    {
        var key = e.Parameter as string;

        if (key is not null && Stubs.TryGetValue(key, out var stub))
            await DisplayAlertAsync(stub.Title, stub.Text, "Понятно");
        else
            await DisplayAlertAsync("Скоро", "Этот раздел скоро будет добавлен.", "Понятно");
    }

    // ===== Переключатель темы =====
    private void OnThemeToggleTapped(object? sender, TappedEventArgs e)
    {
        ThemeService.Toggle();          // меняет цвета на всей странице
        SyncToggleVisual(animated: true);
    }

    // Иконка и положение ползунка по текущей теме
    private void SyncToggleVisual(bool animated)
    {
        bool dark = ThemeService.IsDark;
        ThemeIcon.Text = dark ? "🌙" : "☀️";

        // 40 (дорожка) - 16 (ползунок) - 3 - 3 (отступы) = 18
        double target = dark ? 0 : 18;

        if (animated)
            _ = ToggleThumb.TranslateToAsync(target, 0, 150, Easing.CubicInOut);
        else
            ToggleThumb.TranslationX = target;
    }
}