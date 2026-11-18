/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для логики фильтрации отклонённых замечаний CheckIssueFilter
 * СОДЕРЖИМОЕ: CheckFilterTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Models, GosTek.Views
 * ==============================================================================
 */
using GosTek.Models;
using GosTek.Views;
using Xunit;

namespace GosTek.Tests;

public class CheckFilterTests
{
    [Fact]
    public void FilterIssues_NullOrEmptyRejected_ReturnsAllIssues()
    {
        var items = new List<IssueItem>
        {
            new(0, "Параграф 1", "Arial", "Times New Roman"),
            new(1, "Параграф 2", "Calibri", "Times New Roman")
        };
        var issue = new CheckIssue(IssueKind.FontName, IssueSeverity.Important,
            "Гарнитура шрифта", "Затронуто абзацев: 2 из 10", true,
            Actual: "Arial", Expected: "Times New Roman", AffectedCount: 2, Items: items);

        var list = new List<CheckIssue> { issue };

        var filteredEmpty = CheckIssueFilter.FilterIssues(list, new HashSet<(IssueKind, int, string)>());
        Assert.Single(filteredEmpty);
        Assert.Equal(2, filteredEmpty[0].Items?.Count);

        var filteredNull = CheckIssueFilter.FilterIssues(list, null!);
        Assert.Single(filteredNull);
    }

    [Fact]
    public void FilterIssues_RejectSingleItem_FiltersOnlyThatItemAndUpdatesCount()
    {
        var items = new List<IssueItem>
        {
            new(0, "Параграф 1", "Arial", "Times New Roman"),
            new(1, "Параграф 2", "Calibri", "Times New Roman")
        };
        var issue = new CheckIssue(IssueKind.FontName, IssueSeverity.Important,
            "Гарнитура шрифта", "Затронуто абзацев: 2 из 10", true,
            Actual: "Arial", Expected: "Times New Roman", AffectedCount: 2, Items: items);

        var rejected = new HashSet<(IssueKind Kind, int Index, string Text)>
        {
            (IssueKind.FontName, 0, "Параграф 1")
        };

        var filtered = CheckIssueFilter.FilterIssues(new[] { issue }, rejected);

        Assert.Single(filtered);
        var remainingIssue = filtered[0];
        Assert.Equal(1, remainingIssue.AffectedCount);
        Assert.NotNull(remainingIssue.Items);
        Assert.Single(remainingIssue.Items);
        Assert.Equal(1, remainingIssue.Items[0].Index);
        Assert.Equal("Параграф 2", remainingIssue.Items[0].Text);
    }

    [Fact]
    public void FilterIssues_RejectAllItemsOfIssue_RemovesIssueEntirely()
    {
        var items = new List<IssueItem>
        {
            new(0, "Параграф 1", "Arial", "Times New Roman"),
            new(1, "Параграф 2", "Calibri", "Times New Roman")
        };
        var issue = new CheckIssue(IssueKind.FontName, IssueSeverity.Important,
            "Гарнитура шрифта", "Затронуто абзацев: 2 из 10", true,
            Actual: "Arial", Expected: "Times New Roman", AffectedCount: 2, Items: items);

        var rejected = new HashSet<(IssueKind Kind, int Index, string Text)>
        {
            (IssueKind.FontName, 0, "Параграф 1"),
            (IssueKind.FontName, 1, "Параграф 2")
        };

        var filtered = CheckIssueFilter.FilterIssues(new[] { issue }, rejected);
        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterIssues_RejectCardWithoutItems_RemovesIssue()
    {
        var issue = new CheckIssue(IssueKind.PageNumbers, IssueSeverity.Warning,
            "Номера страниц", "В колонтитуле", true,
            Actual: "Отсутствуют", Expected: "По центру сверху", AffectedCount: null, Items: null);

        var rejected = new HashSet<(IssueKind Kind, int Index, string Text)>
        {
            (IssueKind.PageNumbers, -1, "Номера страниц")
        };

        var filtered = CheckIssueFilter.FilterIssues(new[] { issue }, rejected);
        Assert.Empty(filtered);
    }

    [Fact]
    public void FilterIssues_UnrejectedIssueWithoutItems_KeptIntact()
    {
        var issue = new CheckIssue(IssueKind.PageNumbers, IssueSeverity.Warning,
            "Номера страниц", "В колонтитуле", true,
            Actual: "Отсутствуют", Expected: "По центру сверху", AffectedCount: null, Items: null);

        var rejected = new HashSet<(IssueKind Kind, int Index, string Text)>
        {
            (IssueKind.FontName, -1, "Другое замечание")
        };

        var filtered = CheckIssueFilter.FilterIssues(new[] { issue }, rejected);
        Assert.Single(filtered);
        Assert.Equal(IssueKind.PageNumbers, filtered[0].Kind);
    }

    [Fact]
    public void CheckGroupVm_DiffText_FormatsLineSpacingWithSuffix()
    {
        var issue = new CheckIssue(IssueKind.LineSpacing, IssueSeverity.Warning,
            "Межстрочный интервал", "Затронуто абзацев: 1 из 1", true,
            Actual: "1.15", Expected: "1.5", AffectedCount: 1, Items: null);

        var vm = new CheckGroupVm(issue);
        Assert.Equal("1.15 строки → 1.5 строки", vm.DiffText);
    }

    [Fact]
    public void CheckItemVm_DisplayText_MarginFormatsAsSection()
    {
        var marginItem = new IssueItem(1, "Раздел 1", "2 см", "3 см");
        var issue = new CheckIssue(IssueKind.MarginLeft, IssueSeverity.Important,
            "Левое поле", "Весь документ", true,
            Actual: "2 см", Expected: "3 см", AffectedCount: 1,
            Items: new List<IssueItem> { marginItem });

        var group = new CheckGroupVm(issue);
        var itemVm = new CheckItemVm(marginItem, group);

        Assert.Equal("Раздел 1", itemVm.DisplayText);
    }

    [Fact]
    public void CheckItemVm_DisplayText_RegularParagraphEnclosedInQuotes()
    {
        var paraItem = new IssueItem(5, "Введение и постановка задачи", "12 pt", "14 pt");
        var issue = new CheckIssue(IssueKind.FontSize, IssueSeverity.Important,
            "Размер шрифта", "Затронуто абзацев: 1 из 10", true,
            Actual: "12 pt", Expected: "14 pt", AffectedCount: 1,
            Items: new List<IssueItem> { paraItem });

        var group = new CheckGroupVm(issue);
        var itemVm = new CheckItemVm(paraItem, group);

        Assert.Equal("«Введение и постановка задачи»", itemVm.DisplayText);
    }
}
