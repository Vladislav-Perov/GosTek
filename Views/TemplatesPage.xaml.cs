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

    private const string FavoritesChipTitle = "⭐ Избранное";
    private bool _isFavoriteFilter;

    private string? _category;   // null = «Все» (или режим «Избранное»)
    private string[] _categories = Array.Empty<string>();   // категории чипов (кроме «Избранное» и «Все»)
    private string _query = "";

    private IDispatcherTimer? _watchTimer;
    private string _signature = "";
    private bool _isFirstLoad = true;
    private bool _isUpdatingFavorite;

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
        StartWatching();

        if (_isFirstLoad)
        {
            _isFirstLoad = false;
            CatalogSkeleton.StartShimmer();
            ResultsList.IsVisible = false;
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
                MainThread.BeginInvokeOnMainThread(ReloadTemplates);
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
        var rawCats = TemplateCatalog.Categories;
        var sortedCats = rawCats
            .Distinct()
            .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (_category is not null && !sortedCats.Contains(_category))
            _category = null;

        _categories = sortedCats;

        // Порядок категорий:
        // 1. Первым элементом (индекс 0) ВСЕГДА идет категория "Все".
        // 2. Вторым элементом (индекс 1) ВСЕГДА идет категория "Избранное" (со звездочкой).
        // 3. Все остальные категории выводятся строго по алфавиту (А-Я) после первых двух.
        var titles = new List<string> { "Все", FavoritesChipTitle };
        titles.AddRange(sortedCats);

        int selected;
        if (_isFavoriteFilter)
            selected = 1;
        else if (_category is null)
            selected = 0;
        else
        {
            int catIdx = Array.IndexOf(sortedCats, _category);
            selected = catIdx >= 0 ? catIdx + 2 : 0;
        }

        CategoryChips.SetItems(titles, selected);
    }

    // Выбрали чип: 0 = «Все», 1 = «⭐ Избранное», дальше категории по алфавиту
    private void OnCategoryChanged(object? sender, int index)
    {
        HapticHelper.Click();
        if (index == 0)
        {
            _isFavoriteFilter = false;
            _category = null;
        }
        else if (index == 1)
        {
            _isFavoriteFilter = true;
            _category = null;
        }
        else
        {
            _isFavoriteFilter = false;
            int catIdx = index - 2;
            _category = (catIdx >= 0 && catIdx < _categories.Length) ? _categories[catIdx] : null;
        }
        ApplyFilter();
    }

    // ===== Фильтрация =====
    private void ApplyFilter()
    {
        var favIds = FavoritesService.Instance.GetAll();

        var queryable = TemplateCatalog.Items.AsEnumerable();

        if (_isFavoriteFilter)
            queryable = queryable.Where(t => favIds.Contains(t.Id));
        else if (_category is not null)
            queryable = queryable.Where(t => t.Category == _category);

        if (_query.Length > 0)
        {
            queryable = queryable.Where(t =>
                t.Title.Contains(_query, StringComparison.CurrentCultureIgnoreCase)
                || t.Description.Contains(_query, StringComparison.CurrentCultureIgnoreCase));
        }

        var results = queryable.ToList();

        BindableLayout.SetItemsSource(ResultsList, results);
        EmptyLabel.IsVisible = results.Count == 0;
        EmptyCard.IsVisible = results.Count == 0;
        if (TemplateCatalog.Items.Length == 0)
        {
            EmptyLabel.Text = $"В папке шаблонов нет файлов .docx:\n{SettingsService.TemplatesFolder}\n\nИзменить папку можно в настройках.";
        }
        else if (results.Count == 0 && _isFavoriteFilter)
        {
            EmptyLabel.Text = "У вас пока нет избранных шаблонов.\nНажмите на звёздочку в карточке шаблона, чтобы добавить его сюда.";
        }

        // «Недавние» — только когда нет поиска, не включён фильтр избранного и выбрано «Все»
        var recent = LoadRecent();
        BindableLayout.SetItemsSource(RecentList, recent);
        RecentSection.IsVisible = _query.Length == 0 && !_isFavoriteFilter && _category is null && recent.Count > 0;

        // Синхронизация состояния строк (звёздочки избранного и подсветка совпадений)
        Dispatcher.Dispatch(async () =>
        {
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

    private async void OnFavoriteTapped(object? sender, EventArgs e)
    {
        if (_isUpdatingFavorite)
            return;

        if (sender is ListRow row && row.BindingContext is TemplateItem item)
        {
            _isUpdatingFavorite = true;
            try
            {
                await row.AnimateFavoriteToggleAsync();

                bool isFav = FavoritesService.Instance.Toggle(item.Id);
                Toast.Show(isFav ? "Добавлено в избранное" : "Удалено из избранного", isFav ? "starfilled" : "star");
            }
            finally
            {
                _isUpdatingFavorite = false;
            }
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
