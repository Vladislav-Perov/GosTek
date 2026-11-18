/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: ViewModel для группового и точечного отображения замечаний на CheckPage
 * СОДЕРЖИМОЕ: CheckGroupVm, CheckItemVm, CheckIssueFilter
 * ЗАВИСИМОСТИ: GosTek.Models.CheckIssue, GosTek.Models.IssueItem, GosTek.Models.IssueKind
 * ==============================================================================
 */
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using GosTek.Models;

namespace GosTek.Views;

/// <summary>
/// Представление группы замечания (карточки) в списке Проверки.
/// Поддерживает свёрнутый и развёрнутый вид с точечными элементами.
/// </summary>
public sealed class CheckGroupVm : INotifyPropertyChanged
{
    public const int PageSize = 30;

    private readonly List<IssueItem> _allItems = new();
    private int _displayedCount = PageSize;
    private bool _isExpanded;
    private CheckIssue _issue;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CheckIssue Issue => _issue;
    public ObservableCollection<CheckItemVm> Items { get; }
    public ICommand LoadMoreCommand { get; }

    public CheckGroupVm(CheckIssue issue)
    {
        _issue = issue ?? throw new ArgumentNullException(nameof(issue));
        Items = new ObservableCollection<CheckItemVm>();
        LoadMoreCommand = new Command(() => LoadMore(PageSize));

        if (issue.Items != null)
        {
            _allItems.AddRange(issue.Items);
            int take = Math.Min(PageSize, _allItems.Count);
            for (int i = 0; i < take; i++)
            {
                Items.Add(new CheckItemVm(_allItems[i], this));
            }
            _displayedCount = Items.Count;
        }
    }

    public void LoadMore(int count = PageSize)
    {
        if (count <= 0) count = PageSize;
        int current = Items.Count;
        int toTake = Math.Min(count, _allItems.Count - current);
        if (toTake <= 0) return;

        for (int i = 0; i < toTake; i++)
        {
            Items.Add(new CheckItemVm(_allItems[current + i], this));
        }

        _displayedCount = Items.Count;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(HasMoreText));
        OnPropertyChanged(nameof(MoreText));
    }

    public void UpdateIssue(CheckIssue newIssue)
    {
        _issue = newIssue ?? throw new ArgumentNullException(nameof(newIssue));
        _allItems.Clear();
        if (newIssue.Items != null)
        {
            _allItems.AddRange(newIssue.Items);
        }

        // Сохраняем объём показанных строк (не меньше 30, но не больше доступных)
        int targetCount = Math.Min(Math.Max(PageSize, Items.Count), _allItems.Count);

        for (int i = 0; i < targetCount; i++)
        {
            var item = _allItems[i];
            if (i < Items.Count)
            {
                if (Items[i].Item.Index != item.Index ||
                    Items[i].Item.Text != item.Text ||
                    Items[i].Item.Actual != item.Actual ||
                    Items[i].Item.Expected != item.Expected)
                {
                    Items[i] = new CheckItemVm(item, this);
                }
            }
            else
            {
                Items.Add(new CheckItemVm(item, this));
            }
        }

        while (Items.Count > targetCount)
        {
            Items.RemoveAt(Items.Count - 1);
        }

        _displayedCount = Items.Count;

        OnPropertyChanged(nameof(Issue));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WhereText));
        OnPropertyChanged(nameof(DiffText));
        OnPropertyChanged(nameof(HasDiff));
        OnPropertyChanged(nameof(CanFix));
        OnPropertyChanged(nameof(CannotFix));
        OnPropertyChanged(nameof(FixAllHint));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(HasMoreText));
        OnPropertyChanged(nameof(MoreText));
        OnPropertyChanged(nameof(ExpandButtonText));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ExpandButtonText));
            }
        }
    }

    public bool HasItems => Items.Count > 0;
    public bool HasMore => _allItems.Count > Items.Count;
    public bool HasMoreText => HasMore;

    public string? MoreText
    {
        get
        {
            int remaining = _allItems.Count - Items.Count;
            if (remaining <= 0) return null;
            if (remaining >= PageSize) return $"Показать ещё {PageSize}";
            return $"Показать ещё {remaining}";
        }
    }

    public string Title => Issue.Title;

    public string WhereText
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Issue.Where)) return string.Empty;
            string w = Issue.Where.Trim();
            if (w.StartsWith("Затронуто абзацев: ", StringComparison.OrdinalIgnoreCase))
            {
                string rest = w["Затронуто абзацев: ".Length..].Trim();
                return rest.Contains("абзац", StringComparison.OrdinalIgnoreCase) ? rest : $"{rest} абзацев";
            }
            if (w.StartsWith("Затронуто: ", StringComparison.OrdinalIgnoreCase))
                return w["Затронуто: ".Length..].Trim();
            if (w.StartsWith("Затронуто элементов: ", StringComparison.OrdinalIgnoreCase))
                return w["Затронуто элементов: ".Length..].Trim();
            if (w.StartsWith("Затронуто заголовков: ", StringComparison.OrdinalIgnoreCase))
                return w["Затронуто заголовков: ".Length..].Trim();
            if (w.StartsWith("Затронуто подписей: ", StringComparison.OrdinalIgnoreCase))
                return w["Затронуто подписей: ".Length..].Trim();
            return w;
        }
    }

    public string DiffText
    {
        get
        {
            string actual = (Issue.Actual ?? string.Empty).Trim();
            string expected = (Issue.Expected ?? string.Empty).Trim();

            if (Issue.Kind == IssueKind.LineSpacing)
            {
                if (actual.Length > 0 && char.IsDigit(actual[^1]))
                    actual += " строки";
                if (expected.Length > 0 && char.IsDigit(expected[^1]))
                    expected += " строки";
            }

            if (actual.Length > 0 && expected.Length > 0)
                return $"{actual} → {expected}";
            if (expected.Length > 0)
                return $"→ {expected}";
            return actual;
        }
    }

    public bool HasDiff => Issue.HasDiff;
    public bool CanFix => Issue.CanFix;
    public bool CannotFix => !Issue.CanFix;

    public string FixAllHint
    {
        get
        {
            if (!CanFix) return string.Empty;
            int count = Issue.AffectedCount ?? (_allItems.Count > 0 ? _allItems.Count : (Items.Count > 0 ? Items.Count : 1));
            if (count <= 0) count = 1;
            return $"Принять: исправит все {count}";
        }
    }

    public string ExpandButtonText => IsExpanded ? "Свернуть ▴" : "Подробнее ▾";

    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
}

/// <summary>
/// Представление строки конкретного элемента замечания в развёрнутой карточке.
/// </summary>
public sealed class CheckItemVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public IssueItem Item { get; }
    public CheckGroupVm Parent { get; }

    public CheckItemVm(IssueItem item, CheckGroupVm parent)
    {
        Item = item ?? throw new ArgumentNullException(nameof(item));
        Parent = parent ?? throw new ArgumentNullException(nameof(parent));
    }

    public int Index => Item.Index;
    public string Text => Item.Text;

    public string DisplayText
    {
        get
        {
            // Для полей страницы строки называются «Раздел N»
            if (IsMarginKind(Parent.Issue.Kind))
            {
                return $"Раздел {Item.Index}";
            }
            if (string.IsNullOrWhiteSpace(Item.Text))
            {
                return $"Раздел {Item.Index}";
            }

            string clean = Item.Text.Trim().Trim('«', '»', '\"');
            return $"«{clean}»";
        }
    }

    public string DiffSubtext
    {
        get
        {
            string act = (Item.Actual ?? string.Empty).Trim();
            string exp = (Item.Expected ?? string.Empty).Trim();
            if (Parent.Issue.Kind == IssueKind.LineSpacing)
            {
                if (act.Length > 0 && char.IsDigit(act[^1])) act += " строки";
                if (exp.Length > 0 && char.IsDigit(exp[^1])) exp += " строки";
            }
            if (act.Length > 0 && exp.Length > 0)
                return $"{act} → {exp}";
            if (exp.Length > 0)
                return $"→ {exp}";
            return act;
        }
    }

    public bool CanFix => Parent.CanFix;

    private void OnPropertyChanged([CallerMemberName] string? propName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));

    private static bool IsMarginKind(IssueKind kind) =>
        kind is IssueKind.MarginLeft or IssueKind.MarginRight or IssueKind.MarginTop or IssueKind.MarginBottom;
}

/// <summary>
/// Чистая статическая логика фильтрации отклонённых замечаний без UI-зависимостей.
/// </summary>
public static class CheckIssueFilter
{
    public static List<CheckIssue> FilterIssues(
        IEnumerable<CheckIssue> issues,
        IReadOnlySet<(IssueKind Kind, int Index, string Text)> rejected)
    {
        if (issues == null) return new List<CheckIssue>();
        if (rejected == null || rejected.Count == 0)
            return issues.ToList();

        var result = new List<CheckIssue>();

        foreach (var issue in issues)
        {
            // Карточка без Items (например, PageNumbers, MissingSection)
            if (issue.Items == null || issue.Items.Count == 0)
            {
                // Ключ для замечаний без Items: (Kind, -1, Title)
                if (rejected.Contains((issue.Kind, -1, issue.Title)))
                {
                    continue; // Скрываем карточку
                }
                result.Add(issue);
                continue;
            }

            // Карточка с Items: фильтруем отдельные строки
            var remainingItems = issue.Items
                .Where(item => !rejected.Contains((issue.Kind, item.Index, item.Text)))
                .ToList();

            // Если у замечания не осталось элементов, оно скрывается
            if (remainingItems.Count == 0)
            {
                continue;
            }

            if (remainingItems.Count == issue.Items.Count)
            {
                result.Add(issue);
            }
            else
            {
                int newAffectedCount = remainingItems.Count;
                string newWhere = FormatRemainingWhere(issue.Where, newAffectedCount);

                result.Add(issue with
                {
                    Items = remainingItems,
                    AffectedCount = newAffectedCount,
                    Where = newWhere
                });
            }
        }

        return result;
    }

    private static string FormatRemainingWhere(string originalWhere, int remainingCount)
    {
        if (string.IsNullOrWhiteSpace(originalWhere))
            return $"Затронуто: {remainingCount}";

        int izIdx = originalWhere.IndexOf("из", StringComparison.OrdinalIgnoreCase);
        if (izIdx >= 0)
        {
            string prefix = originalWhere[..izIdx].TrimEnd();
            int colonIdx = prefix.LastIndexOf(':');
            string pfx = colonIdx >= 0 ? prefix[..(colonIdx + 1)] + " " : "Затронуто: ";
            return $"{pfx}{remainingCount} {originalWhere[izIdx..]}";
        }

        return $"Затронуто: {remainingCount}";
    }
}
