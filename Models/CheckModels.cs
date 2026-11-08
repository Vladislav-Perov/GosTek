/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модели данных аудита оформления по ГОСТ (RuleSet, CheckIssue, MarginRule, SectionRule, CustomRuleDto)
 * СОДЕРЖИМОЕ: IssueSeverity, IssueKind, CheckIssue, MarginRule, SectionRule, RuleSet, CustomRuleDto, RulePreset, RuleSets
 * ЗАВИСИМОСТИ: .NET BCL
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>Насколько замечание важное: красная точка («Важное») или жёлтая («Желательно»).</summary>
public enum IssueSeverity { Important, Warning }

/// <summary>
/// Что именно проверено. По типу замечания экран «Проверка» понимает, что исправилось,
/// а DocumentChecker.Fix знает, что менять в .docx.
/// </summary>
public enum IssueKind
{
    MarginLeft,
    MarginRight,
    MarginTop,
    MarginBottom,
    FontName,
    FontSize,
    LineSpacing,
    PageNumbers,
    MissingSection,
}

/// <summary>
/// Одно замечание к документу.
/// Title — что не так («Левое поле 2 см, по правилам нужно 3 см»), Where — где («Весь документ»),
/// CanFix — можно ли исправить автоматически (иначе на экране бейдж «Вручную»).
/// </summary>
public sealed record CheckIssue(IssueKind Kind, IssueSeverity Severity, string Title, string Where, bool CanFix);

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
        Description: dto.Description);
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
}

/// <summary>Кнопка-чип на экране «Проверка»: подпись и набор правил, который она включает.</summary>
public sealed record RulePreset(string Title, RuleSet Rules);

/// <summary>Наборы правил приложения.</summary>
public static class RuleSets
{
    /// <summary>
    /// Универсальные правила (по макету): поля 3 / 1 / 2 / 2 см, Times New Roman 14 pt,
    /// интервал 1,5, нумерация страниц и список литературы.
    /// Раздел с литературой требуется только от больших документов (от SectionsMinParagraphs абзацев),
    /// чтобы короткие заявления и бланки не получали ложное замечание.
    /// </summary>
    public static readonly RuleSet Universal = new(
        Id: "universal",
        Title: "Универсальные правила",
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

    public static readonly RuleSet StudyRules = Universal with {
        Id = "study_bgtu",
        Title = "Учебные работы, БГТУ",
        Category = "Учебная",
        Description = "Требования БГТУ к курсовым и дипломным проектам",
    };

    public static readonly RuleSet ApplicationRules = Universal with {
        Id = "applications",
        Title = "Заявления и бланки",
        Category = "Заявление",
        Description = "Требования к официальным заявлениям, служебным запискам и бланкам",
    };

    public static readonly RuleSet ContractRules = Universal with {
        Id = "contracts",
        Title = "Договоры",
        Category = "Договор",
        Description = "Требования к хозяйственным договорам и соглашениям",
    };

    /// <summary>
    /// Чипы на экране «Проверка».
    /// CODE-01: Пресет Universal намеренно используется как базовый государственный стандарт (ГОСТ)
    /// для всех категорий документов, обеспечивая единые общепринятые требования оформления.
    /// </summary>
    public static readonly IReadOnlyList<RulePreset> Presets = new[] {
        new RulePreset("Учебные работы, БГТУ", StudyRules),
        new RulePreset("Заявления и бланки", ApplicationRules),
        new RulePreset("Договоры", ContractRules),
    };
}
