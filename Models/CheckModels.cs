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
/// Набор правил оформления. Сейчас используется один экземпляр (RuleSets.Universal),
/// но проверка и исправление принимают RuleSet параметром: чтобы добавить правила для договоров
/// или заявлений, достаточно описать ещё один RuleSet и указать его в RuleSets.Presets.
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
    int SectionsMinParagraphs)
{
    /// <summary>Допуск при сравнении полей, см (Word округляет значения).</summary>
    public double MarginToleranceCm { get; init; } = 0.1;

    /// <summary>Допуск при сравнении межстрочного интервала (в «строках»).</summary>
    public double SpacingTolerance { get; init; } = 0.05;
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
        SectionsMinParagraphs: 100);

    /// <summary>
    /// Чипы на экране «Проверка».
    /// CODE-01: Пресет Universal намеренно используется как базовый государственный стандарт (ГОСТ)
    /// для всех категорий документов, обеспечивая единые общепринятые требования оформления.
    /// TODO: развести с Gost1, когда появятся отдельные формализованные требования для каждого пресета
    /// (учебные работы БГТУ, официальные заявления/бланки и хозяйственные договоры). Поведение не меняется.
    /// </summary>
    public static readonly IReadOnlyList<RulePreset> Presets = new[] {
        new RulePreset("Учебные работы, БГТУ", Universal),
        new RulePreset("Заявления и бланки", Universal),
        new RulePreset("Договоры", Universal),
    };
}
