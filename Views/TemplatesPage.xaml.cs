using GosTek.Models;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

public partial class TemplatesPage : ContentPage
{
    private const string RecentKey = "recent_templates";
    private const int RecentMax = 3;

    // Категории для чипов; null = «Все» (без фильтра)
    private static readonly string?[] Categories = { null, "Предприниматель", "Организация", "Учёба" };

    private readonly List<(string? Category, Border Chip, Label Text)> _chips = new();

    private string? _category;   // null = «Все»
    private string _query = "";

    public TemplatesPage()
    {
        InitializeComponent();

        foreach (var category in Categories)
            CreateChip(category);

        UpdateChips();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ApplyFilter();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
    }

    // ===== Поиск =====
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _query = (e.NewTextValue ?? "").Trim();
        ApplyFilter();
    }

    // ===== Категории =====
    private void CreateChip(string? category)
    {
        var label = new Label { Text = category ?? "Все", FontSize = 14 };
        var chip = new Border {
            StrokeShape = new RoundRectangle { CornerRadius = 18 },
            Padding = new Thickness(16, 8),
            Content = label,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => {
            _category = category;
            UpdateChips();
            ApplyFilter();
        };
        chip.GestureRecognizers.Add(tap);

        ChipsHost.Children.Add(chip);
        _chips.Add((category, chip, label));
    }

    private void UpdateChips()
    {
        foreach (var (category, chip, label) in _chips) {
            bool active = category == _category;

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
        var results = TemplateCatalog.Items
            .Where(t => _category is null || t.Category == _category)
            .Where(t => _query.Length == 0
                     || t.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                     || t.Description.Contains(_query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        BindableLayout.SetItemsSource(ResultsList, results);
        EmptyLabel.IsVisible = results.Count == 0;

        // «Недавние» — только когда нет ни поиска, ни фильтра
        var recent = LoadRecent();
        BindableLayout.SetItemsSource(RecentList, recent);
        RecentSection.IsVisible = _query.Length == 0 && _category is null && recent.Count > 0;
    }

    // ===== Нажатие на шаблон =====
    private async void OnTemplateTapped(object? sender, EventArgs e)
    {
        if (sender is ListRow { BindingContext: TemplateItem item })
            await OpenAsync(item);
    }

    /// <summary>Открыть шаблон (и с этой страницы, и с главной). Без описания полей — заглушка.</summary>
    public static async Task OpenAsync(TemplateItem item)
    {
        if (TemplateCatalog.Find(item.Id) is null) {
            await Stubs.ShowTemplateAsync(item.Title);
            return;
        }

        AddRecent(item.Id);
        await Shell.Current.GoToAsync($"FieldsPage?id={item.Id}");
    }

    // ===== Недавние (хранятся в Preferences) =====
    private static List<string> LoadRecentIds()
        => Preferences.Get(RecentKey, "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .ToList();

    private static List<TemplateItem> LoadRecent()
        => LoadRecentIds()
            .Select(id => TemplateCatalog.Items.FirstOrDefault(t => t.Id == id))
            .OfType<TemplateItem>()
            .ToList();

    /// <summary>Очистить список «Недавние» (настройки: «Удалить все данные»).</summary>
    public static void ClearRecent() => Preferences.Remove(RecentKey);

    private static void AddRecent(string id)
    {
        var ids = LoadRecentIds();

        ids.Remove(id);
        ids.Insert(0, id);

        Preferences.Set(RecentKey, string.Join(",", ids.Take(RecentMax)));
    }
}
