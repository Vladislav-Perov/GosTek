/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модели данных аудита оформления по ГОСТ (RuleSet, CheckIssue, MarginRule, SectionRule, CustomRuleDto)
 * СОДЕРЖИМОЕ: IssueSeverity, IssueKind, CheckIssue, MarginRule, SectionRule, RuleSet, CustomRuleDto, RulePreset, RuleSets
 * ЗАВИСИМОСТИ: .NET BCL
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>Насколько замечание важное: красная точка («Важное») или жёлтая («Желательно»).</summary>
public enum IssueSeverity { Important, Warning, Moderate = Warning }

/// <summary>
/// Что именно проверено. По типу замечания экран «Проверка» понимает, что исправилось,
/// а DocumentChecker.Fix знает, что менять в .docx.
/// </summary>
public enum IssueKind
{
    MarginLeft, MarginRight, MarginTop, MarginBottom,
    FontName, FontSize, LineSpacing, PageNumbers, MissingSection,
    Alignment, FirstLineIndent,
    SpacingBeforeAfter, WidowControl, TableFormatting, FigureFormatting,
    CodeSnippetFormatting, HeadingFormatting
}

/// <summary>
/// Конкретный проблемный элемент документа (абзац, ячейка, заголовок, раздел).
/// Index — порядковый номер элемента в едином обходе документа;
/// Text — первые 60 символов с «…» при усечении;
/// Actual и Expected — реальные значения этого элемента.
/// </summary>
public sealed record IssueItem(int Index, string Text, string? Actual, string? Expected);

/// <summary>
/// Одно замечание к документу.
/// Title — что не так («Левое поле», «Размер шрифта»), Where — где («Затронуто абзацев: 12 из 40»),
/// CanFix — можно ли исправить автоматически (иначе на экране бейдж «Вручную»),
/// Actual — фактическое значение («2 см», «Arial»), Expected — ожидаемое по ГОСТ («3 см», «Times New Roman»),
/// Page — номер страницы (null, если без рендера не определить), Sample — первые 60 символов первого проблемного абзаца,
/// AffectedCount — число затронутых абзацев/элементов, Items — список конкретных адресуемых проблемных элементов.
/// </summary>
public sealed record CheckIssue(
    IssueKind Kind,
    IssueSeverity Severity,
    string Title,
    string Where,
    bool CanFix,
    string? Actual = null,
    string? Expected = null,
    int? Page = null,
    string? Sample = null,
    int? AffectedCount = null,
    IReadOnlyList<IssueItem>? Items = null)
{
    /// <summary>Показывать ли блок сравнения «Сейчас -> Будет» в карточке замечания.</summary>
    public bool HasDiff => !string.IsNullOrWhiteSpace(Actual) || !string.IsNullOrWhiteSpace(Expected);

    /// <summary>Требует ручной правки автором (нельзя применить автоматически).</summary>
    public bool CannotFix => !CanFix;

    /// <summary>Есть ли фрагмент текста проблемного абзаца для отображения курсивом.</summary>
    public bool HasSample => !string.IsNullOrWhiteSpace(Sample);
}

/// <summary>Требование к полю страницы. Exact = true: ровно столько; false: не меньше.</summary>
public sealed record MarginRule(double Cm, bool Exact);

/// <summary>
/// Обязательный раздел. Pattern — регулярное выражение для заголовка (без учёта регистра, «ё» = «е»).
/// Подходит любой из вариантов названия: «Список литературы», «Список источников» и т. д.
/// </summary>
public sealed record SectionRule(string Title, string Pattern);

/// <summary>
/// Набор правил оформления. Поддерживает встроенные пресеты и пользовательские конфигурации.
/// Включает поля страницы, шрифт, размер, межстрочный интервал, красную строку и выравнивание.
/// </summary>
public sealed record RuleSet(
    string Id,
    string Title,
    MarginRule Left,
    MarginRule Right,
    MarginRule Top,
    MarginRule Bottom,
    string FontName,
    double FontSizePt,
    double LineSpacing,
    bool RequirePageNumbers,
    IReadOnlyList<SectionRule> RequiredSections,
    int SectionsMinParagraphs,
    double FirstLineIndentCm = 1.25,
    string Alignment = "Justify",
    string? Category = null,
    bool IsCustom = false,
    string? Description = null)
{
    /// <summary>Допуск при сравнении полей, см (Word округляет значения).</summary>
    public double MarginToleranceCm { get; init; } = 0.1;

    /// <summary>Допуск при сравнении межстрочного интервала (в «строках»).</summary>
    public double SpacingTolerance { get; init; } = 0.05;

    /// <summary>Встроенное (неудаляемое) стандартное правило GOST.</summary>
    public bool IsBuiltIn { get; set; } = false;

    // --- Настройки рисунков (Figures) ---
    public string FigureAlignment { get; set; } = "Center";
    public double FigureSpacingBeforePt { get; set; } = 12.0;
    public double FigureSpacingAfterPt { get; set; } = 12.0;
    public bool FigureRequireNoIndent { get; set; } = true;

    // --- Настройки подписей к рисункам (Captions) ---
    public string CaptionAlignment { get; set; } = "Center";
    public double CaptionSpacingBeforePt { get; set; } = 0.0;
    public double CaptionSpacingAfterPt { get; set; } = 0.0;
    public double CaptionLineSpacing { get; set; } = 1.0;
    public bool CaptionRequireNoIndent { get; set; } = true;

    // --- Настройки таблиц (Tables) ---
    public bool CheckTables { get; set; } = true;
    public string TableFontFamily { get; set; } = "Times New Roman";
    public double TableFontSizePt { get; set; } = 12.0;
    public double TableLineSpacing { get; set; } = 1.0;
    public bool TableRequireNoIndent { get; set; } = true;

    // --- Настройки подписей к таблицам (Table Captions) ---
    public string TableCaptionAlignment { get; set; } = "Left";
    public double TableCaptionSpacingBeforePt { get; set; } = 6.0;
    public double TableCaptionSpacingAfterPt { get; set; } = 3.0;
    public double TableCaptionLineSpacing { get; set; } = 1.0;
    public bool TableCaptionRequireNoIndent { get; set; } = true;

    // --- Настройки листингов кода (Code Snippets) ---
    public string CodeFontFamily { get; set; } = "Consolas";
    public double CodeFontSizePt { get; set; } = 12.0;
    public double CodeLineSpacing { get; set; } = 1.0;
    public bool CodeRequireNoIndent { get; set; } = true;

    // --- Настройки заголовков (Headings) ---
    public string HeadingFontFamily { get; set; } = "Times New Roman";
    public double HeadingFontSizePt { get; set; } = 16.0;
    public double HeadingLineSpacing { get; set; } = 1.5;
    public string HeadingAlignment { get; set; } = "Left";
    public double HeadingSpacingBeforePt { get; set; } = 12.0;
    public double HeadingSpacingAfterPt { get; set; } = 6.0;
    public bool HeadingRequireBold { get; set; } = true;
    public bool HeadingRequireNoIndent { get; set; } = true;
    public bool HeadingRemoveTrailingDot { get; set; } = true;

    /// <summary>Преобразование модели в сериализуемый DTO для локального хранения.</summary>
    public CustomRuleDto ToDto() => new()
    {
        Id = Id,
        Title = Title,
        Description = Description,
        Category = Category,
        LeftMarginCm = Left.Cm,
        LeftMarginExact = Left.Exact,
        RightMarginCm = Right.Cm,
        RightMarginExact = Right.Exact,
        TopMarginCm = Top.Cm,
        TopMarginExact = Top.Exact,
        BottomMarginCm = Bottom.Cm,
        BottomMarginExact = Bottom.Exact,
        FontName = FontName,
        FontSizePt = FontSizePt,
        LineSpacing = LineSpacing,
        RequirePageNumbers = RequirePageNumbers,
        FirstLineIndentCm = FirstLineIndentCm,
        Alignment = Alignment,
        SectionsMinParagraphs = SectionsMinParagraphs,

        FigureAlignment = FigureAlignment,
        FigureSpacingBeforePt = FigureSpacingBeforePt,
        FigureSpacingAfterPt = FigureSpacingAfterPt,
        FigureRequireNoIndent = FigureRequireNoIndent,

        CaptionAlignment = CaptionAlignment,
        CaptionSpacingBeforePt = CaptionSpacingBeforePt,
        CaptionSpacingAfterPt = CaptionSpacingAfterPt,
        CaptionLineSpacing = CaptionLineSpacing,
        CaptionRequireNoIndent = CaptionRequireNoIndent,

        CheckTables = CheckTables,
        TableFontFamily = TableFontFamily,
        TableFontSizePt = TableFontSizePt,
        TableLineSpacing = TableLineSpacing,
        TableRequireNoIndent = TableRequireNoIndent,

        TableCaptionAlignment = TableCaptionAlignment,
        TableCaptionSpacingBeforePt = TableCaptionSpacingBeforePt,
        TableCaptionSpacingAfterPt = TableCaptionSpacingAfterPt,
        TableCaptionLineSpacing = TableCaptionLineSpacing,
        TableCaptionRequireNoIndent = TableCaptionRequireNoIndent,

        CodeFontFamily = CodeFontFamily,
        CodeFontSizePt = CodeFontSizePt,
        CodeLineSpacing = CodeLineSpacing,
        CodeRequireNoIndent = CodeRequireNoIndent,

        HeadingFontFamily = HeadingFontFamily,
        HeadingFontSizePt = HeadingFontSizePt,
        HeadingLineSpacing = HeadingLineSpacing,
        HeadingAlignment = HeadingAlignment,
        HeadingSpacingBeforePt = HeadingSpacingBeforePt,
        HeadingSpacingAfterPt = HeadingSpacingAfterPt,
        HeadingRequireBold = HeadingRequireBold,
        HeadingRequireNoIndent = HeadingRequireNoIndent,
        HeadingRemoveTrailingDot = HeadingRemoveTrailingDot,
    };

    /// <summary>Создание экземпляра RuleSet из DTO.</summary>
    public static RuleSet FromDto(CustomRuleDto dto) => new(
        Id: dto.Id,
        Title: dto.Title,
        Left: new MarginRule(dto.LeftMarginCm, dto.LeftMarginExact),
        Right: new MarginRule(dto.RightMarginCm, dto.RightMarginExact),
        Top: new MarginRule(dto.TopMarginCm, dto.TopMarginExact),
        Bottom: new MarginRule(dto.BottomMarginCm, dto.BottomMarginExact),
        FontName: dto.FontName,
        FontSizePt: dto.FontSizePt,
        LineSpacing: dto.LineSpacing,
        RequirePageNumbers: dto.RequirePageNumbers,
        RequiredSections: Array.Empty<SectionRule>(),
        SectionsMinParagraphs: dto.SectionsMinParagraphs,
        FirstLineIndentCm: dto.FirstLineIndentCm,
        Alignment: dto.Alignment,
        Category: dto.Category,
        IsCustom: true,
        Description: dto.Description)
    {
        FigureAlignment = dto.FigureAlignment,
        FigureSpacingBeforePt = dto.FigureSpacingBeforePt,
        FigureSpacingAfterPt = dto.FigureSpacingAfterPt,
        FigureRequireNoIndent = dto.FigureRequireNoIndent,

        CaptionAlignment = dto.CaptionAlignment,
        CaptionSpacingBeforePt = dto.CaptionSpacingBeforePt,
        CaptionSpacingAfterPt = dto.CaptionSpacingAfterPt,
        CaptionLineSpacing = dto.CaptionLineSpacing,
        CaptionRequireNoIndent = dto.CaptionRequireNoIndent,

        CheckTables = dto.CheckTables,
        TableFontFamily = dto.TableFontFamily,
        TableFontSizePt = dto.TableFontSizePt,
        TableLineSpacing = dto.TableLineSpacing,
        TableRequireNoIndent = dto.TableRequireNoIndent,

        TableCaptionAlignment = dto.TableCaptionAlignment,
        TableCaptionSpacingBeforePt = dto.TableCaptionSpacingBeforePt,
        TableCaptionSpacingAfterPt = dto.TableCaptionSpacingAfterPt,
        TableCaptionLineSpacing = dto.TableCaptionLineSpacing,
        TableCaptionRequireNoIndent = dto.TableCaptionRequireNoIndent,

        CodeFontFamily = dto.CodeFontFamily,
        CodeFontSizePt = dto.CodeFontSizePt,
        CodeLineSpacing = dto.CodeLineSpacing,
        CodeRequireNoIndent = dto.CodeRequireNoIndent,

        HeadingFontFamily = dto.HeadingFontFamily,
        HeadingFontSizePt = dto.HeadingFontSizePt,
        HeadingLineSpacing = dto.HeadingLineSpacing,
        HeadingAlignment = dto.HeadingAlignment,
        HeadingSpacingBeforePt = dto.HeadingSpacingBeforePt,
        HeadingSpacingAfterPt = dto.HeadingSpacingAfterPt,
        HeadingRequireBold = dto.HeadingRequireBold,
        HeadingRequireNoIndent = dto.HeadingRequireNoIndent,
        HeadingRemoveTrailingDot = dto.HeadingRemoveTrailingDot,
    };
}

/// <summary>DTO для сериализации правил оформления в формат JSON.</summary>
public sealed class CustomRuleDto
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? Category { get; set; }
    public double LeftMarginCm { get; set; } = 3.0;
    public bool LeftMarginExact { get; set; } = true;
    public double RightMarginCm { get; set; } = 1.0;
    public bool RightMarginExact { get; set; } = false;
    public double TopMarginCm { get; set; } = 2.0;
    public bool TopMarginExact { get; set; } = false;
    public double BottomMarginCm { get; set; } = 2.0;
    public bool BottomMarginExact { get; set; } = false;
    public string FontName { get; set; } = "Times New Roman";
    public double FontSizePt { get; set; } = 14.0;
    public double LineSpacing { get; set; } = 1.5;
    public bool RequirePageNumbers { get; set; } = true;
    public double FirstLineIndentCm { get; set; } = 1.25;
    public string Alignment { get; set; } = "Justify";
    public int SectionsMinParagraphs { get; set; } = 100;

    // --- Настройки рисунков (Figures) ---
    public string FigureAlignment { get; set; } = "Center";
    public double FigureSpacingBeforePt { get; set; } = 12.0;
    public double FigureSpacingAfterPt { get; set; } = 12.0;
    public bool FigureRequireNoIndent { get; set; } = true;

    // --- Настройки подписей к рисункам (Captions) ---
    public string CaptionAlignment { get; set; } = "Center";
    public double CaptionSpacingBeforePt { get; set; } = 0.0;
    public double CaptionSpacingAfterPt { get; set; } = 0.0;
    public double CaptionLineSpacing { get; set; } = 1.0;
    public bool CaptionRequireNoIndent { get; set; } = true;

    // --- Настройки таблиц (Tables) ---
    public bool CheckTables { get; set; } = true;
    public string TableFontFamily { get; set; } = "Times New Roman";
    public double TableFontSizePt { get; set; } = 12.0;
    public double TableLineSpacing { get; set; } = 1.0;
    public bool TableRequireNoIndent { get; set; } = true;

    // --- Настройки подписей к таблицам (Table Captions) ---
    public string TableCaptionAlignment { get; set; } = "Left";
    public double TableCaptionSpacingBeforePt { get; set; } = 6.0;
    public double TableCaptionSpacingAfterPt { get; set; } = 3.0;
    public double TableCaptionLineSpacing { get; set; } = 1.0;
    public bool TableCaptionRequireNoIndent { get; set; } = true;

    // --- Настройки листингов кода (Code Snippets) ---
    public string CodeFontFamily { get; set; } = "Consolas";
    public double CodeFontSizePt { get; set; } = 12.0;
    public double CodeLineSpacing { get; set; } = 1.0;
    public bool CodeRequireNoIndent { get; set; } = true;

    // --- Настройки заголовков (Headings) ---
    public string HeadingFontFamily { get; set; } = "Times New Roman";
    public double HeadingFontSizePt { get; set; } = 16.0;
    public double HeadingLineSpacing { get; set; } = 1.5;
    public string HeadingAlignment { get; set; } = "Left";
    public double HeadingSpacingBeforePt { get; set; } = 12.0;
    public double HeadingSpacingAfterPt { get; set; } = 6.0;
    public bool HeadingRequireBold { get; set; } = true;
    public bool HeadingRequireNoIndent { get; set; } = true;
    public bool HeadingRemoveTrailingDot { get; set; } = true;
}

/// <summary>Кнопка-чип на экране «Проверка»: подпись и набор правил, который она включает.</summary>
public sealed record RulePreset(string Title, RuleSet Rules);

/// <summary>Наборы правил приложения.</summary>
public static class RuleSets
{
    public const string MasterRuleId = "master-rule";

    /// <summary>
    /// Главное правило «Основной ГОСТ»: базовый государственный стандарт.
    /// Постоянный Id, всегда первое в списке, удалить нельзя.
    /// Правки сохраняются и переживают перезапуск приложения.
    /// </summary>
    public static readonly RuleSet MasterRule = new(
        Id: MasterRuleId,
        Title: "Основной ГОСТ",
        Left: new MarginRule(3.0, Exact: true),
        Right: new MarginRule(1.0, Exact: false),
        Top: new MarginRule(2.0, Exact: false),
        Bottom: new MarginRule(2.0, Exact: false),
        FontName: "Times New Roman",
        FontSizePt: 14,
        LineSpacing: 1.5,
        RequirePageNumbers: true,
        RequiredSections: new[] {
            new SectionRule("Список литературы",
                @"^(\d+[\.\s]*)?(список\s+(использованн\w+\s+)?(литератур\w*|источник\w*)|литература|библиографический\s+список|библиография)\s*[\.:]?$"),
        },
        SectionsMinParagraphs: 100,
        FirstLineIndentCm: 1.25,
        Alignment: "Justify",
        Category: "Универсальное",
        IsCustom: false,
        Description: "Стандартные требования ГОСТ 7.32 / ГОСТ 2.105");

    /// <summary>Алиас для обратной совместимости с существующим кодом и тестами.</summary>
    public static RuleSet Universal => MasterRule;

    /// <summary>
    /// Чипы на экране «Проверка». Содержит только главное правило «Основной ГОСТ».
    /// Готовых пресетов «Учебные работы», «Заявления и бланки», «Договоры» нет.
    /// </summary>
    public static readonly IReadOnlyList<RulePreset> Presets = new[] {
        new RulePreset("Основной ГОСТ", MasterRule),
    };
}
