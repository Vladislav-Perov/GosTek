namespace GosTek.Views;

public record TemplateItem(string Id, string Title, string Description, string Category);

public partial class TemplatesPage : ContentPage
{
    private const string RecentKey = "recent_templates";
    private const int RecentMax = 3;

    // Ключ чипа -> название категории (null = без фильтра)
    private static readonly Dictionary<string, string?> CategoryNames = new() {
        ["all"] = null,
        ["biz"] = "Предприниматель",
        ["org"] = "Организация",
        ["study"] = "Учёба",
    };

    private static readonly List<TemplateItem> AllTemplates = new()
    {
        new("explanatory", "Объяснительная записка", "Причина опоздания, нарушения срока, отсутствия", "Организация"),
        new("ip",          "Заявление о регистрации ИП", "Для подачи в регистрирующий орган", "Предприниматель"),
        new("title",       "Титульный лист работы", "Курсовая, дипломная, отчёт по практике", "Учёба"),
        new("contract",    "Договор оказания услуг", "С реквизитами заказчика и исполнителя", "Предприниматель"),
        new("proxy",       "Доверенность", "На получение товара или представление интересов", "Организация"),
        new("practice",    "Отчёт по практике", "Структура по требованиям кафедры", "Учёба"),
    };

    private readonly Dictionary<string, (Border Chip, Label Text)> _chips;

    private string _category = "all";
    private string _query = "";

    public TemplatesPage()
    {
        InitializeComponent();

        _chips = new() {
            ["all"] = (ChipAll, ChipAllLabel),
            ["biz"] = (ChipBiz, ChipBizLabel),
            ["org"] = (ChipOrg, ChipOrgLabel),
            ["study"] = (ChipStudy, ChipStudyLabel),
        };

        UpdateChips();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyFilter();
        SyncToggleVisual(animated: false);   // подхватить тему, выбранную на другой странице
        BottomTabs.ResetHover();             // сбросить «залипшее» наведение таббара
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

    // ===== Поиск =====
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _query = (e.NewTextValue ?? "").Trim();
        ApplyFilter();
    }

    // ===== Категории =====
    private void OnChipTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string key || !CategoryNames.ContainsKey(key))
            return;

        _category = key;
        UpdateChips();
        ApplyFilter();
    }

    private void UpdateChips()
    {
        foreach (var (key, (chip, label)) in _chips) {
            bool active = key == _category;

            chip.SetDynamicResource(VisualElement.BackgroundColorProperty, active ? "TextPrimary" : "CardBg");
            chip.SetDynamicResource(Border.StrokeProperty, "CardStroke");
            chip.StrokeThickness = active ? 0 : 1;

            label.SetDynamicResource(Label.TextColorProperty, active ? "PageBg" : "TextPrimary");
            label.FontAttributes = active ? FontAttributes.Bold : FontAttributes.None;
        }
    }

    // ===== Фильтрация =====
    private void ApplyFilter()
    {
        string? categoryName = CategoryNames[_category];

        var results = AllTemplates
            .Where(t => categoryName is null || t.Category == categoryName)
            .Where(t => _query.Length == 0
                     || t.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                     || t.Description.Contains(_query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        BindableLayout.SetItemsSource(ResultsList, results);
        EmptyLabel.IsVisible = results.Count == 0;

        // «Недавние» — только когда нет ни поиска, ни фильтра
        var recent = LoadRecent();
        BindableLayout.SetItemsSource(RecentList, recent);
        RecentSection.IsVisible = _query.Length == 0 && _category == "all" && recent.Count > 0;
    }

    // ===== Нажатие на шаблон =====
    private async void OnTemplateTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string id)
            return;

        var item = AllTemplates.FirstOrDefault(t => t.Id == id);
        if (item is null)
            return;

        AddRecent(id);

        await DisplayAlertAsync("📄 " + item.Title,
            "Скоро здесь будет форма заполнения этого шаблона.",
            "Понятно");

        ApplyFilter();
    }

    // ===== Недавние (хранятся в Preferences) =====
    private static List<TemplateItem> LoadRecent()
    {
        return Preferences.Get(RecentKey, "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => AllTemplates.FirstOrDefault(t => t.Id == id))
            .OfType<TemplateItem>()
            .ToList();
    }

    private static void AddRecent(string id)
    {
        var ids = Preferences.Get(RecentKey, "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

        ids.Remove(id);
        ids.Insert(0, id);

        Preferences.Set(RecentKey, string.Join(",", ids.Take(RecentMax)));
    }
}