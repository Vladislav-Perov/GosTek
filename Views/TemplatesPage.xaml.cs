using GosTek.Models;
using Microsoft.Maui.Controls.Shapes;

namespace GosTek.Views;

public partial class TemplatesPage : ContentPage
{
    private const string RecentKey = "recent_templates";
    private const int RecentMax = 3;

    private readonly List<(string? Category, Border Chip, Label Text)> _chips = new();

    private string? _category;   // null = «Все»
    private string _query = "";

    private IDispatcherTimer? _watchTimer;
    private string _signature = "";

    public TemplatesPage()
    {
        InitializeComponent();

        // Стрелки по краям списка категорий показываем, только когда есть куда листать
        ChipsScroll.Scrolled += (_, _) => UpdateChipArrows();
        ChipsScroll.SizeChanged += (_, _) => UpdateChipArrows();
        ChipsHost.SizeChanged += (_, _) => UpdateChipArrows();
#if WINDOWS
        // На ПК колесо мыши по умолчанию не листает горизонтальный список — включаем
        ChipsScroll.HandlerChanged += (_, _) => HookChipsWheel();
#endif
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ReloadTemplates();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
        StartWatching();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        StopWatching();
    }

    private void ReloadTemplates()
    {
        TemplateCatalog.Refresh();   // подхватить новые/удалённые файлы в папке шаблонов
        _signature = TemplateCatalog.FolderSignature();
        RebuildChips();
        ApplyFilter();
    }

    // ===== Автообновление: пока страница открыта, следим за папкой шаблонов =====
    private void StartWatching()
    {
        _watchTimer ??= Dispatcher.CreateTimer();
        _watchTimer.Interval = TimeSpan.FromSeconds(1.5);
        _watchTimer.Tick -= OnWatchTick;
        _watchTimer.Tick += OnWatchTick;
        _watchTimer.Start();
    }

    private void StopWatching() => _watchTimer?.Stop();

    private async void OnWatchTick(object? sender, EventArgs e)
    {
        _watchTimer?.Stop();   // не накладывать проверки друг на друга
        try {
            var sig = await Task.Run(TemplateCatalog.FolderSignature);
            if (sig != _signature) {
                await Task.Delay(500);   // дать файлу докопироваться
                ReloadTemplates();
            }
        } catch {
            // папка временно недоступна — попробуем в следующий раз
        } finally {
            _watchTimer?.Start();
        }
    }

#if WINDOWS
    private void HookChipsWheel()
    {
        if (ChipsScroll.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.ScrollViewer sv)
            return;

        sv.PointerWheelChanged += (_, args) => {
            var delta = args.GetCurrentPoint(sv).Properties.MouseWheelDelta;
            sv.ChangeView(sv.HorizontalOffset - delta, null, null);
            args.Handled = true;
        };
    }
#endif

    // ===== Стрелки прокрутки категорий =====
    private void UpdateChipArrows()
    {
        double max = ChipsScroll.ContentSize.Width - ChipsScroll.Width;
        ChipsLeftArrow.IsVisible = ChipsScroll.ScrollX > 1;
        ChipsRightArrow.IsVisible = max > 1 && ChipsScroll.ScrollX < max - 1;
    }

    private async void OnChipsLeftTapped(object? sender, TappedEventArgs e)
        => await ChipsScroll.ScrollToAsync(Math.Max(0, ChipsScroll.ScrollX - 200), 0, true);

    private async void OnChipsRightTapped(object? sender, TappedEventArgs e)
        => await ChipsScroll.ScrollToAsync(ChipsScroll.ScrollX + 200, 0, true);

    // ===== Поиск =====
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _query = (e.NewTextValue ?? "").Trim();
        ApplyFilter();
    }

    // ===== Категории (берутся из имён файлов) =====
    private void RebuildChips()
    {
        ChipsHost.Children.Clear();
        _chips.Clear();

        var cats = TemplateCatalog.Categories;
        if (_category is not null && !cats.Contains(_category))
            _category = null;

        CreateChip(null);
        foreach (var category in cats)
            CreateChip(category);

        UpdateChips();
    }

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
        if (TemplateCatalog.Items.Length == 0)
            EmptyLabel.Text = $"В папке шаблонов нет файлов .docx:\n{SettingsService.TemplatesFolder}\n\nИзменить папку можно в настройках.";

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
            await Stubs.ShowTemplateAsync(item.Title);   // файл удалили или переименовали
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
