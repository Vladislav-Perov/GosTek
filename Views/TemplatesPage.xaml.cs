/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Каталог шаблонов документов с автообновлением, категоризацией, избранным и поиском с подсветкой
 * СОДЕРЖИМОЕ: TemplatesPage
 * ЗАВИСИМОСТИ: GosTek.Models, GosTek.Services, GosTek.TemplateEngine, GosTek.Views.Controls, GosTek.Helpers
 * ==============================================================================
 */
using GosTek.Helpers;
using GosTek.Models;
using GosTek.Services;
using GosTek.Views.Controls;

namespace GosTek.Views;

public partial class TemplatesPage : ContentPage
{
    private const string RecentKey = "recent_templates";
    private const int RecentMax = 3;

    private string? _category;   // null = «Все»
    private string[] _categories = Array.Empty<string>();   // категории чипов (кроме «Все»)
    private string _query = "";

    private IDispatcherTimer? _watchTimer;
    private string _signature = "";
    private bool _isFirstLoad = true;

    public TemplatesPage()
    {
        InitializeComponent();

        CategoryChips.SelectionChanged += OnCategoryChanged;
        FavoritesService.Instance.Changed += OnFavoritesChanged;
    }

    private void OnFavoritesChanged()
    {
        ApplyFilter();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        BottomTabs.ResetHover();   // сбросить «залипшее» наведение таббара
        StartWatching();

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            CatalogSkeleton.StartShimmer();
            ResultsList.IsVisible = false;
            FavoritesSection.IsVisible = false;
            RecentSection.IsVisible = false;

            await Task.Delay(250);   // реалистичная скелетон-анимация при открытии каталога
            ReloadTemplates();
            CatalogSkeleton.StopShimmer();
            ResultsList.IsVisible = true;
        }
        else
        {
            ReloadTemplates();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CatalogSkeleton.StopShimmer();
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
        try
        {
            var sig = await Task.Run(TemplateCatalog.FolderSignature);
            if (sig != _signature)
            {
                await Task.Delay(500);   // дать файлу докопироваться
                ReloadTemplates();
            }
        }
        catch (Exception ex)
        {
            // папка временно недоступна — попробуем в следующий раз
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
        }
        finally
        {
            _watchTimer?.Start();
        }
    }

    // ===== Поиск =====
    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        _query = (e.NewTextValue ?? "").Trim();
        ApplyFilter();
    }

    // ===== Категории (берутся из имён файлов) =====
    private void RebuildChips()
    {
        var cats = TemplateCatalog.Categories;
        if (_category is not null && !cats.Contains(_category))
            _category = null;

        _categories = cats;

        var titles = new List<string> { "Все" };
        titles.AddRange(cats);

        int selected = _category is null ? 0 : Array.IndexOf(cats, _category) + 1;
        CategoryChips.SetItems(titles, selected);
    }

    // Выбрали чип: 0 = «Все», дальше категории по порядку
    private void OnCategoryChanged(object? sender, int index)
    {
        HapticHelper.Click();
        _category = index <= 0 || index > _categories.Length ? null : _categories[index - 1];
        ApplyFilter();
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
        EmptyCard.IsVisible = results.Count == 0;
        if (TemplateCatalog.Items.Length == 0)
            EmptyLabel.Text = $"В папке шаблонов нет файлов .docx:\n{SettingsService.TemplatesFolder}\n\nИзменить папку можно в настройках.";

        // «Избранные» — только когда нет ни поиска, ни фильтра
        var favIds = FavoritesService.Instance.GetAll();
        var favorites = favIds
            .Select(id => TemplateCatalog.Items.FirstOrDefault(t => t.Id == id))
            .OfType<TemplateItem>()
            .ToList();
        BindableLayout.SetItemsSource(FavoritesList, favorites);
        FavoritesSection.IsVisible = _query.Length == 0 && _category is null && favorites.Count > 0;

        // «Недавние» — только когда нет ни поиска, ни фильтра
        var recent = LoadRecent();
        BindableLayout.SetItemsSource(RecentList, recent);
        RecentSection.IsVisible = _query.Length == 0 && _category is null && recent.Count > 0;

        // Синхронизация состояния строк (звёздочки избранного и подсветка совпадений)
        Dispatcher.Dispatch(async () =>
        {
            UpdateRowsState(FavoritesList, favIds, "");
            UpdateRowsState(RecentList, favIds, "");
            UpdateRowsState(ResultsList, favIds, _query);

            var rows = ResultsList.Children.OfType<VisualElement>().ToList();
            if (rows.Count > 0)
                await AnimationHelper.AnimateStaggeredEntranceAsync(rows);
        });
    }

    private void UpdateRowsState(Layout layout, IReadOnlyList<string> favIds, string query)
    {
        var highlightColor = ThemeService.IsDark ? Color.FromArgb("#3FC3A1") : Color.FromArgb("#0D6B57");
        foreach (var child in layout.Children)
        {
            if (child is ListRow row && row.BindingContext is TemplateItem item)
            {
                row.IsFavorite = favIds.Contains(item.Id);
                row.SetHighlightedTitle(item.Title, query, highlightColor);
            }
        }
    }

    private void OnFavoriteTapped(object? sender, EventArgs e)
    {
        if (sender is ListRow { BindingContext: TemplateItem item })
        {
            bool isFav = FavoritesService.Instance.Toggle(item.Id);
            Toast.Show(isFav ? "Добавлено в избранное" : "Удалено из избранного", isFav ? "star" : "trash");
        }
    }

    // ===== Нажатие на шаблон =====
    private async void OnTemplateTapped(object? sender, EventArgs e)
    {
        HapticHelper.Click();
        if (sender is ListRow { BindingContext: TemplateItem item })
            await OpenAsync(item);
    }

    /// <summary>Открыть шаблон (и с этой страницы, и с главной). Без описания полей — заглушка.</summary>
    public static async Task OpenAsync(TemplateItem item)
    {
        if (TemplateCatalog.Find(item.Id) is null)
        {
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
