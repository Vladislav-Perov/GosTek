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
using GosTek.Models;

namespace GosTek.Views;

/// <summary>
/// Представление группы замечания (карточки) в списке Проверки.
/// Поддерживает свёрнутый и развёрнутый вид с точечными элементами.
/// </summary>
public sealed class CheckGroupVm : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CheckIssue Issue { get; }
    public ObservableCollection<CheckItemVm> Items { get; }
    public string? MoreText { get; }

    public CheckGroupVm(CheckIssue issue)
    {
        Issue = issue ?? throw new ArgumentNullException(nameof(issue));
        Items = new ObservableCollection<CheckItemVm>();

        if (issue.Items != null)
        {
            foreach (var item in issue.Items.Take(200))
            {
                Items.Add(new CheckItemVm(item, this));
            }

            int totalAffected = issue.AffectedCount ?? issue.Items.Count;
            if (totalAffected > Items.Count)
            {
                int remaining = totalAffected - Items.Count;
                MoreText = $"…ещё {remaining}";
            }
        }
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
    public bool HasMoreText => !string.IsNullOrEmpty(MoreText);

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
            int count = Issue.AffectedCount ?? (Items.Count > 0 ? Items.Count : 1);
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
