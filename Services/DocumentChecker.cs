/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Движок инспекции и автоматического исправления форматирования docx-документов по правилам ГОСТ
 * СОДЕРЖИМОЕ: DocumentChecker, Context, DocumentInfo
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models.CheckModels
 * ==============================================================================
 */
using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;

// В MAUI есть одноимённый Style
using Style = DocumentFormat.OpenXml.Wordprocessing.Style;      
using FontSize = DocumentFormat.OpenXml.Wordprocessing.FontSize;

namespace GosTek.Services;

/// <summary>Сведения о файле для карточки на экране «Проверка».</summary>
/// <param name="Pages">Число страниц из свойств файла; null, если Word его не записал.</param>
/// <param name="Paragraphs">Число непустых абзацев в тексте.</param>
public sealed record DocumentInfo(int? Pages, int Paragraphs);

/// <summary>
/// Проверка оформления .docx по правилам ГОСТ: поля, шрифт, размер, интервал, нумерация страниц, разделы.
/// Всё читается напрямую из OpenXML.
///
/// Check — только читает файл и возвращает список замечаний.
/// Fix   — правит .docx на месте (копию, которую выбрал вызывающий код) и возвращает число изменений.
///
/// Что считается «основным текстом»: абзацы вне таблиц, не заголовки и не оглавление.
/// Для таблиц и заголовков проверяются только поля страницы, шрифт и нумерация.
/// </summary>
public static class DocumentChecker
{
    private const double TwipsPerCm = 1440 / 2.54;

    // Стили, которые не считаем основным текстом (по id или названию)
    private static readonly string[] StructuralStyleWords =
        { "heading", "заголовок", "title", "toc", "оглавление", "содержание" };

    private static readonly Regex PageField = new(@"\bPAGE\b", RegexOptions.Compiled);

    // =====================================================================
    //  ПРОВЕРКА
    // =====================================================================

    /// <summary>Читает файл и возвращает замечания в порядке: поля, шрифт, размер, интервал, нумерация, разделы.</summary>
    public static List<CheckIssue> Check(string path, RuleSet rules)
    {
        using var ms = CopyToMemory(path);
        using var doc = WordprocessingDocument.Open(ms, false);
        var ctx = new Context(doc);

        var issues = new List<CheckIssue>();
        CheckMargins(ctx, rules, issues);
        CheckFontName(ctx, rules, issues);
        CheckFontSize(ctx, rules, issues);
        CheckSpacing(ctx, rules, issues);
        CheckAlignment(ctx, rules, issues);
        CheckIndent(ctx, rules, issues);
        CheckSpacingBeforeAfter(ctx, rules, issues);
        CheckWidowControl(ctx, rules, issues);
        CheckTableFormatting(ctx, rules, issues);
        CheckFigureFormatting(ctx, rules, issues);
        CheckCodeSnippets(ctx, rules, issues);
        CheckHeadings(ctx, rules, issues);
        CheckPageNumbers(ctx, rules, issues);
        CheckSections(ctx, rules, issues);
        return issues;
    }

    /// <summary>Страницы и абзацы документа. Не бросает исключений: при ошибке вернёт пустые сведения.</summary>
    public static DocumentInfo ReadInfo(string path)
    {
        try {
            using var ms = CopyToMemory(path);
            using var doc = WordprocessingDocument.Open(ms, false);
            var ctx = new Context(doc);

            int? pages = int.TryParse(doc.ExtendedFilePropertiesPart?.Properties?.Pages?.Text, out var n) && n > 0 ? n : null;
            return new DocumentInfo(pages, ctx.CountTextParagraphs());
        } catch (Exception ex) {
            Debug.WriteLine($"DocumentChecker.ReadInfo: {ex}");
            return new DocumentInfo(null, 0);
        }
    }

    private static string? GetSample(Paragraph? p)
    {
        if (p == null) return null;
        var text = p.InnerText?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        return text.Length <= 60 ? text : text.Substring(0, 60);
    }

    private static string FormatItemText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        raw = raw.Trim();
        return raw.Length <= 60 ? raw : raw.Substring(0, 60) + "…";
    }

    private static bool MatchesText(string? actualText, string? expectedSample)
    {
        if (string.IsNullOrWhiteSpace(expectedSample)) return true;
        string cleanExpected = expectedSample.TrimEnd('…').Trim();
        if (cleanExpected.Length == 0) return true;
        string cleanActual = (actualText ?? "").Trim();
        return cleanActual.StartsWith(cleanExpected, StringComparison.Ordinal);
    }

    // =====================================================================
    //  ЕДИНЫЕ ВНУТРЕННИЕ ОБХОДЫ ДЛЯ CHECK И FIXITEMS
    // =====================================================================

    public const int TableCaptionIndexBase = 10000;

    private static List<(int Index, SectionProperties Section, PageMargin Margin)> GetMarginSections(Context ctx)
    {
        var list = new List<(int Index, SectionProperties Section, PageMargin Margin)>();
        int idx = 1;
        foreach (var sec in ctx.Sections) {
            if (sec.GetFirstChild<PageMargin>() is { } margin) {
                list.Add((idx++, sec, margin));
            }
        }
        return list;
    }

    private static List<(int Index, Paragraph Paragraph, string StyleId)> GetAllParagraphEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph, string StyleId)>();
        int idx = 0;
        foreach (var p in ctx.AllParagraphs()) {
            list.Add((idx++, p, ctx.ParagraphStyleId(p) ?? ""));
        }
        return list;
    }

    private static bool IsInTable(Paragraph p) =>
        p.Ancestors<TableCell>().Any() || p.Ancestors<Table>().Any();

    private static bool IsTableCaption(Paragraph p)
    {
        if (IsInTable(p)) return false;
        string text = (p.InnerText ?? "").Trim();
        if (string.IsNullOrEmpty(text)) return false;
        return text.StartsWith("Таблица", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Табл.", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTableCaption(Context ctx, Paragraph p) => IsTableCaption(p);

    private static bool IsHeadingParagraph(Paragraph p)
    {
        int? outlineLvl = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
        string? styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return (outlineLvl != null && outlineLvl < 9) ||
               (styleId != null && (styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase) ||
                                   styleId.Contains("Заголовок", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsHeadingParagraph(Context ctx, Paragraph p) => IsHeadingParagraph(p);

    private static bool IsSpecialParagraph(Paragraph p)
    {
        if (IsInTable(p))
            return true;
        if (IsTableCaption(p))
            return true;
        if (IsFigureParagraph(p))
            return true;
        if (IsFigureCaption(p))
            return true;
        if (IsHeadingParagraph(p))
            return true;
        if (IsCodeSnippetParagraph(p))
            return true;
        return false;
    }

    private static bool IsSpecialParagraph(Context ctx, Paragraph p) => IsSpecialParagraph(p);

    private static List<(int Index, Paragraph Paragraph, string StyleId)> GetBodyParagraphEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph, string StyleId)>();
        int idx = 0;
        foreach (var p in ctx.BodyParagraphs()) {
            if (IsSpecialParagraph(p)) continue;
            list.Add((idx++, p, ctx.ParagraphStyleId(p) ?? ""));
        }
        return list;
    }

    private static List<(int Index, Paragraph Paragraph)> GetTextBodyParagraphEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph)>();
        int idx = 0;
        foreach (var p in ctx.BodyParagraphs()) {
            if (IsSpecialParagraph(p)) continue;
            if (ctx.HasText(p)) {
                list.Add((idx++, p));
            }
        }
        return list;
    }

    private static List<(int Index, Paragraph Paragraph, Table Table, TableCell Cell)> GetTableCellParagraphEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph, Table Table, TableCell Cell)>();
        int cellIdx = 0;
        foreach (var p in ctx.Body.Descendants<Paragraph>()) {
            var cell = p.Ancestors<TableCell>().FirstOrDefault();
            if (cell != null) {
                var table = cell.Ancestors<Table>().FirstOrDefault() ?? p.Ancestors<Table>().FirstOrDefault();
                if (table != null) {
                    list.Add((cellIdx++, p, table, cell));
                }
            }
        }
        return list;
    }

    private static List<(int Index, Paragraph Paragraph)> GetTableCaptionEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph)>();
        int capIdx = TableCaptionIndexBase;
        foreach (var p in ctx.AllParagraphs()) {
            if (IsTableCaption(p)) {
                list.Add((capIdx++, p));
            }
        }
        return list;
    }

    private static bool IsFigureParagraph(Paragraph p) =>
        p.Descendants<Drawing>().Any() || p.Descendants<Picture>().Any();

    private static bool IsFigureCaption(Paragraph p)
    {
        if (IsFigureParagraph(p)) return false;
        string text = p.InnerText.Trim();
        return text.StartsWith("Рисунок", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Рис.", StringComparison.OrdinalIgnoreCase);
    }

    private static List<(int Index, Paragraph Paragraph, bool IsFigure, bool IsCaption)> GetFigureEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph, bool IsFigure, bool IsCaption)>();
        int idx = 0;
        foreach (var p in ctx.AllParagraphs()) {
            bool isFig = IsFigureParagraph(p);
            bool isCap = IsFigureCaption(p);
            if (isFig || isCap) {
                list.Add((idx++, p, isFig, isCap));
            }
        }
        return list;
    }

    private static bool IsCodeSnippetParagraph(Paragraph p, RuleSet? rules = null)
    {
        string? styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        bool isCodeStyle = !string.IsNullOrEmpty(styleId) &&
            (styleId.Contains("code", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("source", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("listing", StringComparison.OrdinalIgnoreCase) ||
             styleId.Contains("листинг", StringComparison.OrdinalIgnoreCase));

        bool isMonoFont = false;
        foreach (var run in p.Descendants<Run>()) {
            string? font = run.RunProperties?.RunFonts?.Ascii?.Value;
            if (!string.IsNullOrEmpty(font) && (font.Equals("Consolas", StringComparison.OrdinalIgnoreCase) ||
                                                font.Equals("Courier New", StringComparison.OrdinalIgnoreCase) ||
                                                font.Equals("Lucida Console", StringComparison.OrdinalIgnoreCase))) {
                isMonoFont = true;
                break;
            }
        }
        return isCodeStyle || isMonoFont;
    }

    private static List<(int Index, Paragraph Paragraph)> GetCodeSnippetEntries(Context ctx, RuleSet rules)
    {
        var list = new List<(int Index, Paragraph Paragraph)>();
        int idx = 0;
        foreach (var p in ctx.AllParagraphs()) {
            if (!ctx.HasText(p)) continue;
            if (IsCodeSnippetParagraph(p, rules)) {
                list.Add((idx++, p));
            }
        }
        return list;
    }

    private static List<(int Index, Paragraph Paragraph)> GetHeadingEntries(Context ctx)
    {
        var list = new List<(int Index, Paragraph Paragraph)>();
        int idx = 0;
        foreach (var p in ctx.AllParagraphs()) {
            if (!ctx.HasText(p)) continue;
            if (IsHeadingParagraph(p)) {
                list.Add((idx++, p));
            }
        }
        return list;
    }

    // ----- Поля страницы -----
    private static void CheckMargins(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var marginSections = GetMarginSections(ctx);
        if (marginSections.Count == 0)
            return;

        var sides = new (IssueKind Kind, string Name, MarginRule Rule, Func<PageMargin, double?> Read)[] {
            (IssueKind.MarginLeft,   "Левое",   rules.Left,   m => m.Left?.Value),
            (IssueKind.MarginRight,  "Правое",  rules.Right,  m => m.Right?.Value),
            (IssueKind.MarginTop,    "Верхнее", rules.Top,    m => m.Top?.Value),
            (IssueKind.MarginBottom, "Нижнее",  rules.Bottom, m => m.Bottom?.Value),
        };

        foreach (var (kind, name, rule, read) in sides) {
            var bad = new List<(int Number, double Cm)>();
            var items = new List<IssueItem>();

            foreach (var (index, sec, margin) in marginSections) {
                if (read(margin) is { } twips && !MarginOk(twips / TwipsPerCm, rule, rules.MarginToleranceCm)) {
                    double cm = Math.Round(twips / TwipsPerCm, 2);
                    bad.Add((index, cm));
                    if (items.Count < 200) {
                        items.Add(new IssueItem(index, $"Раздел {index}", $"{RuFormat.Number(cm)} см", $"{RuFormat.Number(rule.Cm)} см"));
                    }
                }
            }

            if (bad.Count == 0)
                continue;

            var need = rule.Exact
                ? $"по правилам нужно {RuFormat.Number(rule.Cm)} см"
                : $"нужно не меньше {RuFormat.Number(rule.Cm)} см";
            var where = bad.Count == marginSections.Count
                ? "Весь документ"
                : "Разделы: " + string.Join(", ", bad.Select(b => b.Number));

            issues.Add(new CheckIssue(kind, IssueSeverity.Important,
                $"{name} поле", where, CanFix: true,
                Actual: $"{RuFormat.Number(bad[0].Cm)} см",
                Expected: $"{RuFormat.Number(rule.Cm)} см",
                Page: null,
                Sample: null,
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    private static bool MarginOk(double actualCm, MarginRule rule, double tolerance)
        => rule.Exact
            ? Math.Abs(actualCm - rule.Cm) <= tolerance
            : actualCm >= rule.Cm - tolerance;

    // ----- Шрифт (гарнитура) -----
    private static void CheckFontName(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);
        var badParagraphs = new HashSet<Paragraph>();
        var allEntries = GetAllParagraphEntries(ctx);
        var items = new List<IssueItem>();
        Paragraph? firstBad = null;
        int allCount = allEntries.Count;

        foreach (var entry in allEntries) {
            var p = entry.Paragraph;
            if (IsCodeSnippetParagraph(p, rules))
                continue;

            var styleId = entry.StyleId;
            bool pBad = false;
            string? worstFontInP = null;
            int maxCharsInP = -1;

            foreach (var run in ctx.TextRuns(p)) {
                var font = ctx.FontOf(run, styleId);
                if (SameFont(font, rules.FontName))
                    continue;

                int chars = ctx.CharCount(run);
                bad[font] = bad.GetValueOrDefault(font) + chars;
                pBad = true;
                if (chars > maxCharsInP) {
                    maxCharsInP = chars;
                    worstFontInP = font;
                }
            }

            if (pBad) {
                badParagraphs.Add(p);
                firstBad ??= p;
                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), worstFontInP ?? "Неизвестный", rules.FontName));
                }
            }
        }

        if (badParagraphs.Count == 0)
            return;

        var worst = bad.OrderByDescending(kv => kv.Value).First().Key;
        issues.Add(new CheckIssue(IssueKind.FontName, IssueSeverity.Important,
            "Гарнитура шрифта",
            $"Затронуто абзацев: {badParagraphs.Count} из {allCount}",
            CanFix: true,
            Actual: worst,
            Expected: rules.FontName,
            Page: null,
            Sample: GetSample(firstBad),
            AffectedCount: badParagraphs.Count,
            Items: items.Count > 0 ? items : null));
    }

    private static bool SameFont(string actual, string required)
        => string.Equals(actual.Trim(), required.Trim(), StringComparison.CurrentCultureIgnoreCase);

    // ----- Размер шрифта -----
    private static void CheckFontSize(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new Dictionary<double, int>();
        var badParagraphs = new HashSet<Paragraph>();
        var bodyEntries = GetBodyParagraphEntries(ctx);
        var items = new List<IssueItem>();
        Paragraph? firstBad = null;
        int allCount = bodyEntries.Count;

        foreach (var entry in bodyEntries) {
            var p = entry.Paragraph;
            var styleId = entry.StyleId;
            bool pBad = false;
            double? worstSizeInP = null;
            int maxCharsInP = -1;

            foreach (var run in ctx.TextRuns(p)) {
                var size = ctx.SizeOf(run, styleId);
                if (Math.Abs(size - rules.FontSizePt) < 0.01)
                    continue;

                int chars = ctx.CharCount(run);
                bad[size] = bad.GetValueOrDefault(size) + chars;
                pBad = true;
                if (chars > maxCharsInP) {
                    maxCharsInP = chars;
                    worstSizeInP = size;
                }
            }

            if (pBad) {
                badParagraphs.Add(p);
                firstBad ??= p;
                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), $"{RuFormat.Number(worstSizeInP ?? 0)} pt", $"{RuFormat.Number(rules.FontSizePt)} pt"));
                }
            }
        }

        if (badParagraphs.Count == 0)
            return;

        var worst = bad.OrderByDescending(kv => kv.Value).First().Key;
        issues.Add(new CheckIssue(IssueKind.FontSize, IssueSeverity.Important,
            "Размер шрифта",
            $"Затронуто абзацев: {badParagraphs.Count} из {allCount}",
            CanFix: true,
            Actual: $"{RuFormat.Number(worst)} pt",
            Expected: $"{RuFormat.Number(rules.FontSizePt)} pt",
            Page: null,
            Sample: GetSample(firstBad),
            AffectedCount: badParagraphs.Count,
            Items: items.Count > 0 ? items : null));
    }

    // ----- Межстрочный интервал -----
    private static void CheckSpacing(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var textBody = GetTextBodyParagraphEntries(ctx);
        var badParagraphs = new List<Paragraph>();
        var badActuals = new Dictionary<string, int>();
        var items = new List<IssueItem>();
        int badCount = 0;
        int total = textBody.Count;

        foreach (var entry in textBody) {
            var p = entry.Paragraph;
            var spacing = ctx.SpacingOf(p);
            if (SpacingOk(spacing, rules))
                continue;

            badCount++;
            badParagraphs.Add(p);

            string actualStr = spacing.IsAuto
                ? $"{RuFormat.Number(spacing.Multiple)}"
                : $"{RuFormat.Number(spacing.Points)} pt";
            badActuals[actualStr] = badActuals.GetValueOrDefault(actualStr) + 1;

            if (items.Count < 200) {
                items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), actualStr, $"{RuFormat.Number(rules.LineSpacing)}"));
            }
        }

        if (badCount == 0)
            return;

        var worstActual = badActuals.OrderByDescending(kv => kv.Value).First().Key;
        var firstBad = badParagraphs.FirstOrDefault();
        issues.Add(new CheckIssue(IssueKind.LineSpacing, IssueSeverity.Warning,
            "Межстрочный интервал",
            $"Затронуто абзацев: {badCount} из {total}",
            CanFix: true,
            Actual: worstActual,
            Expected: $"{RuFormat.Number(rules.LineSpacing)}",
            Page: null,
            Sample: GetSample(firstBad),
            AffectedCount: badCount,
            Items: items.Count > 0 ? items : null));
    }

    private static bool SpacingOk(LineSpacing s, RuleSet rules)
        => s.IsAuto && Math.Abs(s.Multiple - rules.LineSpacing) <= rules.SpacingTolerance;

    private static string DescribeSpacing(LineSpacing s, RuleSet rules)
    {
        var need = RuFormat.Number(rules.LineSpacing);
        if (s.IsAuto)
            return $"Межстрочный интервал {RuFormat.Number(s.Multiple)}, нужен {need}";

        var kind = s.IsExact ? "точно" : "не меньше";
        return $"Межстрочный интервал {kind} {RuFormat.Number(s.Points)} pt, нужен {need}";
    }

    // ----- Нумерация страниц -----
    private static void CheckPageNumbers(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        if (!rules.RequirePageNumbers || ctx.HasPageNumbers())
            return;

        issues.Add(new CheckIssue(IssueKind.PageNumbers, IssueSeverity.Warning,
            "Нумерация страниц", "Колонтитул", CanFix: true,
            Actual: "Отсутствует",
            Expected: "Внизу по центру",
            Items: null));
    }

    // ----- Обязательные разделы -----
    private static void CheckSections(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        if (rules.RequiredSections.Count == 0 || ctx.CountTextParagraphs() < rules.SectionsMinParagraphs)
            return;

        var titles = ctx.AllParagraphs()
            .Select(p => NormalizeTitle(p.InnerText))
            .Where(t => t.Length is > 0 and < 120)
            .ToList();

        foreach (var section in rules.RequiredSections) {
            var rx = new Regex(section.Pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (titles.Any(title => rx.IsMatch(title)))
                continue;

            issues.Add(new CheckIssue(IssueKind.MissingSection, IssueSeverity.Important,
                $"Не найден раздел «{section.Title}»", "Конец документа", CanFix: false,
                Items: null));
        }
    }

    private static string NormalizeTitle(string text)
        => Regex.Replace(text.Replace('ё', 'е').Replace('Ё', 'Е'), @"\s+", " ").Trim();

    // ----- Выравнивание -----
    private static void CheckAlignment(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(rules.Alignment) || !rules.Alignment.Contains("Justify", StringComparison.OrdinalIgnoreCase)) 
            return;

        var textBody = GetTextBodyParagraphEntries(ctx);
        var badParagraphs = new HashSet<Paragraph>();
        var badAlignments = new Dictionary<string, int>();
        var items = new List<IssueItem>();
        int total = textBody.Count;

        foreach (var entry in textBody) {
            var p = entry.Paragraph;
            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != JustificationValues.Both) {
                badParagraphs.Add(p);
                string alignStr = (jc == JustificationValues.Center) ? "По центру"
                    : (jc == JustificationValues.Right) ? "По правому краю"
                    : "По левому краю";
                badAlignments[alignStr] = badAlignments.GetValueOrDefault(alignStr) + 1;

                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), alignStr, "По ширине"));
                }
            }
        }

        if (badParagraphs.Count == 0) return;
        var worstAlign = badAlignments.OrderByDescending(kv => kv.Value).First().Key;
        var firstBad = badParagraphs.FirstOrDefault();
        issues.Add(new CheckIssue(IssueKind.Alignment, IssueSeverity.Important,
            "Выравнивание текста",
            $"Затронуто абзацев: {badParagraphs.Count} из {total}",
            CanFix: true,
            Actual: worstAlign,
            Expected: "По ширине",
            Page: null,
            Sample: GetSample(firstBad),
            AffectedCount: badParagraphs.Count,
            Items: items.Count > 0 ? items : null));
    }

    // ----- Абзацный отступ (Красная строка) -----
    private static void CheckIndent(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var textBody = GetTextBodyParagraphEntries(ctx);
        var badParagraphs = new HashSet<Paragraph>();
        var badIndents = new Dictionary<double, int>();
        var items = new List<IssueItem>();
        double targetTwips = rules.FirstLineIndentCm * TwipsPerCm;
        double toleranceTwips = 0.1 * TwipsPerCm; // Допуск 1 мм
        int total = textBody.Count;

        foreach (var entry in textBody) {
            var p = entry.Paragraph;
            double actualTwips = 0;
            var ind = p.ParagraphProperties?.Indentation;
            if (ind?.FirstLine?.Value != null && double.TryParse(ind.FirstLine.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fl)) {
                actualTwips = fl;
            }

            if (Math.Abs(actualTwips - targetTwips) > toleranceTwips) {
                badParagraphs.Add(p);
                double actualCm = Math.Round(actualTwips / TwipsPerCm, 2);
                badIndents[actualCm] = badIndents.GetValueOrDefault(actualCm) + 1;
                string actualStr = actualCm <= 0.05 ? "0 см (нет отступа)" : $"{RuFormat.Number(actualCm)} см";

                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), actualStr, $"{RuFormat.Number(rules.FirstLineIndentCm)} см"));
                }
            }
        }

        if (badParagraphs.Count == 0) return;
        var worstIndentCm = badIndents.OrderByDescending(kv => kv.Value).First().Key;
        string actualIndentStr = worstIndentCm == 0 ? "0 см (нет отступа)" : $"{RuFormat.Number(worstIndentCm)} см";
        var firstBad = badParagraphs.FirstOrDefault();

        issues.Add(new CheckIssue(IssueKind.FirstLineIndent, IssueSeverity.Important,
            "Абзацный отступ",
            $"Затронуто абзацев: {badParagraphs.Count} из {total}",
            CanFix: true,
            Actual: actualIndentStr,
            Expected: $"{RuFormat.Number(rules.FirstLineIndentCm)} см",
            Page: null,
            Sample: GetSample(firstBad),
            AffectedCount: badParagraphs.Count,
            Items: items.Count > 0 ? items : null));
    }

    // ----- Интервалы До и После абзаца -----
    private static void CheckSpacingBeforeAfter(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var textBody = GetTextBodyParagraphEntries(ctx);
        var bad = new HashSet<Paragraph>();
        var badSpacings = new Dictionary<string, int>();
        var items = new List<IssueItem>();
        int total = textBody.Count;

        foreach (var entry in textBody) {
            var p = entry.Paragraph;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing != null && ((spacing.Before != null && spacing.Before.Value != "0") || (spacing.After != null && spacing.After.Value != "0"))) {
                bad.Add(p);
                double beforePt = (spacing.Before?.Value != null && double.TryParse(spacing.Before.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var bVal)) ? bVal / 20.0 : 0;
                double afterPt = (spacing.After?.Value != null && double.TryParse(spacing.After.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var aVal)) ? aVal / 20.0 : 0;
                string spStr = (beforePt > 0 && afterPt > 0)
                    ? $"{beforePt:0.#} / {afterPt:0.#} pt"
                    : (afterPt > 0 ? $"{afterPt:0.#} pt после" : $"{beforePt:0.#} pt до");
                badSpacings[spStr] = badSpacings.GetValueOrDefault(spStr) + 1;

                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), spStr, "0 pt"));
                }
            }
        }
        if (bad.Count > 0) {
            var worstSp = badSpacings.OrderByDescending(kv => kv.Value).FirstOrDefault().Key ?? "Лишние отступы";
            var firstBad = bad.FirstOrDefault();
            issues.Add(new CheckIssue(IssueKind.SpacingBeforeAfter, IssueSeverity.Warning, 
                "Отступы до/после абзаца",
                $"Затронуто абзацев: {bad.Count} из {total}",
                true,
                Actual: worstSp,
                Expected: "0 pt",
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    // ----- Запрет висячих строк -----
    private static void CheckWidowControl(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var textBody = GetTextBodyParagraphEntries(ctx);
        var bad = new HashSet<Paragraph>();
        var items = new List<IssueItem>();
        int total = textBody.Count;

        foreach (var entry in textBody) {
            var p = entry.Paragraph;
            if (p.ParagraphProperties?.WidowControl?.Val?.Value == false) {
                bad.Add(p);
                if (items.Count < 200) {
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), "Отключен", "Включен"));
                }
            }
        }
        if (bad.Count > 0) {
            var firstBad = bad.FirstOrDefault();
            issues.Add(new CheckIssue(IssueKind.WidowControl, IssueSeverity.Warning, 
                "Запрет висячих строк",
                $"Затронуто абзацев: {bad.Count} из {total}",
                true,
                Actual: "Отключен",
                Expected: "Включен",
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    // ----- Форматирование таблиц (Динамическое по ГОСТ) -----
    private static void CheckTableFormatting(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        if (!rules.CheckTables) return;
        var cellEntries = GetTableCellParagraphEntries(ctx);
        var capEntries = GetTableCaptionEntries(ctx);

        int badCells = 0;
        int badCaptions = 0;
        var badCellFonts = new Dictionary<string, int>();
        var badTableParagraphs = new List<Paragraph>();
        var items = new List<IssueItem>();
        string tableLineSpacingStr = Math.Round(rules.TableLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in cellEntries) {
            var p = entry.Paragraph;
            if (!ctx.HasText(p)) continue;
            
            bool needsFix = false;
            var styleId = ctx.ParagraphStyleId(p);
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            string? curFont = null;
            double? curSize = null;
            if (run != null) {
                curSize = ctx.SizeOf(run, styleId);
                curFont = ctx.FontOf(run, styleId);
                if (Math.Abs(curSize.Value - rules.TableFontSizePt) > 0.01) needsFix = true;
                if (!string.IsNullOrEmpty(rules.TableFontFamily) && !curFont.Contains(rules.TableFontFamily)) needsFix = true;
                if (needsFix) {
                    string key = $"{curFont}, {curSize:0.#} pt";
                    badCellFonts[key] = badCellFonts.GetValueOrDefault(key) + 1;
                }
            }
            
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != tableLineSpacingStr) needsFix = true;
            
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableRequireNoIndent && indent != null && indent != "0") needsFix = true;
            
            if (needsFix) {
                badCells++;
                badTableParagraphs.Add(p);
                if (items.Count < 200) {
                    string cellActual = (curFont != null && curSize != null) ? $"{curFont}, {curSize:0.#} pt" : "Не по ГОСТ";
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), cellActual, $"{rules.TableFontFamily}, {rules.TableFontSizePt:0.#} pt"));
                }
            }
        }

        string capLineSpacingStr = Math.Round(rules.TableCaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string capBeforeStr = Math.Round(rules.TableCaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capAfterStr = Math.Round(rules.TableCaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        var expectedJc = rules.TableCaptionAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in capEntries) {
            var p = entry.Paragraph;
            bool needsFix = false;
            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != expectedJc) needsFix = true;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableCaptionRequireNoIndent && indent != null && indent != "0") needsFix = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != capLineSpacingStr) needsFix = true;
            if (rules.TableCaptionSpacingBeforePt > 0 && spacing?.Before?.Value != capBeforeStr) needsFix = true;
            if (rules.TableCaptionSpacingAfterPt > 0 && spacing?.After?.Value != capAfterStr) needsFix = true;

            if (needsFix) {
                badCaptions++;
                badTableParagraphs.Add(p);
                if (items.Count < 200) {
                    string alignName = (jc == JustificationValues.Center) ? "По центру" : (jc == JustificationValues.Right ? "По правому краю" : "По левому краю");
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), $"Выравнивание: {alignName}", rules.TableCaptionAlignment));
                }
            }
        }

        if (badCells > 0 || badCaptions > 0) {
            string actual = badCellFonts.Count > 0 
                ? badCellFonts.OrderByDescending(kv => kv.Value).First().Key 
                : "Не по ГОСТ";
            var firstBad = badTableParagraphs.FirstOrDefault();
            string whereDesc = (badCaptions > 0 && badCells > 0)
                ? $"Затронуто элементов: {badCells + badCaptions}"
                : (badCaptions > 0 ? $"Затронуто подписей: {badCaptions}" : $"Затронуто ячеек: {badCells}");
            issues.Add(new CheckIssue(IssueKind.TableFormatting, IssueSeverity.Important, 
                "Форматирование таблиц",
                whereDesc,
                true,
                Actual: actual,
                Expected: $"{rules.TableFontFamily}, {rules.TableFontSizePt:0.#} pt",
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: badCells + badCaptions,
                Items: items.Count > 0 ? items : null));
        }
    }

    // ----- Форматирование рисунков и подписей (Динамическое) -----
    private static void CheckFigureFormatting(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var entries = GetFigureEntries(ctx);
        var bad = new HashSet<Paragraph>();
        var items = new List<IssueItem>();
        int total = entries.Count;
        
        string figSpacingBeforeStr = Math.Round(rules.FigureSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string figSpacingAfterStr = Math.Round(rules.FigureSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingBeforeStr = Math.Round(rules.CaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingAfterStr = Math.Round(rules.CaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capLineSpacingStr = Math.Round(rules.CaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        string? worstFigAlign = null;

        foreach (var entry in entries) {
            var p = entry.Paragraph;
            bool needsFix = false;

            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;

            if (entry.IsFigure) {
                if (rules.FigureAlignment == "Center" && jc != JustificationValues.Center) {
                    needsFix = true;
                    worstFigAlign = jc == JustificationValues.Both ? "По ширине" : "По левому краю";
                }
                if (rules.FigureRequireNoIndent && indent != null && indent != "0") needsFix = true;
                if (spacing?.Before?.Value != figSpacingBeforeStr || spacing?.After?.Value != figSpacingAfterStr) needsFix = true;

                if (needsFix) {
                    bad.Add(p);
                    if (items.Count < 200) {
                        string text = p.InnerText.Trim();
                        items.Add(new IssueItem(entry.Index, FormatItemText(text.Length > 0 ? text : "[Рисунок]"), worstFigAlign ?? "Не по стандарту", rules.FigureAlignment));
                    }
                }
            } 
            else if (entry.IsCaption) {
                if (rules.CaptionAlignment == "Center" && jc != JustificationValues.Center) {
                    needsFix = true;
                    worstFigAlign ??= jc == JustificationValues.Both ? "По ширине" : "По левому краю";
                }
                if (rules.CaptionRequireNoIndent && indent != null && indent != "0") needsFix = true;
                if (spacing?.Line?.Value != capLineSpacingStr) needsFix = true;
                if (rules.CaptionSpacingBeforePt > 0 && spacing?.Before?.Value != capSpacingBeforeStr) needsFix = true;
                if (rules.CaptionSpacingAfterPt > 0 && spacing?.After?.Value != capSpacingAfterStr) needsFix = true;

                if (needsFix) {
                    bad.Add(p);
                    if (items.Count < 200) {
                        items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), worstFigAlign ?? "Не по стандарту", rules.CaptionAlignment));
                    }
                }
            }
        }

        if (bad.Count > 0) {
            string actual = worstFigAlign ?? "Не по стандарту";
            string expected = rules.FigureAlignment == "Center" ? "По центру, без отступа" : $"{rules.FigureAlignment}, без отступа";
            var firstBad = bad.FirstOrDefault();
            issues.Add(new CheckIssue(IssueKind.FigureFormatting, IssueSeverity.Important,
                "Форматирование рисунков",
                $"Затронуто элементов: {bad.Count} из {total}",
                true,
                Actual: actual,
                Expected: expected,
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    // ----- Листинги кода (Динамическое) -----
    private static void CheckCodeSnippets(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var entries = GetCodeSnippetEntries(ctx, rules);
        var bad = new HashSet<Paragraph>();
        var items = new List<IssueItem>();
        int total = entries.Count;
        string codeLineSpacingStr = Math.Round(rules.CodeLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        var badFonts = new Dictionary<string, int>();

        foreach (var entry in entries) {
            var p = entry.Paragraph;
            string? styleId = ctx.ParagraphStyleId(p);
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
            bool needsFix = false;
            string? curFont = null;
            double? curSize = null;

            if (run != null) {
                curSize = ctx.SizeOf(run, styleId);
                curFont = ctx.FontOf(run, styleId);
                if (Math.Abs(curSize.Value - rules.CodeFontSizePt) > 0.01) needsFix = true;
                if (curFont != rules.CodeFontFamily && !curFont.Contains(rules.CodeFontFamily)) needsFix = true;
                if (needsFix) {
                    string key = $"{curFont}, {curSize:0.#} pt";
                    badFonts[key] = badFonts.GetValueOrDefault(key) + 1;
                }
            }
            
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.CodeRequireNoIndent && indent != null && indent != "0") needsFix = true;
            
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != codeLineSpacingStr) needsFix = true;

            if (needsFix) {
                bad.Add(p);
                if (items.Count < 200) {
                    string actual = (curFont != null && curSize != null) ? $"{curFont}, {curSize:0.#} pt" : "Обычный шрифт";
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), actual, $"{rules.CodeFontFamily}, {rules.CodeFontSizePt:0.#} pt"));
                }
            }
        }

        if (bad.Count > 0) {
            string actual = badFonts.Count > 0 ? badFonts.OrderByDescending(kv => kv.Value).First().Key : "Обычный шрифт";
            var firstBad = bad.FirstOrDefault();
            issues.Add(new CheckIssue(IssueKind.CodeSnippetFormatting, IssueSeverity.Important,
                "Листинг кода",
                $"Затронуто абзацев: {bad.Count} из {total}",
                true,
                Actual: actual,
                Expected: $"{rules.CodeFontFamily}, {rules.CodeFontSizePt:0.#} pt",
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    // ----- Заголовки (Динамическое) -----
    private static void CheckHeadings(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var entries = GetHeadingEntries(ctx);
        var bad = new HashSet<Paragraph>();
        var items = new List<IssueItem>();
        int total = entries.Count;
        var badHeadings = new Dictionary<string, int>();

        string headLineSpacingStr = Math.Round(rules.HeadingLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string headBeforeStr = Math.Round(rules.HeadingSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string headAfterStr = Math.Round(rules.HeadingSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        var expectedJc = rules.HeadingAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in entries) {
            var p = entry.Paragraph;
            string? styleId = ctx.ParagraphStyleId(p);
            bool needsFix = false;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.HeadingRequireNoIndent && indent != null && indent != "0") needsFix = true;
            
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            string? curFont = null;
            double? curSize = null;
            if (run != null) {
                curFont = ctx.FontOf(run, styleId);
                curSize = ctx.SizeOf(run, styleId);
                if (rules.HeadingRequireBold) {
                    bool isBold = run?.RunProperties?.Bold != null && (run.RunProperties.Bold.Val == null || run.RunProperties.Bold.Val.Value);
                    if (!isBold) needsFix = true;
                }
                if (!string.IsNullOrEmpty(rules.HeadingFontFamily) && !curFont.Contains(rules.HeadingFontFamily)) needsFix = true;
                if (rules.HeadingFontSizePt > 0 && Math.Abs(curSize.Value - rules.HeadingFontSizePt) > 0.01) needsFix = true;
                if (needsFix) {
                    string key = $"{curFont}, {curSize:0.#} pt";
                    badHeadings[key] = badHeadings.GetValueOrDefault(key) + 1;
                }
            }

            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != expectedJc) needsFix = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != headLineSpacingStr) needsFix = true;
            if (rules.HeadingSpacingBeforePt > 0 && spacing?.Before?.Value != headBeforeStr) needsFix = true;
            if (rules.HeadingSpacingAfterPt > 0 && spacing?.After?.Value != headAfterStr) needsFix = true;

            string text = p.InnerText.TrimEnd();
            if (rules.HeadingRemoveTrailingDot && text.EndsWith(".")) needsFix = true;

            if (needsFix) {
                bad.Add(p);
                if (items.Count < 200) {
                    string actual = (curFont != null && curSize != null) ? $"{curFont}, {curSize:0.#} pt" : "Не по стандарту";
                    items.Add(new IssueItem(entry.Index, FormatItemText(p.InnerText), actual, $"{rules.HeadingFontFamily}, {rules.HeadingFontSizePt:0.#} pt"));
                }
            }
        }

        if (bad.Count > 0) {
            string actual = badHeadings.Count > 0 ? badHeadings.OrderByDescending(kv => kv.Value).First().Key : "Не по стандарту";
            var firstBad = bad.FirstOrDefault();
            issues.Add(new CheckIssue(IssueKind.HeadingFormatting, IssueSeverity.Important,
                "Заголовки разделов",
                $"Затронуто заголовков: {bad.Count} из {total}",
                true,
                Actual: actual,
                Expected: $"{rules.HeadingFontFamily}, {rules.HeadingFontSizePt:0.#} pt",
                Page: null,
                Sample: GetSample(firstBad),
                AffectedCount: bad.Count,
                Items: items.Count > 0 ? items : null));
        }
    }

    // =====================================================================
    //  ИСПРАВЛЕНИЕ
    // =====================================================================

    /// <summary>
    /// Правит файл на месте. kinds = null: исправить всё, что умеем.
    /// Раздел «Список литературы» автоматически не добавляется (это работа автора), поэтому
    /// MissingSection игнорируется. Возвращает число изменённых элементов.
    /// </summary>
    public static int Fix(string path, RuleSet rules, IEnumerable<IssueKind>? kinds = null)
    {
        var wanted = kinds is null ? null : new HashSet<IssueKind>(kinds);
        bool Want(IssueKind k) => wanted is null || wanted.Contains(k);

        int changes = 0;

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            ValidateZipArchive(fs);

        using (var doc = WordprocessingDocument.Open(path, true)) {
            var ctx = new Context(doc);

            if (Want(IssueKind.MarginLeft))   changes += FixMargin(ctx, rules.Left, rules, m => m.Left?.Value, (m, tw) => m.Left = (uint)tw);
            if (Want(IssueKind.MarginRight))  changes += FixMargin(ctx, rules.Right, rules, m => m.Right?.Value, (m, tw) => m.Right = (uint)tw);
            if (Want(IssueKind.MarginTop))    changes += FixMargin(ctx, rules.Top, rules, m => m.Top?.Value, (m, tw) => m.Top = tw);
            if (Want(IssueKind.MarginBottom)) changes += FixMargin(ctx, rules.Bottom, rules, m => m.Bottom?.Value, (m, tw) => m.Bottom = tw);

            if (Want(IssueKind.FontName))     changes += FixFontName(ctx, rules);
            if (Want(IssueKind.FontSize))     changes += FixFontSize(ctx, rules);
            if (Want(IssueKind.LineSpacing))  changes += FixSpacing(ctx, rules);
            if (Want(IssueKind.Alignment))    changes += FixAlignment(ctx, rules);
            if (Want(IssueKind.FirstLineIndent)) changes += FixIndent(ctx, rules);
            if (Want(IssueKind.SpacingBeforeAfter)) changes += FixSpacingBeforeAfter(ctx, rules);
            if (Want(IssueKind.WidowControl)) changes += FixWidowControl(ctx, rules);
            if (Want(IssueKind.TableFormatting)) changes += FixTableFormatting(ctx, rules);
            if (Want(IssueKind.FigureFormatting)) changes += FixFigureFormatting(ctx, rules);
            if (Want(IssueKind.CodeSnippetFormatting)) changes += FixCodeSnippets(ctx, rules);
            if (Want(IssueKind.HeadingFormatting)) changes += FixHeadings(ctx, rules);
            if (Want(IssueKind.PageNumbers))  changes += FixPageNumbers(ctx, rules);

            if (changes > 0) {
                ctx.Main.Document?.Save();
                foreach (var f in ctx.Main.FooterParts)
                    f.Footer?.Save();
            }
        }

        return changes;
    }


    // =====================================================================
    //  ТОЧЕЧНОЕ ИСПРАВЛЕНИЕ (FixItems)
    // =====================================================================

    /// <summary>
    /// Точечное исправление только выбранных элементов замечания.
    /// Меняет только элементы с указанными номерами, остальное в файле остаётся неизменным.
    /// Перед правкой сверяет Text элемента с абзацем по индексу и пропускает несовпавшие, возвращая количество реально изменённых.
    /// </summary>
    public static int FixItems(
        string path,
        RuleSet rules,
        IssueKind kind,
        IReadOnlySet<int> indices,
        IReadOnlyDictionary<int, string>? expectedTexts = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(indices);

        if (kind == IssueKind.PageNumbers || kind == IssueKind.MissingSection) {
            throw new NotSupportedException($"Точечное исправление элементов для замечания {kind} не поддерживается, так как замечание относится ко всему документу.");
        }

        if (indices.Count == 0 || !File.Exists(path))
            return 0;

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            ValidateZipArchive(fs);

        int changes = 0;
        using (var doc = WordprocessingDocument.Open(path, true)) {
            var ctx = new Context(doc);
            changes = kind switch {
                IssueKind.MarginLeft => FixItemMargins(ctx, rules, rules.Left, m => m.Left?.Value, (m, tw) => m.Left = (uint)tw, indices),
                IssueKind.MarginRight => FixItemMargins(ctx, rules, rules.Right, m => m.Right?.Value, (m, tw) => m.Right = (uint)tw, indices),
                IssueKind.MarginTop => FixItemMargins(ctx, rules, rules.Top, m => m.Top?.Value, (m, tw) => m.Top = tw, indices),
                IssueKind.MarginBottom => FixItemMargins(ctx, rules, rules.Bottom, m => m.Bottom?.Value, (m, tw) => m.Bottom = tw, indices),

                IssueKind.FontName => FixItemFontName(ctx, rules, indices, expectedTexts),
                IssueKind.FontSize => FixItemFontSize(ctx, rules, indices, expectedTexts),
                IssueKind.LineSpacing => FixItemLineSpacing(ctx, rules, indices, expectedTexts),
                IssueKind.Alignment => FixItemAlignment(ctx, rules, indices, expectedTexts),
                IssueKind.FirstLineIndent => FixItemIndent(ctx, rules, indices, expectedTexts),
                IssueKind.SpacingBeforeAfter => FixItemSpacingBeforeAfter(ctx, rules, indices, expectedTexts),
                IssueKind.WidowControl => FixItemWidowControl(ctx, rules, indices, expectedTexts),
                IssueKind.TableFormatting => FixItemTableFormatting(ctx, rules, indices, expectedTexts),
                IssueKind.FigureFormatting => FixItemFigureFormatting(ctx, rules, indices, expectedTexts),
                IssueKind.CodeSnippetFormatting => FixItemCodeSnippets(ctx, rules, indices, expectedTexts),
                IssueKind.HeadingFormatting => FixItemHeadings(ctx, rules, indices, expectedTexts),

                _ => throw new NotSupportedException($"Точечное исправление для {kind} не поддерживается.")
            };

            if (changes > 0) {
                ctx.Main.Document?.Save();
            }
        }

        return changes;
    }

    /// <summary>
    /// Точечное исправление элементов по переданной коллекции IssueItem с автоматической сверкой текста.
    /// </summary>
    public static int FixItems(string path, RuleSet rules, IssueKind kind, IReadOnlyCollection<IssueItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var indices = items.Select(i => i.Index).ToHashSet();
        var texts = items.ToDictionary(i => i.Index, i => i.Text);
        return FixItems(path, rules, kind, indices, texts);
    }

    private static int FixItemMargins(Context ctx, RuleSet rules, MarginRule rule, Func<PageMargin, double?> read, Action<PageMargin, int> write, IReadOnlySet<int> indices)
    {
        int count = 0;
        int target = (int)Math.Round(rule.Cm * TwipsPerCm);

        foreach (var (index, sec, margin) in GetMarginSections(ctx)) {
            if (!indices.Contains(index))
                continue;

            if (read(margin) is not { } twips)
                continue;

            if (!MarginOk(twips / TwipsPerCm, rule, rules.MarginToleranceCm)) {
                write(margin, target);
                count++;
            }
        }

        return count;
    }

    private static int FixItemFontName(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;

        foreach (var entry in GetAllParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (IsCodeSnippetParagraph(entry.Paragraph, rules))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            bool modified = false;
            foreach (var run in ctx.TextRuns(entry.Paragraph).ToList()) {
                if (SameFont(ctx.FontOf(run, entry.StyleId), rules.FontName))
                    continue;

                var rPr = run.RunProperties ??= new RunProperties();
                var rf = rPr.RunFonts ??= new RunFonts();
                rf.Ascii = rules.FontName;
                rf.HighAnsi = rules.FontName;
                rf.ComplexScript = rules.FontName;
                modified = true;
            }

            if (modified)
                count++;
        }

        return count;
    }

    private static int FixItemFontSize(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        var halfPoints = HalfPoints(rules.FontSizePt);

        foreach (var entry in GetBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            bool modified = false;
            foreach (var run in ctx.TextRuns(entry.Paragraph).ToList()) {
                if (Math.Abs(ctx.SizeOf(run, entry.StyleId) - rules.FontSizePt) < 0.01)
                    continue;

                var rPr = run.RunProperties ??= new RunProperties();
                rPr.FontSize = new FontSize { Val = halfPoints };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
                modified = true;
            }

            if (modified)
                count++;
        }

        return count;
    }

    private static int FixItemLineSpacing(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        var line = ((int)Math.Round(rules.LineSpacing * 240)).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            if (SpacingOk(ctx.SpacingOf(entry.Paragraph), rules))
                continue;

            var spacing = entry.Paragraph.ParagraphProperties?.SpacingBetweenLines;
            if (spacing is null) {
                entry.Paragraph.ParagraphProperties ??= new ParagraphProperties();
                spacing = new SpacingBetweenLines();
                entry.Paragraph.ParagraphProperties.AppendChild(spacing);
            }

            spacing.Line = line;
            spacing.LineRule = LineSpacingRuleValues.Auto;
            count++;
        }

        return count;
    }

    private static int FixItemAlignment(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var props = entry.Paragraph.ParagraphProperties ??= new ParagraphProperties();
            var jc = props.Justification ??= new Justification();
            if (jc.Val != JustificationValues.Both) {
                jc.Val = JustificationValues.Both;
                count++;
            }
        }
        return count;
    }

    private static int FixItemIndent(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        int targetTwips = (int)Math.Round(rules.FirstLineIndentCm * TwipsPerCm);
        double toleranceTwips = 0.1 * TwipsPerCm;

        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            double actualTwips = 0;
            var ind = entry.Paragraph.ParagraphProperties?.Indentation;
            if (ind?.FirstLine?.Value != null && double.TryParse(ind.FirstLine.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fl)) {
                actualTwips = fl;
            }

            if (Math.Abs(actualTwips - targetTwips) > toleranceTwips) {
                var props = entry.Paragraph.ParagraphProperties ??= new ParagraphProperties();
                var indentation = props.Indentation ??= new Indentation();
                indentation.FirstLine = targetTwips.ToString(CultureInfo.InvariantCulture);
                indentation.FirstLineChars = null;
                count++;
            }
        }
        return count;
    }

    private static int FixItemSpacingBeforeAfter(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var spacing = entry.Paragraph.ParagraphProperties?.SpacingBetweenLines;
            if (spacing != null && ((spacing.Before != null && spacing.Before.Value != "0") || (spacing.After != null && spacing.After.Value != "0"))) {
                spacing.Before = "0";
                spacing.After = "0";
                count++;
            }
        }
        return count;
    }

    private static int FixItemWidowControl(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index))
                continue;

            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var props = entry.Paragraph.ParagraphProperties ??= new ParagraphProperties();
            var wc = props.WidowControl ??= new WidowControl();
            if (wc.Val?.Value == false) {
                wc.Val = true;
                count++;
            }
        }
        return count;
    }

    private static int FixItemTableFormatting(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        if (!rules.CheckTables) return 0;
        int count = 0;
        string tableLineSpacingStr = Math.Round(rules.TableLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string fontSizeHalfPtStr = Math.Round(rules.TableFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetTableCellParagraphEntries(ctx)) {
            if (!indices.Contains(entry.Index)) continue;
            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var p = entry.Paragraph;
            if (!ctx.HasText(p)) continue;

            bool violates = false;
            var styleId = ctx.ParagraphStyleId(p);
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            if (run != null) {
                var curSize = ctx.SizeOf(run, styleId);
                var curFont = ctx.FontOf(run, styleId);
                if (Math.Abs(curSize - rules.TableFontSizePt) > 0.01) violates = true;
                if (!string.IsNullOrEmpty(rules.TableFontFamily) && !curFont.Contains(rules.TableFontFamily)) violates = true;
            }

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != tableLineSpacingStr) violates = true;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableRequireNoIndent && indent != null && indent != "0") violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            if (rules.TableRequireNoIndent) {
                var ind = props.Indentation ??= new Indentation();
                ind.FirstLine = "0";
            }
            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = tableLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;

            foreach (var r in p.Descendants<Run>()) {
                var rPr = r.RunProperties ??= new RunProperties();
                rPr.FontSize = new FontSize { Val = fontSizeHalfPtStr };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = fontSizeHalfPtStr };
                if (!string.IsNullOrEmpty(rules.TableFontFamily)) {
                    var rf = rPr.RunFonts ??= new RunFonts();
                    rf.Ascii = rules.TableFontFamily;
                    rf.HighAnsi = rules.TableFontFamily;
                    rf.ComplexScript = rules.TableFontFamily;
                }
            }
            count++;
        }

        string capLineSpacingStr = Math.Round(rules.TableCaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string capBeforeStr = Math.Round(rules.TableCaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capAfterStr = Math.Round(rules.TableCaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        var expectedJc = rules.TableCaptionAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in GetTableCaptionEntries(ctx)) {
            if (!indices.Contains(entry.Index)) continue;
            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var p = entry.Paragraph;
            bool violates = false;

            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != expectedJc) violates = true;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableCaptionRequireNoIndent && indent != null && indent != "0") violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != capLineSpacingStr) violates = true;
            if (rules.TableCaptionSpacingBeforePt > 0 && spacing?.Before?.Value != capBeforeStr) violates = true;
            if (rules.TableCaptionSpacingAfterPt > 0 && spacing?.After?.Value != capAfterStr) violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var just = props.Justification ??= new Justification();
            just.Val = expectedJc;

            if (rules.TableCaptionRequireNoIndent) {
                var ind = props.Indentation ??= new Indentation();
                ind.FirstLine = "0";
            }

            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = capLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;
            sp.Before = capBeforeStr;
            sp.After = capAfterStr;
            count++;
        }

        return count;
    }

    private static int FixItemFigureFormatting(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        string figSpacingBeforeStr = Math.Round(rules.FigureSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string figSpacingAfterStr = Math.Round(rules.FigureSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingBeforeStr = Math.Round(rules.CaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingAfterStr = Math.Round(rules.CaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capLineSpacingStr = Math.Round(rules.CaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        var figJc = rules.FigureAlignment.ToLowerInvariant() switch {
            "left" => JustificationValues.Left,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Center
        };
        var capJc = rules.CaptionAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in GetFigureEntries(ctx)) {
            if (!indices.Contains(entry.Index)) continue;
            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var p = entry.Paragraph;
            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;

            if (entry.IsFigure) {
                bool violates = false;
                if (rules.FigureAlignment == "Center" && jc != JustificationValues.Center) violates = true;
                else if (rules.FigureAlignment != "Center" && jc != figJc) violates = true;
                if (rules.FigureRequireNoIndent && indent != null && indent != "0") violates = true;
                if (spacing?.Before?.Value != figSpacingBeforeStr || spacing?.After?.Value != figSpacingAfterStr) violates = true;

                if (!violates) continue;

                var props = p.ParagraphProperties ??= new ParagraphProperties();
                var just = props.Justification ??= new Justification();
                just.Val = figJc;
                if (rules.FigureRequireNoIndent) {
                    var ind = props.Indentation ??= new Indentation();
                    ind.FirstLine = "0";
                }
                var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
                sp.Before = figSpacingBeforeStr;
                sp.After = figSpacingAfterStr;
                count++;
            } else if (entry.IsCaption) {
                bool violates = false;
                if (rules.CaptionAlignment == "Center" && jc != JustificationValues.Center) violates = true;
                else if (rules.CaptionAlignment != "Center" && jc != capJc) violates = true;
                if (rules.CaptionRequireNoIndent && indent != null && indent != "0") violates = true;
                if (spacing?.Line?.Value != capLineSpacingStr) violates = true;
                if (rules.CaptionSpacingBeforePt > 0 && spacing?.Before?.Value != capSpacingBeforeStr) violates = true;
                if (rules.CaptionSpacingAfterPt > 0 && spacing?.After?.Value != capSpacingAfterStr) violates = true;

                if (!violates) continue;

                var props = p.ParagraphProperties ??= new ParagraphProperties();
                var just = props.Justification ??= new Justification();
                just.Val = capJc;
                if (rules.CaptionRequireNoIndent) {
                    var ind = props.Indentation ??= new Indentation();
                    ind.FirstLine = "0";
                }
                var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
                sp.Line = capLineSpacingStr;
                sp.LineRule = LineSpacingRuleValues.Auto;
                sp.Before = capSpacingBeforeStr;
                sp.After = capSpacingAfterStr;
                count++;
            }
        }
        return count;
    }

    private static int FixItemCodeSnippets(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        string codeLineSpacingStr = Math.Round(rules.CodeLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string halfPointsStr = Math.Round(rules.CodeFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetCodeSnippetEntries(ctx, rules)) {
            if (!indices.Contains(entry.Index)) continue;
            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var p = entry.Paragraph;
            string? styleId = ctx.ParagraphStyleId(p);
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
            bool violates = false;

            if (run != null) {
                var curSize = ctx.SizeOf(run, styleId);
                var curFont = ctx.FontOf(run, styleId);
                if (Math.Abs(curSize - rules.CodeFontSizePt) > 0.01) violates = true;
                if (curFont != rules.CodeFontFamily && !curFont.Contains(rules.CodeFontFamily)) violates = true;
            }

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.CodeRequireNoIndent && indent != null && indent != "0") violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != codeLineSpacingStr) violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            if (rules.CodeRequireNoIndent) {
                var ind = props.Indentation ??= new Indentation();
                ind.FirstLine = "0";
            }
            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = codeLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;

            foreach (var r in p.Descendants<Run>()) {
                var rPr = r.RunProperties ??= new RunProperties();
                rPr.FontSize = new FontSize { Val = halfPointsStr };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPointsStr };
                var rf = rPr.RunFonts ??= new RunFonts();
                rf.Ascii = rules.CodeFontFamily;
                rf.HighAnsi = rules.CodeFontFamily;
                rf.ComplexScript = rules.CodeFontFamily;
            }
            count++;
        }
        return count;
    }

    private static int FixItemHeadings(Context ctx, RuleSet rules, IReadOnlySet<int> indices, IReadOnlyDictionary<int, string>? expectedTexts)
    {
        int count = 0;
        string headLineSpacingStr = Math.Round(rules.HeadingLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string headBeforeStr = Math.Round(rules.HeadingSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string headAfterStr = Math.Round(rules.HeadingSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string headFontSizeHalfPt = Math.Round(rules.HeadingFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        var expectedJc = rules.HeadingAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in GetHeadingEntries(ctx)) {
            if (!indices.Contains(entry.Index)) continue;
            if (expectedTexts != null && expectedTexts.TryGetValue(entry.Index, out var expText) && !MatchesText(entry.Paragraph.InnerText, expText))
                continue;

            var p = entry.Paragraph;
            string? styleId = ctx.ParagraphStyleId(p);
            bool violates = false;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.HeadingRequireNoIndent && indent != null && indent != "0") violates = true;

            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            if (run != null) {
                var curFont = ctx.FontOf(run, styleId);
                var curSize = ctx.SizeOf(run, styleId);
                if (rules.HeadingRequireBold) {
                    bool isBold = run?.RunProperties?.Bold != null && (run.RunProperties.Bold.Val == null || run.RunProperties.Bold.Val.Value);
                    if (!isBold) violates = true;
                }
                if (!string.IsNullOrEmpty(rules.HeadingFontFamily) && !curFont.Contains(rules.HeadingFontFamily)) violates = true;
                if (rules.HeadingFontSizePt > 0 && Math.Abs(curSize - rules.HeadingFontSizePt) > 0.01) violates = true;
            }

            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != expectedJc) violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != headLineSpacingStr) violates = true;
            if (rules.HeadingSpacingBeforePt > 0 && spacing?.Before?.Value != headBeforeStr) violates = true;
            if (rules.HeadingSpacingAfterPt > 0 && spacing?.After?.Value != headAfterStr) violates = true;

            string text = p.InnerText.TrimEnd();
            if (rules.HeadingRemoveTrailingDot && text.EndsWith(".")) violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            if (rules.HeadingRequireNoIndent) ind.FirstLine = "0";

            var just = props.Justification ??= new Justification();
            just.Val = expectedJc;

            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = headLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;
            sp.Before = headBeforeStr;
            sp.After = headAfterStr;

            foreach (var r in p.Descendants<Run>()) {
                var rPr = r.RunProperties ??= new RunProperties();
                if (rules.HeadingRequireBold) {
                    rPr.Bold = new Bold { Val = true };
                    rPr.BoldComplexScript = new BoldComplexScript { Val = true };
                }
                if (!string.IsNullOrEmpty(rules.HeadingFontFamily)) {
                    var rf = rPr.RunFonts ??= new RunFonts();
                    rf.Ascii = rules.HeadingFontFamily;
                    rf.HighAnsi = rules.HeadingFontFamily;
                    rf.ComplexScript = rules.HeadingFontFamily;
                }
                if (rules.HeadingFontSizePt > 0) {
                    rPr.FontSize = new FontSize { Val = headFontSizeHalfPt };
                    rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = headFontSizeHalfPt };
                }
            }

            if (rules.HeadingRemoveTrailingDot) {
                var lastRun = p.Descendants<Run>().LastOrDefault(r => r.Elements<Text>().Any());
                var lastText = lastRun?.Elements<Text>().LastOrDefault();
                if (lastText != null && lastText.Text.EndsWith(".")) {
                    lastText.Text = lastText.Text.TrimEnd('.');
                }
            }

            count++;
        }
        return count;
    }

    private static int FixMargin(Context ctx, MarginRule rule, RuleSet rules,
        Func<PageMargin, double?> read, Action<PageMargin, int> write)
    {
        int count = 0;
        int target = (int)Math.Round(rule.Cm * TwipsPerCm);

        foreach (var section in ctx.Sections) {
            var margin = section.GetFirstChild<PageMargin>();
            if (margin is null || read(margin) is not { } twips)
                continue;

            if (MarginOk(twips / TwipsPerCm, rule, rules.MarginToleranceCm))
                continue;

            write(margin, target);
            count++;
        }

        return count;
    }

    private static int FixFontName(Context ctx, RuleSet rules)
    {
        int count = 0;

        foreach (var entry in GetAllParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            if (IsCodeSnippetParagraph(p, rules))
                continue;

            var styleId = entry.StyleId;
            bool modified = false;
            foreach (var run in ctx.TextRuns(p).ToList()) {
                if (SameFont(ctx.FontOf(run, styleId), rules.FontName))
                    continue;

                var props = run.RunProperties ??= new RunProperties();
                var rf = props.RunFonts ??= new RunFonts();
                rf.Ascii = rules.FontName;
                rf.HighAnsi = rules.FontName;
                rf.EastAsia = rules.FontName;
                rf.ComplexScript = rules.FontName;
                modified = true;
            }

            if (modified)
                count++;
        }

        return count;
    }

    private static int FixFontSize(Context ctx, RuleSet rules)
    {
        int count = 0;
        var halfPoints = HalfPoints(rules.FontSizePt);

        foreach (var entry in GetBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            var styleId = entry.StyleId;
            bool modified = false;
            foreach (var run in ctx.TextRuns(p).ToList()) {
                if (Math.Abs(ctx.SizeOf(run, styleId) - rules.FontSizePt) < 0.01)
                    continue;

                var props = run.RunProperties ??= new RunProperties();
                props.FontSize = new FontSize { Val = halfPoints };
                props.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
                modified = true;
            }

            if (modified)
                count++;
        }

        return count;
    }

    private static int FixSpacing(Context ctx, RuleSet rules)
    {
        int count = 0;
        var line = ((int)Math.Round(rules.LineSpacing * 240)).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            if (SpacingOk(ctx.SpacingOf(p), rules))
                continue;

            // Отступы до и после абзаца (Before/After) остаются как были
            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var spacing = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            spacing.Line = line;
            spacing.LineRule = LineSpacingRuleValues.Auto;
            count++;
        }

        return count;
    }

    // ----- Исправление: Выравнивание -----
    private static int FixAlignment(Context ctx, RuleSet rules)
    {
        if (string.IsNullOrWhiteSpace(rules.Alignment) || !rules.Alignment.Contains("Justify", StringComparison.OrdinalIgnoreCase)) 
            return 0;

        int count = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            var props = p.ParagraphProperties ??= new ParagraphProperties();
            if (props.Justification?.Val?.Value != JustificationValues.Both) {
                props.Justification = new Justification { Val = JustificationValues.Both };
                count++;
            }
        }
        return count;
    }

    // ----- Исправление: Абзацный отступ -----
    private static int FixIndent(Context ctx, RuleSet rules)
    {
        int count = 0;
        double targetTwips = rules.FirstLineIndentCm * TwipsPerCm;
        string targetStr = Math.Round(targetTwips).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();

            double actualTwips = 0;
            if (ind.FirstLine?.Value != null && double.TryParse(ind.FirstLine.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fl)) {
                actualTwips = fl;
            }

            if (Math.Abs(actualTwips - targetTwips) > (0.1 * TwipsPerCm)) {
                ind.FirstLine = targetStr;
                count++;
            }
        }
        return count;
    }

    private static int FixSpacingBeforeAfter(Context ctx, RuleSet rules)
    {
        int c = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing != null && ((spacing.Before != null && spacing.Before.Value != "0") || (spacing.After != null && spacing.After.Value != "0"))) {
                spacing.Before = "0";
                spacing.After = "0";
                c++;
            }
        }
        return c;
    }

    private static int FixWidowControl(Context ctx, RuleSet rules)
    {
        int c = 0;
        foreach (var entry in GetTextBodyParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var wc = props.WidowControl;
            if (wc != null && wc.Val?.Value == false) {
                wc.Val = true;
                c++;
            }
        }
        return c;
    }

    private static int FixTableFormatting(Context ctx, RuleSet rules)
    {
        if (!rules.CheckTables) return 0;
        int c = 0;
        
        string tableLineSpacingStr = Math.Round(rules.TableLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string fontSizeHalfPtStr = Math.Round(rules.TableFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetTableCellParagraphEntries(ctx)) {
            var p = entry.Paragraph;
            if (!ctx.HasText(p)) continue;

            bool violates = false;
            var styleId = ctx.ParagraphStyleId(p);
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            if (run != null) {
                var curSize = ctx.SizeOf(run, styleId);
                var curFont = ctx.FontOf(run, styleId);
                if (Math.Abs(curSize - rules.TableFontSizePt) > 0.01) violates = true;
                if (!string.IsNullOrEmpty(rules.TableFontFamily) && !curFont.Contains(rules.TableFontFamily)) violates = true;
            }

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != tableLineSpacingStr) violates = true;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableRequireNoIndent && indent != null && indent != "0") violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            if (rules.TableRequireNoIndent) {
                var ind = props.Indentation ??= new Indentation();
                ind.FirstLine = "0";
            }
            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = tableLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;

            foreach (var r in p.Descendants<Run>()) {
                var rPr = r.RunProperties ??= new RunProperties();
                rPr.FontSize = new FontSize { Val = fontSizeHalfPtStr };
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = fontSizeHalfPtStr };
                if (!string.IsNullOrEmpty(rules.TableFontFamily)) {
                    var rf = rPr.RunFonts ??= new RunFonts();
                    rf.Ascii = rules.TableFontFamily;
                    rf.HighAnsi = rules.TableFontFamily;
                    rf.ComplexScript = rules.TableFontFamily;
                }
            }
            c++;
        }

        // Подписи к таблицам (Table Captions)
        string capLineSpacingStr = Math.Round(rules.TableCaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string capBeforeStr = Math.Round(rules.TableCaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capAfterStr = Math.Round(rules.TableCaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        var expectedJc = rules.TableCaptionAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in GetTableCaptionEntries(ctx)) {
            var p = entry.Paragraph;
            bool violates = false;
            var curJc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (curJc != expectedJc) violates = true;

            var ind = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.TableCaptionRequireNoIndent && ind != null && ind != "0") violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != capLineSpacingStr || spacing.Before?.Value != capBeforeStr || spacing.After?.Value != capAfterStr)
                violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var jc = props.Justification ??= new Justification();
            jc.Val = expectedJc;

            if (rules.TableCaptionRequireNoIndent) {
                var indentation = props.Indentation ??= new Indentation();
                indentation.FirstLine = "0";
            }

            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = capLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;
            sp.Before = capBeforeStr;
            sp.After = capAfterStr;
            c++;
        }

        return c;
    }

    private static int FixFigureFormatting(Context ctx, RuleSet rules)
    {
        int count = 0;
        
        string figSpacingBeforeStr = Math.Round(rules.FigureSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string figSpacingAfterStr = Math.Round(rules.FigureSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingBeforeStr = Math.Round(rules.CaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingAfterStr = Math.Round(rules.CaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capLineSpacingStr = Math.Round(rules.CaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetFigureEntries(ctx)) {
            var p = entry.Paragraph;
            bool isFigure = entry.IsFigure;
            bool isCaption = entry.IsCaption;

            bool violates = false;
            var curJc = p.ParagraphProperties?.Justification?.Val?.Value;
            var indVal = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;

            if (isFigure) {
                if (rules.FigureAlignment == "Center" && curJc != JustificationValues.Center) violates = true;
                if (rules.FigureRequireNoIndent && indVal != null && indVal != "0") violates = true;
                if (spacing == null || spacing.Before?.Value != figSpacingBeforeStr || spacing.After?.Value != figSpacingAfterStr) violates = true;
            } else if (isCaption) {
                if (rules.CaptionAlignment == "Center" && curJc != JustificationValues.Center) violates = true;
                if (rules.CaptionRequireNoIndent && indVal != null && indVal != "0") violates = true;
                if (spacing == null || spacing.Line?.Value != capLineSpacingStr || spacing.Before?.Value != capSpacingBeforeStr || spacing.After?.Value != capSpacingAfterStr) violates = true;
            }

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();

            if (isFigure) {
                if (rules.FigureAlignment == "Center") props.Justification = new Justification { Val = JustificationValues.Center };
                if (rules.FigureRequireNoIndent) ind.FirstLine = "0";
                sp.Before = figSpacingBeforeStr;
                sp.After = figSpacingAfterStr;
            } 
            else if (isCaption) {
                if (rules.CaptionAlignment == "Center") props.Justification = new Justification { Val = JustificationValues.Center };
                if (rules.CaptionRequireNoIndent) ind.FirstLine = "0";
                sp.Line = capLineSpacingStr;
                sp.LineRule = LineSpacingRuleValues.Auto;
                sp.Before = capSpacingBeforeStr;
                sp.After = capSpacingAfterStr;
            }
            count++;
        }
        return count;
    }

    // ----- Исправление: Листинги кода -----
    private static int FixCodeSnippets(Context ctx, RuleSet rules)
    {
        int count = 0;
        string codeLineSpacingStr = Math.Round(rules.CodeLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string halfPointsStr = Math.Round(rules.CodeFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        foreach (var entry in GetCodeSnippetEntries(ctx, rules)) {
            var p = entry.Paragraph;
            if (!ctx.HasText(p)) continue;

            bool violates = false;
            var indVal = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.CodeRequireNoIndent && indVal != null && indVal != "0") violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != codeLineSpacingStr) violates = true;

            var styleId = ctx.ParagraphStyleId(p);
            var firstRun = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            if (firstRun != null) {
                var curFont = ctx.FontOf(firstRun, styleId);
                var curSize = ctx.SizeOf(firstRun, styleId);
                if (!string.IsNullOrEmpty(rules.CodeFontFamily) && !curFont.Contains(rules.CodeFontFamily)) violates = true;
                if (Math.Abs(curSize - rules.CodeFontSizePt) > 0.01) violates = true;
            }

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            if (rules.CodeRequireNoIndent) ind.FirstLine = "0";

            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = codeLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;

            foreach (var run in p.Descendants<Run>()) {
                var rPr = run.RunProperties ??= new RunProperties();
                var rf = rPr.RunFonts ??= new RunFonts();
                rf.Ascii = rules.CodeFontFamily;
                rf.HighAnsi = rules.CodeFontFamily;
                rf.ComplexScript = rules.CodeFontFamily;
                rPr.FontSize = new FontSize { Val = halfPointsStr }; 
                rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPointsStr };
            }
            count++;
        }
        return count;
    }

    // ----- Исправление: Заголовки -----
    private static int FixHeadings(Context ctx, RuleSet rules)
    {
        int count = 0;
        string headLineSpacingStr = Math.Round(rules.HeadingLineSpacing * 240).ToString(CultureInfo.InvariantCulture);
        string headBeforeStr = Math.Round(rules.HeadingSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string headAfterStr = Math.Round(rules.HeadingSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string headFontSizeHalfPt = Math.Round(rules.HeadingFontSizePt * 2).ToString(CultureInfo.InvariantCulture);

        var expectedJc = rules.HeadingAlignment.ToLowerInvariant() switch {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            _ => JustificationValues.Left
        };

        foreach (var entry in GetHeadingEntries(ctx)) {
            var p = entry.Paragraph;
            if (!ctx.HasText(p)) continue;

            bool violates = false;
            var curJc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (curJc != expectedJc) violates = true;

            var indVal = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.HeadingRequireNoIndent && indVal != null && indVal != "0") violates = true;

            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing == null || spacing.Line?.Value != headLineSpacingStr || spacing.Before?.Value != headBeforeStr || spacing.After?.Value != headAfterStr) violates = true;

            var styleId = ctx.ParagraphStyleId(p);
            var firstRun = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text))) ?? p.Descendants<Run>().FirstOrDefault();
            if (firstRun != null) {
                if (rules.HeadingRequireBold) {
                    bool isBold = firstRun.RunProperties?.Bold != null && (firstRun.RunProperties.Bold.Val == null || firstRun.RunProperties.Bold.Val.Value);
                    if (!isBold) violates = true;
                }
                var curFont = ctx.FontOf(firstRun, styleId);
                if (!string.IsNullOrEmpty(rules.HeadingFontFamily) && !curFont.Contains(rules.HeadingFontFamily)) violates = true;
                var curSize = ctx.SizeOf(firstRun, styleId);
                if (rules.HeadingFontSizePt > 0 && Math.Abs(curSize - rules.HeadingFontSizePt) > 0.01) violates = true;
            }

            if (rules.HeadingRemoveTrailingDot && p.InnerText.TrimEnd().EndsWith('.')) violates = true;

            if (!violates) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            if (rules.HeadingRequireNoIndent) ind.FirstLine = "0";

            var jc = props.Justification ??= new Justification();
            jc.Val = expectedJc;

            var sp = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            sp.Line = headLineSpacingStr;
            sp.LineRule = LineSpacingRuleValues.Auto;
            sp.Before = headBeforeStr;
            sp.After = headAfterStr;

            foreach (var run in p.Descendants<Run>()) {
                var rPr = run.RunProperties ??= new RunProperties();
                if (rules.HeadingRequireBold) {
                    rPr.Bold = new Bold { Val = true };
                    rPr.BoldComplexScript = new BoldComplexScript { Val = true };
                }
                if (!string.IsNullOrEmpty(rules.HeadingFontFamily)) {
                    var rf = rPr.RunFonts ??= new RunFonts();
                    rf.Ascii = rules.HeadingFontFamily;
                    rf.HighAnsi = rules.HeadingFontFamily;
                    rf.ComplexScript = rules.HeadingFontFamily;
                }
                if (rules.HeadingFontSizePt > 0) {
                    rPr.FontSize = new FontSize { Val = headFontSizeHalfPt };
                    rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = headFontSizeHalfPt };
                }
            }

            if (rules.HeadingRemoveTrailingDot) {
                var texts = p.Descendants<Text>().ToList();
                for (int i = texts.Count - 1; i >= 0; i--) {
                    var t = texts[i];
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;
                    
                    if (t.Text.TrimEnd().EndsWith(".")) {
                        int dotIndex = t.Text.LastIndexOf('.');
                        t.Text = t.Text.Remove(dotIndex, 1);
                        break;
                    }
                }
            }
            count++;
        }
        return count;
    }

    private static int FixPageNumbers(Context ctx, RuleSet rules)
    {
        if (ctx.HasPageNumbers())
            return 0;

        int count = 0;
        var main = ctx.Main;
        var sections = ctx.Sections.ToList();

        // В документе нет описания раздела: создаём его (формат A4 и поля по правилам)
        if (sections.Count == 0) {
            var created = new SectionProperties(
                new PageSize { Width = 11906U, Height = 16838U },
                new PageMargin {
                    Left = (uint)Math.Round(rules.Left.Cm * TwipsPerCm),
                    Right = (uint)Math.Round(rules.Right.Cm * TwipsPerCm),
                    Top = (int)Math.Round(rules.Top.Cm * TwipsPerCm),
                    Bottom = (int)Math.Round(rules.Bottom.Cm * TwipsPerCm),
                    Header = 708U,
                    Footer = 708U,
                    Gutter = 0U,
                });
            ctx.Body.AppendChild(created);
            sections.Add(created);
        }

        for (int i = 0; i < sections.Count; i++) {
            var section = sections[i];
            var reference = section.Elements<FooterReference>()
                .FirstOrDefault(r => r.Type is null || r.Type.Value == HeaderFooterValues.Default);

            FooterPart? part = null;
            if (reference?.Id?.Value is { } id)
                part = main.FooterParts.FirstOrDefault(f => main.GetIdOfPart(f) == id);

            if (part is null) {
                // Следующие разделы без своего колонтитула наследуют колонтитул первого
                if (i > 0)
                    continue;

                part = main.AddNewPart<FooterPart>();
                part.Footer = new Footer(new Paragraph());
                section.PrependChild(new FooterReference {
                    Type = HeaderFooterValues.Default,
                    Id = main.GetIdOfPart(part),
                });
            }

            if (part.Footer is null)
                part.Footer = new Footer(new Paragraph());

            if (HasPageField(part.Footer))
                continue;

            // Пустые абзацы колонтитула заменяем номером, чтобы не добавлять лишнюю строку
            var footer = part.Footer;
            if (footer.Elements<Paragraph>().All(p => string.IsNullOrWhiteSpace(p.InnerText)))
                foreach (var empty in footer.Elements<Paragraph>().ToList())
                    empty.Remove();

            footer.AppendChild(BuildPageNumberParagraph(rules));
            footer.Save();
            count++;
        }

        return count;
    }

    // Абзац по центру с полем PAGE (номер страницы)
    private static Paragraph BuildPageNumberParagraph(RuleSet rules)
    {
        RunProperties Props() => new() {
            RunFonts = new RunFonts { Ascii = rules.FontName, HighAnsi = rules.FontName, EastAsia = rules.FontName, ComplexScript = rules.FontName },
            FontSize = new FontSize { Val = HalfPoints(rules.FontSizePt) },
        };

        return new Paragraph(
            new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
            new Run(Props(), new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(Props(), new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(Props(), new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(Props(), new Text("1")),
            new Run(Props(), new FieldChar { FieldCharType = FieldCharValues.End }));
    }

    private static string HalfPoints(double pt)
        => ((int)Math.Round(pt * 2)).ToString(CultureInfo.InvariantCulture);

    private static bool HasPageField(OpenXmlElement root)
        => root.Descendants<FieldCode>().Any(f => PageField.IsMatch(f.Text ?? ""))
        || root.Descendants<SimpleField>().Any(f => PageField.IsMatch(f.Instruction?.Value ?? ""));

    private const long MaxUncompressedBytes = 20 * 1024 * 1024; // 20 MB
    private const int MaxZipEntries = 500;
    private const double MaxCompressionRatio = 100.0;

    private static void ValidateZipArchive(Stream stream)
    {
        long initialPos = stream.Position;
        try {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxZipEntries)
                throw new InvalidDataException("Файл повреждён или слишком большой (превышен лимит размера или структуры архива).");

            long totalUncompressed = 0;
            foreach (var entry in archive.Entries) {
                if (entry.Length < 0)
                    throw new InvalidDataException("Файл повреждён или слишком большой (превышен лимит размера или структуры архива).");

                totalUncompressed += entry.Length;
                if (totalUncompressed > MaxUncompressedBytes)
                    throw new InvalidDataException("Файл повреждён или слишком большой (превышен лимит размера или структуры архива).");

                if (entry.Length > 1024) {
                    if (entry.CompressedLength <= 0 || ((double)entry.Length / entry.CompressedLength) > MaxCompressionRatio)
                        throw new InvalidDataException("Файл повреждён или слишком большой (превышен лимит размера или структуры архива).");
                }
            }
        } catch (InvalidDataException) {
            throw;
        } catch (Exception ex) {
            throw new InvalidDataException("Файл повреждён или слишком большой (превышен лимит размера или структуры архива).", ex);
        } finally {
            stream.Position = initialPos;
        }
    }

    // Читаем с FileShare.ReadWrite: файл может быть открыт в Word
    private static MemoryStream CopyToMemory(string file)
    {
        var ms = new MemoryStream();
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            fs.CopyTo(ms);
        ms.Position = 0;
        ValidateZipArchive(ms);
        return ms;
    }

    // =====================================================================
    //  КОНТЕКСТ: разбор стилей, чтобы узнать итоговый шрифт, размер и интервал абзаца
    // =====================================================================

    private readonly record struct LineSpacing(bool IsAuto, bool IsExact, double Multiple, double Points);

    private sealed class Context
    {
        private readonly Dictionary<string, Style> _styles = new();
        private readonly string? _defaultParagraphStyle;
        private readonly double _defaultSizePt = 10;   // так Word считает, если в стилях размера нет
        private readonly RunFonts? _defaultFonts;
        private readonly SpacingBetweenLines? _defaultSpacing;
        private readonly string? _minorFont;
        private readonly string? _majorFont;

        public MainDocumentPart Main { get; }
        public Body Body { get; }

        public Context(WordprocessingDocument doc)
        {
            Main = doc.MainDocumentPart ?? throw new InvalidDataException("В файле нет основного документа Word.");
            Body = Main.Document?.Body ?? throw new InvalidDataException("В документе нет текста.");

            if (Main.StyleDefinitionsPart?.Styles is { } styles) {
                foreach (var style in styles.Elements<Style>())
                    if (style.StyleId?.Value is { } id)
                        _styles[id] = style;

                _defaultParagraphStyle = styles.Elements<Style>()
                    .FirstOrDefault(s => s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true)
                    ?.StyleId?.Value;

                if (styles.DocDefaults is { } defaults) {
                    if (HalfPointsOf(defaults.Descendants<FontSize>().FirstOrDefault()) is { } half)
                        _defaultSizePt = half / 2.0;

                    _defaultFonts = defaults.Descendants<RunFonts>().FirstOrDefault();
                    _defaultSpacing = defaults.Descendants<SpacingBetweenLines>().FirstOrDefault();
                }
            }

            if (Main.ThemePart?.Theme is { } theme) {
                _minorFont = LatinTypeface(theme, "minorFont");
                _majorFont = LatinTypeface(theme, "majorFont");
            }
        }

        // ----- Обход документа -----

        /// <summary>Разделы страницы (последний описан в конце тела, остальные внутри абзацев).</summary>
        public IEnumerable<SectionProperties> Sections => Body.Descendants<SectionProperties>();

        public IEnumerable<Paragraph> AllParagraphs() => Body.Descendants<Paragraph>();

        /// <summary>Основной текст: не таблица, не заголовок, не оглавление.</summary>
        public IEnumerable<Paragraph> BodyParagraphs()
            => AllParagraphs().Where(p => !p.Ancestors<TableCell>().Any() && !IsStructural(p));

        public int CountTextParagraphs() => AllParagraphs().Count(HasText);

        public bool HasText(Paragraph p) => TextRuns(p).Any();

        /// <summary>Run именно этого абзаца (без вложенных надписей) с непустым текстом.</summary>
        public IEnumerable<Run> TextRuns(Paragraph p)
            => p.Descendants<Run>().Where(r => ReferenceEquals(r.Ancestors<Paragraph>().First(), p)
                                               && r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));

        public int CharCount(Run run) => run.Elements<Text>().Sum(t => (t.Text ?? "").Length);

        public string? ParagraphStyleId(Paragraph p)
            => p.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? _defaultParagraphStyle;

        private bool IsStructural(Paragraph p)
        {
            if (p.ParagraphProperties?.OutlineLevel?.Val?.Value is int level && level < 9)
                return true;

            foreach (var style in Chain(ParagraphStyleId(p))) {
                var id = style.StyleId?.Value ?? "";
                var name = style.StyleName?.Val?.Value ?? "";
                if (StructuralStyleWords.Any(w => id.Contains(w, StringComparison.OrdinalIgnoreCase)
                                               || name.Contains(w, StringComparison.OrdinalIgnoreCase)))
                    return true;
            }

            return false;
        }

        // ----- Итоговые значения -----

        public double SizeOf(Run run, string? paragraphStyleId)
        {
            if (HalfPointsOf(run.RunProperties?.FontSize) is { } direct)
                return direct / 2.0;

            foreach (var style in Chain(run.RunProperties?.RunStyle?.Val?.Value))
                if (HalfPointsOf(style.StyleRunProperties?.FontSize) is { } fromChar)
                    return fromChar / 2.0;

            foreach (var style in Chain(paragraphStyleId))
                if (HalfPointsOf(style.StyleRunProperties?.FontSize) is { } fromPara)
                    return fromPara / 2.0;

            return _defaultSizePt;
        }

        public string FontOf(Run run, string? paragraphStyleId)
        {
            if (FontName(run.RunProperties?.RunFonts) is { } direct)
                return direct;

            foreach (var style in Chain(run.RunProperties?.RunStyle?.Val?.Value))
                if (FontName(style.StyleRunProperties?.RunFonts) is { } fromChar)
                    return fromChar;

            foreach (var style in Chain(paragraphStyleId))
                if (FontName(style.StyleRunProperties?.RunFonts) is { } fromPara)
                    return fromPara;

            return FontName(_defaultFonts) ?? "Times New Roman";
        }

        public LineSpacing SpacingOf(Paragraph p)
        {
            string? line = null;
            LineSpacingRuleValues? rule = null;

            void Take(SpacingBetweenLines? s)
            {
                if (s is null)
                    return;
                line ??= s.Line?.Value;
                rule ??= s.LineRule?.Value;
            }

            // От самого сильного источника к слабому: абзац → его стили → умолчания документа
            Take(p.ParagraphProperties?.SpacingBetweenLines);
            foreach (var style in Chain(ParagraphStyleId(p)))
                Take(style.StyleParagraphProperties?.SpacingBetweenLines);
            Take(_defaultSpacing);

            double twips = double.TryParse(line, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 240;

            if (rule is null || rule == LineSpacingRuleValues.Auto)
                return new LineSpacing(IsAuto: true, IsExact: false, Multiple: twips / 240.0, Points: 0);

            return new LineSpacing(IsAuto: false, IsExact: rule == LineSpacingRuleValues.Exact, Multiple: 0, Points: twips / 20.0);
        }

        /// <summary>Есть ли номер страницы (поле PAGE) в каком-нибудь колонтитуле.</summary>
        public bool HasPageNumbers()
            => Main.FooterParts.Any(f => f.Footer is { } footer && HasPageField(footer))
            || Main.HeaderParts.Any(h => h.Header is { } header && HasPageField(header));

        // ----- Помощники -----

        // Стиль и его предки (basedOn); от повторов в цепочке защищаемся
        private IEnumerable<Style> Chain(string? styleId)
        {
            var seen = new HashSet<string>();
            var id = styleId;

            while (id is not null && seen.Add(id) && _styles.TryGetValue(id, out var style)) {
                yield return style;
                id = style.BasedOn?.Val?.Value;
            }
        }

        private static int? HalfPointsOf(FontSize? size)
            => int.TryParse(size?.Val?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var half) ? half : null;

        // Имя шрифта. Атрибуты темы (asciiTheme) в Word сильнее прямого имени, поэтому смотрим их первыми
        private string? FontName(RunFonts? fonts)
        {
            if (fonts is null)
                return null;

            var theme = fonts.AsciiTheme?.InnerText;
            if (string.IsNullOrEmpty(theme))
                theme = fonts.HighAnsiTheme?.InnerText;

            if (!string.IsNullOrEmpty(theme))
                return theme.StartsWith("major", StringComparison.OrdinalIgnoreCase)
                    ? _majorFont ?? "Calibri Light"
                    : _minorFont ?? "Calibri";

            return fonts.Ascii?.Value ?? fonts.HighAnsi?.Value;
        }

        // Шрифт из темы: <minorFont><latin typeface="Calibri"/>. Читаем без типов Drawing, чтобы не путать имена
        private static string? LatinTypeface(OpenXmlElement theme, string group)
        {
            var node = theme.Descendants().FirstOrDefault(e => e.LocalName == group);
            var latin = node?.ChildElements.FirstOrDefault(e => e.LocalName == "latin");
            var typeface = latin?.GetAttributes().FirstOrDefault(a => a.LocalName == "typeface").Value;
            return string.IsNullOrWhiteSpace(typeface) ? null : typeface;
        }
    }
}
