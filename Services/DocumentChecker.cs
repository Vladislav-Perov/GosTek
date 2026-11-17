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

    // ----- Поля страницы -----
    private static void CheckMargins(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var sections = ctx.Sections.Where(s => s.GetFirstChild<PageMargin>() is not null).ToList();
        if (sections.Count == 0)
            return;

        var sides = new (IssueKind Kind, string Name, MarginRule Rule, Func<PageMargin, double?> Read)[] {
            (IssueKind.MarginLeft,   "Левое",   rules.Left,   m => m.Left?.Value),
            (IssueKind.MarginRight,  "Правое",  rules.Right,  m => m.Right?.Value),
            (IssueKind.MarginTop,    "Верхнее", rules.Top,    m => m.Top?.Value),
            (IssueKind.MarginBottom, "Нижнее",  rules.Bottom, m => m.Bottom?.Value),
        };

        foreach (var (kind, name, rule, read) in sides) {
            var bad = new List<(int Number, double Cm)>();
            for (int i = 0; i < sections.Count; i++) {
                var margin = sections[i].GetFirstChild<PageMargin>()!;
                if (read(margin) is { } twips && !MarginOk(twips / TwipsPerCm, rule, rules.MarginToleranceCm))
                    bad.Add((i + 1, twips / TwipsPerCm));
            }

            if (bad.Count == 0)
                continue;

            var need = rule.Exact
                ? $"по правилам нужно {RuFormat.Number(rule.Cm)} см"
                : $"нужно не меньше {RuFormat.Number(rule.Cm)} см";
            var where = bad.Count == sections.Count
                ? "Весь документ"
                : "Разделы: " + string.Join(", ", bad.Select(b => b.Number));

            issues.Add(new CheckIssue(kind, IssueSeverity.Important,
                $"{name} поле {RuFormat.Number(bad[0].Cm)} см, {need}", where, CanFix: true));
        }
    }

    private static bool MarginOk(double actualCm, MarginRule rule, double tolerance)
        => rule.Exact
            ? Math.Abs(actualCm - rule.Cm) <= tolerance
            : actualCm >= rule.Cm - tolerance;

    // ----- Шрифт (гарнитура) -----
    private static void CheckFontName(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new Dictionary<string, int>(StringComparer.CurrentCultureIgnoreCase);   // гарнитура → число знаков
        var badParagraphs = new HashSet<Paragraph>();
        var all = new HashSet<Paragraph>();

        foreach (var p in ctx.AllParagraphs()) {
            var styleId = ctx.ParagraphStyleId(p);
            foreach (var run in ctx.TextRuns(p)) {
                all.Add(p);
                var font = ctx.FontOf(run, styleId);
                if (SameFont(font, rules.FontName))
                    continue;

                bad[font] = bad.GetValueOrDefault(font) + ctx.CharCount(run);
                badParagraphs.Add(p);
            }
        }

        if (bad.Count == 0)
            return;

        var worst = bad.OrderByDescending(kv => kv.Value).First().Key;
        issues.Add(new CheckIssue(IssueKind.FontName, IssueSeverity.Important,
            $"Шрифт {worst}, нужен {rules.FontName}",
            $"Весь текст: {badParagraphs.Count} из {all.Count} {RuFormat.Plural(all.Count, "абзаца", "абзацев", "абзацев")}",
            CanFix: true));
    }

    private static bool SameFont(string actual, string required)
        => string.Equals(actual.Trim(), required.Trim(), StringComparison.CurrentCultureIgnoreCase);

    // ----- Размер шрифта -----
    private static void CheckFontSize(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new Dictionary<double, int>();
        var badParagraphs = new HashSet<Paragraph>();
        var all = new HashSet<Paragraph>();

        foreach (var p in ctx.BodyParagraphs()) {
            var styleId = ctx.ParagraphStyleId(p);
            foreach (var run in ctx.TextRuns(p)) {
                all.Add(p);
                var size = ctx.SizeOf(run, styleId);
                if (Math.Abs(size - rules.FontSizePt) < 0.01)
                    continue;

                bad[size] = bad.GetValueOrDefault(size) + ctx.CharCount(run);
                badParagraphs.Add(p);
            }
        }

        if (bad.Count == 0)
            return;

        var worst = bad.OrderByDescending(kv => kv.Value).First().Key;
        issues.Add(new CheckIssue(IssueKind.FontSize, IssueSeverity.Important,
            $"Размер шрифта {RuFormat.Number(worst)} pt, нужен {RuFormat.Number(rules.FontSizePt)} pt",
            $"Основной текст: {badParagraphs.Count} из {all.Count} {RuFormat.Plural(all.Count, "абзаца", "абзацев", "абзацев")}",
            CanFix: true));
    }

    // ----- Межстрочный интервал -----
    private static void CheckSpacing(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new Dictionary<string, int>();
        int badCount = 0, total = 0;

        foreach (var p in ctx.BodyParagraphs()) {
            if (!ctx.HasText(p))
                continue;

            total++;
            var spacing = ctx.SpacingOf(p);
            if (SpacingOk(spacing, rules))
                continue;

            badCount++;
            var title = DescribeSpacing(spacing, rules);
            bad[title] = bad.GetValueOrDefault(title) + 1;
        }

        if (badCount == 0)
            return;

        issues.Add(new CheckIssue(IssueKind.LineSpacing, IssueSeverity.Warning,
            bad.OrderByDescending(kv => kv.Value).First().Key,
            $"Основной текст: {badCount} из {total} {RuFormat.Plural(total, "абзаца", "абзацев", "абзацев")}",
            CanFix: true));
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
            "Нет нумерации страниц", "Колонтитул", CanFix: true));
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
                $"Не найден раздел «{section.Title}»", "Конец документа", CanFix: false));
        }
    }

    private static string NormalizeTitle(string text)
        => Regex.Replace(text.Replace('ё', 'е').Replace('Ё', 'Е'), @"\s+", " ").Trim();

    // ----- Выравнивание -----
    private static void CheckAlignment(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        // Если в правилах указано выравнивание по ширине (Justify)
        if (string.IsNullOrWhiteSpace(rules.Alignment) || !rules.Alignment.Contains("Justify", StringComparison.OrdinalIgnoreCase)) 
            return;

        var badParagraphs = new HashSet<Paragraph>();
        int total = 0;

        foreach (var p in ctx.BodyParagraphs()) {
            if (!ctx.HasText(p)) continue;
            total++;
            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            if (jc != JustificationValues.Both) {
                badParagraphs.Add(p);
            }
        }

        if (badParagraphs.Count == 0) return;
        issues.Add(new CheckIssue(IssueKind.Alignment, IssueSeverity.Important,
            "Текст не выровнен по ширине",
            $"Основной текст: {badParagraphs.Count} из {total} {RuFormat.Plural(total, "абзаца", "абзацев", "абзацев")}",
            CanFix: true));
    }

    // ----- Абзацный отступ (Красная строка) -----
    private static void CheckIndent(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var badParagraphs = new HashSet<Paragraph>();
        double targetTwips = rules.FirstLineIndentCm * TwipsPerCm;
        double toleranceTwips = 0.1 * TwipsPerCm; // Допуск 1 мм
        int total = 0;

        foreach (var p in ctx.BodyParagraphs()) {
            if (!ctx.HasText(p)) continue;
            total++;
            double actualTwips = 0;
            var ind = p.ParagraphProperties?.Indentation;
            if (ind?.FirstLine?.Value != null && double.TryParse(ind.FirstLine.Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var fl)) {
                actualTwips = fl;
            }

            if (Math.Abs(actualTwips - targetTwips) > toleranceTwips) {
                badParagraphs.Add(p);
            }
        }

        if (badParagraphs.Count == 0) return;
        issues.Add(new CheckIssue(IssueKind.FirstLineIndent, IssueSeverity.Important,
            $"Абзацный отступ не {RuFormat.Number(rules.FirstLineIndentCm)} см",
            $"Основной текст: {badParagraphs.Count} из {total} {RuFormat.Plural(total, "абзаца", "абзацев", "абзацев")}",
            CanFix: true));
    }

    // ----- Интервалы До и После абзаца -----
    private static void CheckSpacingBeforeAfter(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new HashSet<Paragraph>();
        int total = 0;
        foreach(var p in ctx.BodyParagraphs()) {
            if (!ctx.HasText(p)) continue;
            total++;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing != null && ((spacing.Before != null && spacing.Before.Value != "0") || (spacing.After != null && spacing.After.Value != "0"))) {
                bad.Add(p);
            }
        }
        if (bad.Count > 0) issues.Add(new CheckIssue(IssueKind.SpacingBeforeAfter, IssueSeverity.Warning, 
            "Лишние отступы до/после абзаца", $"Основной текст: {bad.Count} из {total} {RuFormat.Plural(total, "абзаца", "абзацев", "абзацев")}", true));
    }

    // ----- Запрет висячих строк -----
    private static void CheckWidowControl(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new HashSet<Paragraph>();
        int total = 0;
        foreach(var p in ctx.BodyParagraphs()) {
            if (!ctx.HasText(p)) continue;
            total++;
            // В Word по умолчанию включено. Ошибка только если явно отключено (Val == false)
            if (p.ParagraphProperties?.WidowControl?.Val?.Value == false) {
                bad.Add(p);
            }
        }
        if (bad.Count > 0) issues.Add(new CheckIssue(IssueKind.WidowControl, IssueSeverity.Warning, 
            "Отключен запрет висячих строк", $"Основной текст: {bad.Count} из {total} {RuFormat.Plural(total, "абзаца", "абзацев", "абзацев")}", true));
    }

    // ----- Форматирование таблиц -----
    private static void CheckTableFormatting(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var tables = ctx.Body.Descendants<Table>().ToList();
        if (tables.Count == 0) return;
        
        int badCells = 0;
        foreach(var table in tables) {
            foreach(var p in table.Descendants<Paragraph>()) {
                if (!p.Ancestors<TableCell>().Any() || !ctx.HasText(p)) continue;
                
                bool needsFix = false;
                // Проверка шрифта (12pt = 24 в полупунктах)
                var run = p.Descendants<Run>().FirstOrDefault();
                if (run != null && ctx.SizeOf(run, ctx.ParagraphStyleId(p)) != 12.0) needsFix = true;
                
                // Проверка одинарного интервала (240 dxa)
                var spacing = p.ParagraphProperties?.SpacingBetweenLines;
                if (spacing == null || spacing.Line?.Value != "240") needsFix = true;
                
                // Проверка красной строки (не должно быть)
                var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
                if (indent != null && indent != "0") needsFix = true;
                
                if (needsFix) badCells++;
            }
        }
        if (badCells > 0) issues.Add(new CheckIssue(IssueKind.TableFormatting, IssueSeverity.Important, 
            "Форматирование таблиц не по ГОСТ", "Нужен кегль 12pt, интервал 1.0 и без красной строки", true));
    }

    // ----- Форматирование рисунков и подписей (Динамическое) -----
    private static void CheckFigureFormatting(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new HashSet<Paragraph>();
        int total = 0;
        
        string figSpacingBeforeStr = Math.Round(rules.FigureSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string figSpacingAfterStr = Math.Round(rules.FigureSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingBeforeStr = Math.Round(rules.CaptionSpacingBeforePt * 20).ToString(CultureInfo.InvariantCulture);
        string capSpacingAfterStr = Math.Round(rules.CaptionSpacingAfterPt * 20).ToString(CultureInfo.InvariantCulture);
        string capLineSpacingStr = Math.Round(rules.CaptionLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        foreach (var p in ctx.AllParagraphs()) {
            bool isFigure = p.Descendants<Drawing>().Any();
            string text = p.InnerText.Trim();
            bool isCaption = text.StartsWith("Рисунок", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Рис.", StringComparison.OrdinalIgnoreCase);

            if (!isFigure && !isCaption) continue;
            total++;
            bool needsFix = false;

            var jc = p.ParagraphProperties?.Justification?.Val?.Value;
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;

            if (isFigure) {
                if (rules.FigureAlignment == "Center" && jc != JustificationValues.Center) needsFix = true;
                if (rules.FigureRequireNoIndent && indent != null && indent != "0") needsFix = true;
                if (spacing?.Before?.Value != figSpacingBeforeStr || spacing?.After?.Value != figSpacingAfterStr) needsFix = true;
            } 
            else if (isCaption) {
                if (rules.CaptionAlignment == "Center" && jc != JustificationValues.Center) needsFix = true;
                if (rules.CaptionRequireNoIndent && indent != null && indent != "0") needsFix = true;
                if (spacing?.Line?.Value != capLineSpacingStr) needsFix = true;
                if (rules.CaptionSpacingBeforePt > 0 && spacing?.Before?.Value != capSpacingBeforeStr) needsFix = true;
                if (rules.CaptionSpacingAfterPt > 0 && spacing?.After?.Value != capSpacingAfterStr) needsFix = true;
            }

            if (needsFix) bad.Add(p);
        }

        if (bad.Count > 0) issues.Add(new CheckIssue(IssueKind.FigureFormatting, IssueSeverity.Important,
            "Нарушено форматирование рисунков или подписей", "Не соответствуют заданным отступам, выравниванию или красной строке", true));
    }

    // ----- Листинги кода (Динамическое) -----
    private static void CheckCodeSnippets(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new HashSet<Paragraph>();
        int total = 0;
        string codeLineSpacingStr = Math.Round(rules.CodeLineSpacing * 240).ToString(CultureInfo.InvariantCulture);

        foreach (var p in ctx.AllParagraphs()) {
            if (!ctx.HasText(p)) continue;
            
            string? styleId = ctx.ParagraphStyleId(p);
            bool isCodeStyle = styleId != null && (styleId.Contains("Code", StringComparison.OrdinalIgnoreCase) || styleId.Contains("Листинг", StringComparison.OrdinalIgnoreCase));
            
            var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
            bool isMonospace = run != null && (ctx.FontOf(run, styleId).Contains("Consolas") || ctx.FontOf(run, styleId).Contains("Courier"));
            
            if (!isCodeStyle && !isMonospace) continue;
            
            total++;
            bool needsFix = false;

            if (run != null && Math.Abs(ctx.SizeOf(run, styleId) - rules.CodeFontSizePt) > 0.01) needsFix = true;
            if (run != null && ctx.FontOf(run, styleId) != rules.CodeFontFamily && !ctx.FontOf(run, styleId).Contains(rules.CodeFontFamily)) needsFix = true;
            
            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.CodeRequireNoIndent && indent != null && indent != "0") needsFix = true;
            
            var spacing = p.ParagraphProperties?.SpacingBetweenLines;
            if (spacing?.Line?.Value != codeLineSpacingStr) needsFix = true;

            if (needsFix) bad.Add(p);
        }

        if (bad.Count > 0) issues.Add(new CheckIssue(IssueKind.CodeSnippetFormatting, IssueSeverity.Important,
            "Неверное оформление листинга кода", $"Нужен шрифт {rules.CodeFontFamily} {rules.CodeFontSizePt}pt, интервал {rules.CodeLineSpacing} и правильные отступы", true));
    }

    // ----- Заголовки (Динамическое) -----
    private static void CheckHeadings(Context ctx, RuleSet rules, List<CheckIssue> issues)
    {
        var bad = new HashSet<Paragraph>();
        int total = 0;
        
        foreach (var p in ctx.AllParagraphs()) {
            if (!ctx.HasText(p)) continue;
            
            int? outlineLvl = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
            string? styleId = ctx.ParagraphStyleId(p);
            bool isHeading = (outlineLvl != null && outlineLvl < 9) || 
                             (styleId != null && (styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase) || styleId.Contains("Заголовок", StringComparison.OrdinalIgnoreCase)));
            
            if (!isHeading) continue;
            total++;
            bool needsFix = false;

            var indent = p.ParagraphProperties?.Indentation?.FirstLine?.Value;
            if (rules.HeadingRequireNoIndent && indent != null && indent != "0") needsFix = true;
            
            if (rules.HeadingRequireBold) {
                var run = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
                bool isBold = run?.RunProperties?.Bold != null && (run.RunProperties.Bold.Val == null || run.RunProperties.Bold.Val.Value);
                if (run != null && !isBold) needsFix = true;
            }

            string text = p.InnerText.TrimEnd();
            if (rules.HeadingRemoveTrailingDot && text.EndsWith(".")) needsFix = true;

            if (needsFix) bad.Add(p);
        }

        if (bad.Count > 0) issues.Add(new CheckIssue(IssueKind.HeadingFormatting, IssueSeverity.Important,
            "Ошибка в оформлении заголовка", "Не соответствуют заданным правилам (шрифт, отступы или точка в конце)", true));
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

        foreach (var p in ctx.AllParagraphs().ToList()) {
            var styleId = ctx.ParagraphStyleId(p);
            foreach (var run in ctx.TextRuns(p).ToList()) {
                if (SameFont(ctx.FontOf(run, styleId), rules.FontName))
                    continue;

                // Новый RunFonts целиком: так пропадают атрибуты темы (asciiTheme и др.), которые перебивают имя шрифта
                var props = run.RunProperties ??= new RunProperties();
                props.RunFonts = new RunFonts {
                    Ascii = rules.FontName,
                    HighAnsi = rules.FontName,
                    EastAsia = rules.FontName,
                    ComplexScript = rules.FontName,
                };
                count++;
            }
        }

        return count;
    }

    private static int FixFontSize(Context ctx, RuleSet rules)
    {
        int count = 0;
        var halfPoints = HalfPoints(rules.FontSizePt);

        foreach (var p in ctx.BodyParagraphs().ToList()) {
            var styleId = ctx.ParagraphStyleId(p);
            foreach (var run in ctx.TextRuns(p).ToList()) {
                if (Math.Abs(ctx.SizeOf(run, styleId) - rules.FontSizePt) < 0.01)
                    continue;

                var props = run.RunProperties ??= new RunProperties();
                props.FontSize = new FontSize { Val = halfPoints };
                props.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
                count++;
            }
        }

        return count;
    }

    private static int FixSpacing(Context ctx, RuleSet rules)
    {
        int count = 0;
        var line = ((int)Math.Round(rules.LineSpacing * 240)).ToString(CultureInfo.InvariantCulture);

        foreach (var p in ctx.BodyParagraphs().ToList()) {
            if (!ctx.HasText(p) || SpacingOk(ctx.SpacingOf(p), rules))
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
        foreach (var p in ctx.BodyParagraphs().ToList()) {
            if (!ctx.HasText(p)) continue;
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

        foreach (var p in ctx.BodyParagraphs().ToList()) {
            if (!ctx.HasText(p)) continue;
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
        foreach(var p in ctx.BodyParagraphs().ToList()) {
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
        foreach(var p in ctx.BodyParagraphs().ToList()) {
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
        var tables = ctx.Body.Descendants<Table>().ToList();
        if (tables.Count == 0) return 0;
        int c = 0;
        
        foreach(var table in tables) {
            foreach(var p in table.Descendants<Paragraph>().ToList()) {
                if (!p.Ancestors<TableCell>().Any() || !ctx.HasText(p)) continue;
                
                var props = p.ParagraphProperties ??= new ParagraphProperties();
                
                var spacing = props.SpacingBetweenLines ??= new SpacingBetweenLines();
                spacing.Line = "240";
                spacing.LineRule = LineSpacingRuleValues.Auto;
                spacing.Before = "0";
                spacing.After = "0";
                
                var ind = props.Indentation ??= new Indentation();
                ind.FirstLine = "0";
                
                foreach(var run in p.Descendants<Run>()) {
                    var rPr = run.RunProperties ??= new RunProperties();
                    rPr.FontSize = new FontSize { Val = "24" };
                    rPr.FontSizeComplexScript = new FontSizeComplexScript { Val = "24" };
                }
                c++;
            }
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

        foreach (var p in ctx.AllParagraphs().ToList()) {
            bool isFigure = p.Descendants<Drawing>().Any();
            string text = p.InnerText.Trim();
            bool isCaption = text.StartsWith("Рисунок", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Рис.", StringComparison.OrdinalIgnoreCase);

            if (!isFigure && !isCaption) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            var spacing = props.SpacingBetweenLines ??= new SpacingBetweenLines();

            if (isFigure) {
                if (rules.FigureAlignment == "Center") props.Justification = new Justification { Val = JustificationValues.Center };
                if (rules.FigureRequireNoIndent) ind.FirstLine = "0";
                spacing.Before = figSpacingBeforeStr;
                spacing.After = figSpacingAfterStr;
            } 
            else if (isCaption) {
                if (rules.CaptionAlignment == "Center") props.Justification = new Justification { Val = JustificationValues.Center };
                if (rules.CaptionRequireNoIndent) ind.FirstLine = "0";
                spacing.Line = capLineSpacingStr;
                spacing.LineRule = LineSpacingRuleValues.Auto;
                spacing.Before = capSpacingBeforeStr;
                spacing.After = capSpacingAfterStr;
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

        foreach (var p in ctx.AllParagraphs().ToList()) {
            if (!ctx.HasText(p)) continue;
            string? styleId = ctx.ParagraphStyleId(p);
            bool isCodeStyle = styleId != null && (styleId.Contains("Code", StringComparison.OrdinalIgnoreCase) || styleId.Contains("Листинг", StringComparison.OrdinalIgnoreCase));
            
            var firstRun = p.Descendants<Run>().FirstOrDefault(r => r.Elements<Text>().Any(t => !string.IsNullOrWhiteSpace(t.Text)));
            bool isMonospace = firstRun != null && (ctx.FontOf(firstRun, styleId).Contains("Consolas") || ctx.FontOf(firstRun, styleId).Contains("Courier"));
            
            if (!isCodeStyle && !isMonospace) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            if (rules.CodeRequireNoIndent) ind.FirstLine = "0";

            var spacing = props.SpacingBetweenLines ??= new SpacingBetweenLines();
            spacing.Line = codeLineSpacingStr;
            spacing.LineRule = LineSpacingRuleValues.Auto;

            foreach (var run in p.Descendants<Run>()) {
                var rPr = run.RunProperties ??= new RunProperties();
                rPr.RunFonts = new RunFonts { Ascii = rules.CodeFontFamily, HighAnsi = rules.CodeFontFamily, ComplexScript = rules.CodeFontFamily };
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
        foreach (var p in ctx.AllParagraphs().ToList()) {
            if (!ctx.HasText(p)) continue;
            int? outlineLvl = p.ParagraphProperties?.OutlineLevel?.Val?.Value;
            string? styleId = ctx.ParagraphStyleId(p);
            bool isHeading = (outlineLvl != null && outlineLvl < 9) || 
                             (styleId != null && (styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase) || styleId.Contains("Заголовок", StringComparison.OrdinalIgnoreCase)));
            
            if (!isHeading) continue;

            var props = p.ParagraphProperties ??= new ParagraphProperties();
            var ind = props.Indentation ??= new Indentation();
            if (rules.HeadingRequireNoIndent) ind.FirstLine = "0";

            if (rules.HeadingRequireBold) {
                foreach (var run in p.Descendants<Run>()) {
                    var rPr = run.RunProperties ??= new RunProperties();
                    rPr.Bold = new Bold { Val = true };
                    rPr.BoldComplexScript = new BoldComplexScript { Val = true };
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
                        count++;
                    }
                    break;
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
