/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сканирование DOCX-шаблонов, извлечение токенов, генерация структуры предпросмотра и заполнение
 * СОДЕРЖИМОЕ: TemplateEngine, PreviewPart, PreviewPara
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models
 * ==============================================================================
 */
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;

// В MAUI есть одноимённый TextAlignment
using TextAlignment = Microsoft.Maui.TextAlignment;

namespace GosTek;

/// <summary>Кусок абзаца для предпросмотра: обычный текст (FieldId == null) или поле.</summary>
public sealed record PreviewPart(string Text, string? FieldId);

/// <summary>Абзац шаблона для предпросмотра.</summary>
public sealed record PreviewPara(List<PreviewPart> Parts, TextAlignment Align, bool Bold, bool Italic, double Pt, bool Blank)
{
    public double LeftIndentMm { get; init; }
    public double FirstLineMm { get; init; }
    public double? LineSpacing { get; init; }
    public bool Justify { get; init; }
}

/// <summary>
/// Работа с шаблонами .docx.
///
/// ИМЯ ФАЙЛА:   Категория_Название.docx      (первый «_»)
///              Категория - Название.docx    (если «_» нет, то « - »)
///              Без разделителя: категория = имя подпапки, а в корне папки = «Прочее».
///
/// ПОЛЯ В ТЕКСТЕ:  [Название поля-подсказка]  или  {Название поля-подсказка}
///              Например [Дата создания-00.00.0000]: «Дата создания» — подпись поля,
///              «00.00.0000» — серый пример в форме. Разделитель: первый из - – — : |
///              (поэтому дефис внутри самой подписи ставить нельзя). Подсказка необязательна.
///              Одно и то же название в нескольких местах = одно поле, значение подставится везде.
/// </summary>
public static class TemplateEngine
{
    private const string Blank = "__________";

    // [..], {{..}} или {..}; внутри обязательно есть буква (чтобы не трогать сноски вида [1])
    private static readonly Regex Rx = new(@"\[([^\[\]]*\p{L}[^\[\]]*)\]|\{\{([^{}]*\p{L}[^{}]*)\}\}|\{([^{}]*\p{L}[^{}]*)\}", RegexOptions.Compiled);
    private static readonly char[] Seps = { '-', '–', '—', ':', '|' };

    private static readonly string[] MultilineWords =
        { "текст", "описание", "причина", "содержание", "обстоятельств", "пояснение", "примечани", "комментари", "reason", "description", "details", "график", "schedule" };

    // Кэш полей: чтобы не открывать неизменившиеся файлы при каждом обновлении списка
    private static readonly Dictionary<string, (DateTime Time, long Size, TemplateField[] Fields)> Cache = new();

    // ===================================================================
    //  СКАНИРОВАНИЕ ПАПКИ
    // ===================================================================

    public static List<TemplateDef> Scan(string folder)
    {
        var result = new List<TemplateDef>();

        try {
            if (!Directory.Exists(folder)) {
                if (SettingsService.IsDefaultTemplatesFolder)
                    Directory.CreateDirectory(folder);
                else
                    return result;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.docx", SearchOption.AllDirectories)) {
                var name = Path.GetFileName(file);
                if (name.StartsWith("~$"))   // временный файл Word
                    continue;

                try {
                    var fields = ReadFieldsCached(file);
                    var sub = Path.GetRelativePath(folder, Path.GetDirectoryName(file)!);
                    var (category, title) = ParseName(Path.GetFileNameWithoutExtension(file), sub == "." ? null : sub);
                    var rel = Path.GetRelativePath(folder, file).ToLowerInvariant();
                    var id = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(rel)))[..10];

                    result.Add(new TemplateDef(id, title, category, file, fields));
                } catch (Exception ex) {
                    // Битый или заблокированный файл пропускаем
                    System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
                }
            }
        } catch (Exception ex) {
            // Папка недоступна
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
        }

        return result
            .OrderBy(t => t.Category, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public static (string Category, string Title) ParseName(string stem, string? subfolder)
    {
        int i = stem.IndexOf('_');
        int len = 1;
        if (i < 0) {
            i = stem.IndexOf(" - ", StringComparison.Ordinal);
            len = 3;
        }

        if (i > 0 && i + len < stem.Length)
            return (stem[..i].Trim(), stem[(i + len)..].Trim());

        return (string.IsNullOrWhiteSpace(subfolder) ? TemplateCatalog.NoCategory : subfolder.Split(Path.DirectorySeparatorChar)[0],
                stem.Trim());
    }

    // ===================================================================
    //  ПОЛЯ
    // ===================================================================

    public static string FieldId(string label) => Regex.Replace(label.Trim(), @"\s+", " ").ToLowerInvariant();

    /// <summary>
    /// Разбирает токен поля на Label, Hint и режим детализации (например, [Поле|расписать] или [Поле|кратко]).
    /// </summary>
    public static (string Label, string Hint, FieldExpansionMode Mode) ParseTokenDetails(string inner)
    {
        if (string.IsNullOrWhiteSpace(inner))
            return ("", "", FieldExpansionMode.Normal);

        inner = inner.Trim();
        var mode = FieldExpansionMode.Normal;

        // Распознавание директивы режима через разделитель '|'
        int pipeIdx = inner.IndexOf('|');
        if (pipeIdx >= 0)
        {
            var beforePipe = inner[..pipeIdx].Trim();
            var afterPipe = inner[(pipeIdx + 1)..].Trim();

            if (afterPipe.Equals("расписать", StringComparison.OrdinalIgnoreCase) ||
                afterPipe.Equals("развернуть", StringComparison.OrdinalIgnoreCase) ||
                afterPipe.Equals("подробно", StringComparison.OrdinalIgnoreCase))
            {
                mode = FieldExpansionMode.Elaborate;
                inner = beforePipe;
            }
            else if (afterPipe.Equals("кратко", StringComparison.OrdinalIgnoreCase) ||
                     afterPipe.Equals("коротко", StringComparison.OrdinalIgnoreCase))
            {
                mode = FieldExpansionMode.Brief;
                inner = beforePipe;
            }
            else if (beforePipe.Equals("расписать", StringComparison.OrdinalIgnoreCase) ||
                     beforePipe.Equals("развернуть", StringComparison.OrdinalIgnoreCase) ||
                     beforePipe.Equals("подробно", StringComparison.OrdinalIgnoreCase))
            {
                mode = FieldExpansionMode.Elaborate;
                inner = afterPipe;
            }
            else if (beforePipe.Equals("кратко", StringComparison.OrdinalIgnoreCase) ||
                     beforePipe.Equals("коротко", StringComparison.OrdinalIgnoreCase))
            {
                mode = FieldExpansionMode.Brief;
                inner = afterPipe;
            }
        }

        int i = inner.IndexOfAny(Seps);
        if (i <= 0)
            return (inner, "", mode);

        return (inner[..i].Trim(), inner[(i + 1)..].Trim(), mode);
    }

    private static (string Label, string Hint, FieldExpansionMode Mode) ParseToken(Match m)
    {
        var inner = (m.Groups[1].Success ? m.Groups[1].Value :
                     m.Groups[2].Success ? m.Groups[2].Value :
                     m.Groups[3].Value).Trim();
        return ParseTokenDetails(inner);
    }

    private static TemplateField[] ReadFieldsCached(string file)
    {
        var info = new FileInfo(file);
        if (Cache.TryGetValue(file, out var c) && c.Time == info.LastWriteTimeUtc && c.Size == info.Length)
            return c.Fields;

        var fields = ReadFields(file);
        Cache[file] = (info.LastWriteTimeUtc, info.Length, fields);
        return fields;
    }

    private static TemplateField[] ReadFields(string file)
    {
        var list = new List<TemplateField>();
        var isBuiltInExplanation = string.Equals(Path.GetFileName(file),
            Services.TemplateSeeder.ExplanationTargetName, StringComparison.OrdinalIgnoreCase);

        using var ms = CopyToMemory(file);
        using var doc = WordprocessingDocument.Open(ms, false);

        foreach (var root in Roots(doc))
            foreach (var p in root.Descendants<Paragraph>()) {
                foreach (Match m in Rx.Matches(ParagraphText(p))) {
                    var (label, hint, mode) = ParseToken(m);
                    var id = FieldId(label);
                    if (id.Length == 0)
                        continue;

                    var explicitText = m.Groups[3].Success &&
                        string.Equals(m.Groups[3].Value.Trim(), "Текст", StringComparison.OrdinalIgnoreCase);
                    var legacyExplanationText = isBuiltInExplanation && id == "текст объяснения обстоятельств";
                    TextGenerationKind? generation = legacyExplanationText || (explicitText && isBuiltInExplanation)
                        ? TextGenerationKind.Explanation
                        : explicitText ? TextGenerationKind.DocumentText : null;

                    var existing = list.FindIndex(f => f.Id == id);
                    if (existing >= 0) {
                        // Подсказка могла быть только в одном из вхождений
                        if (list[existing].Placeholder.Length == 0 && hint.Length > 0)
                            list[existing] = list[existing] with { Placeholder = hint };
                        if (list[existing].ExpansionMode == FieldExpansionMode.Normal && mode != FieldExpansionMode.Normal)
                            list[existing] = list[existing] with {
                                ExpansionMode = mode,
                                Multiline = mode == FieldExpansionMode.Elaborate || list[existing].Multiline
                            };
                        if (generation is not null)
                            list[existing] = list[existing] with { TextGeneration = generation, Multiline = true };
                        continue;
                    }

                    bool multiline = mode == FieldExpansionMode.Elaborate || IsMultiline(label);
                    list.Add(new TemplateField(id, label, hint, GuessSource(label), multiline || generation is not null, mode, generation));
                }
            }

        return list.ToArray();
    }

    private static bool IsMultiline(string label)
        => MultilineWords.Any(w => label.Contains(w, StringComparison.CurrentCultureIgnoreCase));

    // Что подставлять из профиля по названию поля
    public static ProfileSource GuessSource(string label)
    {
        var l = label.ToLowerInvariant();

        if (l.Contains("и.о") || l.Contains("и. о") || l.Contains("инициал") || l == "shortname")
            return ProfileSource.ShortName;
        if (l.Contains("фио") || l.Contains("ф.и.о") || l.Contains("фамили") || l.Contains("fullname") || l == "name")
            return ProfileSource.FullName;
        if (l.Contains("паспорт") || l.Contains("passport"))
            return ProfileSource.Passport;
        if (l.Contains("адрес") || l.Contains("address"))
            return ProfileSource.Address;

        return ProfileSource.None;
    }

    // ===================================================================
    /// <summary>
    /// Автоподстановка значения поля из профиля: базовые реквизиты или CustomFields.
    /// Если в шаблоне встречается поле [Название] или {{Field}} и его нет в базовом профиле, но есть в CustomFields — подставляется.
    /// </summary>
    public static string? ResolveFromProfile(string label, ProfileData profile)
    {
        var l = label.Trim().ToLowerInvariant();

        if (l.Contains("и.о") || l.Contains("и. о") || l.Contains("инициал") || l == "shortname")
            return TemplateCatalog.ShortName(profile.FullName);
        if (l.Contains("фио") || l.Contains("ф.и.о") || l.Contains("фамилия") || l.Contains("фамили") || l.Contains("fullname") || l == "name")
            return profile.FullName;
        if (l.Contains("наименование") || l.Contains("организаци") || l.Contains("предприяти") || l.Contains("компани") || l.Contains("вуз") || l.Contains("университет") || l.Contains("учреждение") || l == "university")
            return profile.OrgName;
        if (l.Contains("унп") || l.Contains("инн"))
            return profile.Unp;
        if (l.Contains("адрес") || l.Contains("address") || l == "homeaddress")
            return profile.Address;
        if (l.Contains("телефон") || l.Contains("тел.") || l.Contains("phone"))
            return profile.Phone;
        if (l.Contains("паспорт") || l.Contains("passport"))
            return profile.Passport;
        if (l.Contains("счет") || l.Contains("счёт") || l.Contains("iban") || l.Contains("расчетн") || l.Contains("расчётн"))
            return profile.Account;

        return ResolveCustomField(label, profile.CustomFields);
    }

    /// <summary>
    /// Поиск кастомного реквизита в CustomFields (нечувствительно к регистру и пробелам).
    /// </summary>
    public static string? ResolveCustomField(string label, IReadOnlyDictionary<string, string>? customFields)
    {
        if (customFields is null || customFields.Count == 0)
            return null;

        var clean = label.Trim();
        if (customFields.TryGetValue(clean, out var direct) && !string.IsNullOrWhiteSpace(direct))
            return direct.Trim();

        foreach (var (k, v) in customFields)
        {
            if (string.Equals(k.Trim(), clean, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
        }

        return null;
    }

    //  ЗАПОЛНЕНИЕ .docx
    // ===================================================================

    /// <summary>Копирует шаблон в path и подставляет значения. Исходный шаблон не меняется.</summary>
    public static void FillTo(TemplateDef template, IReadOnlyDictionary<string, string> values, string path,
        DocumentFormatting? formatting = null)
    {
        if (string.Equals(Path.GetFullPath(template.FilePath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Выберите другое имя для готового документа. Этот путь ведёт к исходному шаблону.");

        string Value(string label)
        {
            var id = FieldId(label);
            if (values.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            if (values.TryGetValue(label.Trim(), out var direct) && !string.IsNullOrWhiteSpace(direct))
                return direct.Trim();
            return Blank;
        }

        using (var ms = CopyToMemory(template.FilePath)) {
            using (var doc = WordprocessingDocument.Open(ms, true)) {
                foreach (var root in Roots(doc))
                    foreach (var p in root.Descendants<Paragraph>().ToList())
                        FillParagraph(p, Value);

                if (formatting is not null && doc.MainDocumentPart?.Document?.Body is { } body)
                    ApplyFormatting(body, formatting);

                doc.MainDocumentPart?.Document?.Save();
                foreach (var h in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                    h.Header?.Save();
                foreach (var f in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                    f.Footer?.Save();
            }

            File.WriteAllBytes(path, ms.ToArray());
        }
    }

    private static int Twips(double millimetres) => (int)Math.Round(millimetres * 1440 / 25.4);

    private static void ApplyFormatting(Body body, DocumentFormatting options)
    {
        if (options.LeftMm.HasValue || options.RightMm.HasValue || options.TopMm.HasValue || options.BottomMm.HasValue)
        {
            if (body.GetFirstChild<SectionProperties>() is null)
                body.AppendChild(new SectionProperties());

            foreach (var section in body.Descendants<SectionProperties>())
            {
                var margin = section.GetFirstChild<PageMargin>();
                if (margin is null)
                {
                    margin = new PageMargin { Left = 1701, Right = 850, Top = 1134, Bottom = 1134, Header = 720, Footer = 720, Gutter = 0 };
                    section.AddChild(margin);
                }
                if (options.LeftMm is { } left) margin.Left = (uint)Twips(left);
                if (options.RightMm is { } right) margin.Right = (uint)Twips(right);
                if (options.TopMm is { } top) margin.Top = Twips(top);
                if (options.BottomMm is { } bottom) margin.Bottom = Twips(bottom);
            }
        }

        foreach (var paragraph in body.Descendants<Paragraph>())
        {
            if (options.Alignment.HasValue || options.LineSpacing.HasValue || options.FirstLineMm.HasValue)
            {
                var props = paragraph.ParagraphProperties ??= new ParagraphProperties();
                if (options.Alignment is { } align)
                    props.Justification = new Justification
                    {
                        Val = align == TextAlignment.Center ? JustificationValues.Center
                            : align == TextAlignment.End ? JustificationValues.Right : JustificationValues.Left
                    };
                if (options.LineSpacing is { } line)
                {
                    var spacing = props.SpacingBetweenLines ??= new SpacingBetweenLines();
                    spacing.Line = ((int)Math.Round(line * 240)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    spacing.LineRule = LineSpacingRuleValues.Auto;
                }
                if (options.FirstLineMm is { } first)
                {
                    var indent = props.Indentation ??= new Indentation();
                    indent.Hanging = null;
                    indent.HangingChars = null;
                    indent.FirstLineChars = null;
                    indent.FirstLine = Twips(first).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            }

            if (options.FontFamily is null && !options.FontSize.HasValue)
                continue;

            foreach (var run in paragraph.Descendants<Run>().Where(run => run.Ancestors<Paragraph>().First() == paragraph))
            {
                var props = run.RunProperties ??= new RunProperties();
                if (options.FontFamily is { } font)
                    props.RunFonts = new RunFonts { Ascii = font, HighAnsi = font, EastAsia = font, ComplexScript = font };
                if (options.FontSize is { } size)
                {
                    var halfPoints = ((int)Math.Round(size * 2)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    props.FontSize = new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = halfPoints };
                    props.FontSizeComplexScript = new FontSizeComplexScript { Val = halfPoints };
                }
            }
        }
    }

    // Подстановка в абзаце. Word часто режет «[Поле]» на несколько Run, поэтому работаем по символам:
    // значение кладём в первый затронутый Text, из остальных вырезаем свою часть. Форматирование сохраняется.
    private static void FillParagraph(Paragraph p, Func<string, string> value)
    {
        var texts = OwnTexts(p);
        if (texts.Count == 0)
            return;

        var full = string.Concat(texts.Select(t => t.Text ?? ""));
        var matches = Rx.Matches(full);
        if (matches.Count == 0)
            return;

        var starts = new int[texts.Count];
        var lens = new int[texts.Count];
        int pos = 0;
        for (int i = 0; i < texts.Count; i++) {
            starts[i] = pos;
            lens[i] = (texts[i].Text ?? "").Length;
            pos += lens[i];
        }

        // С конца к началу: смещения более ранних вхождений не сдвигаются
        for (int k = matches.Count - 1; k >= 0; k--) {
            var m = matches[k];
            int s = m.Index, e = m.Index + m.Length;
            var lines = value(ParseToken(m).Label).Replace("\r", "").Split('\n');

            int first = -1, last = -1;
            for (int i = 0; i < texts.Count; i++)
                if (lens[i] > 0 && starts[i] < e && starts[i] + lens[i] > s) {
                    if (first < 0) first = i;
                    last = i;
                }
            if (first < 0)
                continue;

            for (int i = first; i <= last; i++) {
                var t = texts[i];
                var cur = t.Text ?? "";
                int ls = Math.Max(s - starts[i], 0);
                int le = Math.Min(e - starts[i], lens[i]);
                t.Space = SpaceProcessingModeValues.Preserve;

                if (i == first) {
                    var pre = cur[..ls];
                    var tail = i == last ? cur[le..] : "";

                    if (lines.Length == 1) {
                        t.Text = pre + lines[0] + tail;
                    } else {
                        t.Text = pre + lines[0];
                        OpenXmlElement anchor = t;
                        for (int j = 1; j < lines.Length; j++) {
                            var br = new Break();
                            anchor.InsertAfterSelf(br);
                            var nt = new Text(j == lines.Length - 1 ? lines[j] + tail : lines[j]) { Space = SpaceProcessingModeValues.Preserve };
                            br.InsertAfterSelf(nt);
                            anchor = nt;
                        }
                    }
                } else {
                    t.Text = i == last ? cur[le..] : "";
                }
            }
        }
    }

    // ===================================================================
    //  ПРЕДПРОСМОТР
    // ===================================================================

    public static (DocumentFormatting Values, string? AlignmentLabel) ReadFormatting(string file)
    {
        static double? Number(string? value) => double.TryParse(value,
            System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

        static double? Millimetres(double? twips) => twips * 25.4 / 1440;

        using var stream = CopyToMemory(file);
        using var document = WordprocessingDocument.Open(stream, false);
        var main = document.MainDocumentPart;
        var body = main?.Document?.Body;
        var styles = main?.StyleDefinitionsPart?.Styles;
        var defaults = styles?.GetFirstChild<DocDefaults>();
        var margin = body?.Descendants<SectionProperties>().FirstOrDefault()?.GetFirstChild<PageMargin>();
        var paragraph = body?.Descendants<Paragraph>().FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.InnerText));
        var run = paragraph?.Descendants<Run>().FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.InnerText));

        IEnumerable<DocumentFormat.OpenXml.Wordprocessing.Style> StyleChain(string? id)
        {
            var visited = new HashSet<string>();
            while (id is not null && visited.Add(id))
            {
                var style = styles?.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().FirstOrDefault(s => s.StyleId?.Value == id);
                if (style is null)
                    yield break;
                yield return style;
                id = style.BasedOn?.Val?.Value;
            }
        }

        var defaultStyle = styles?.Elements<DocumentFormat.OpenXml.Wordprocessing.Style>().FirstOrDefault(s =>
            s.Type?.Value == StyleValues.Paragraph && s.Default?.Value == true);
        var paragraphStyles = StyleChain(paragraph?.ParagraphProperties?.ParagraphStyleId?.Val?.Value
            ?? defaultStyle?.StyleId?.Value).ToArray();
        var paragraphProperties = new OpenXmlElement?[] { paragraph?.ParagraphProperties }
            .Concat(paragraphStyles.Select(s => s.StyleParagraphProperties))
            .Append(defaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle).OfType<OpenXmlElement>().ToArray();
        var runProperties = new OpenXmlElement?[] { run?.RunProperties, paragraph?.ParagraphProperties?.ParagraphMarkRunProperties }
            .Concat(StyleChain(run?.RunProperties?.RunStyle?.Val?.Value).Select(s => s.StyleRunProperties))
            .Concat(paragraphStyles.Select(s => s.StyleRunProperties))
            .Append(defaults?.RunPropertiesDefault?.RunPropertiesBaseStyle).OfType<OpenXmlElement>().ToArray();

        var fontScheme = main?.ThemePart?.Theme?.ThemeElements?.FontScheme;
        string? FontName(RunFonts? fonts)
        {
            var theme = (fonts?.AsciiTheme?.Value ?? fonts?.HighAnsiTheme?.Value)?.ToString();
            var themedName = theme?.StartsWith("major", StringComparison.OrdinalIgnoreCase) == true
                ? fontScheme?.MajorFont?.GetFirstChild<DocumentFormat.OpenXml.Drawing.LatinFont>()?.Typeface?.Value
                : theme?.StartsWith("minor", StringComparison.OrdinalIgnoreCase) == true
                    ? fontScheme?.MinorFont?.GetFirstChild<DocumentFormat.OpenXml.Drawing.LatinFont>()?.Typeface?.Value
                    : null;
            return themedName ?? fonts?.Ascii?.Value ?? fonts?.HighAnsi?.Value;
        }

        var font = runProperties.Select(p => FontName(p.GetFirstChild<RunFonts>()))
            .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name));
        var size = runProperties.Select(p => Number(p.GetFirstChild<DocumentFormat.OpenXml.Wordprocessing.FontSize>()?.Val?.Value))
            .FirstOrDefault(n => n.HasValue) / 2;
        var spacing = paragraphProperties.Select(p => p.GetFirstChild<SpacingBetweenLines>())
            .FirstOrDefault(s => s?.Line is not null);
        double? line = spacing?.LineRule?.Value is { } rule && rule != LineSpacingRuleValues.Auto
            ? null : Number(spacing?.Line?.Value) / 240 ?? 1;
        var indent = paragraphProperties.Select(p => p.GetFirstChild<Indentation>())
            .FirstOrDefault(i => i?.FirstLine is not null || i?.Hanging is not null);
        var alignment = paragraphProperties.Select(p => p.GetFirstChild<Justification>()?.Val?.Value)
            .FirstOrDefault(a => a.HasValue);
        var align = alignment == JustificationValues.Center ? TextAlignment.Center
            : alignment == JustificationValues.Right ? TextAlignment.End : TextAlignment.Start;
        var alignmentLabel = alignment == JustificationValues.Both ? "По ширине" : null;

        return (new DocumentFormatting(
            Millimetres(margin?.Left?.Value), Millimetres(margin?.Right?.Value),
            Millimetres(margin?.Top?.Value), Millimetres(margin?.Bottom?.Value),
            font, size, line, Millimetres(Number(indent?.FirstLine?.Value) ?? -Number(indent?.Hanging?.Value)) ?? 0, align),
            alignmentLabel);
    }

    public static List<PreviewPara> ReadPreview(string file, bool literalLayout = false)
    {
        var result = new List<PreviewPara>();

        try {
            using var ms = CopyToMemory(file);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document?.Body;
            if (body is null)
                return result;

            var paragraphs = body.Descendants<Paragraph>();
            foreach (var p in literalLayout ? paragraphs : paragraphs.Take(300)) {
                var sb = new StringBuilder();
                foreach (var el in p.Descendants()) {
                    if (el.Ancestors<Paragraph>().First() != p)
                        continue;
                    if (el is Text t) sb.Append(t.Text);
                    else if (el is Break) sb.Append('\n');
                    else if (el is TabChar) sb.Append("    ");
                }

                var text = sb.ToString();
                var props = p.ParagraphProperties;
                var jc = props?.Justification?.Val?.Value;
                var align = jc == JustificationValues.Center ? TextAlignment.Center
                          : jc == JustificationValues.Right ? TextAlignment.End
                          : TextAlignment.Start;

                var run = p.Descendants<Run>().FirstOrDefault(r => r.Descendants<Text>().Any());
                var rp = run?.RunProperties;
                bool bold = rp?.Bold is { } b && (b.Val is null || b.Val.Value);
                bool italic = rp?.Italic is { } it && (it.Val is null || it.Val.Value);
                double pt = double.TryParse(rp?.FontSize?.Val?.Value, out var half) ? half / 2 : 14;

                if (string.IsNullOrWhiteSpace(text)) {
                    result.Add(new PreviewPara(new(), align, bold, italic, pt, true));
                    continue;
                }

                if (literalLayout)
                {
                    static double Parse(string? value) => double.TryParse(value, out var number) ? number : 0;
                    var line = Parse(props?.SpacingBetweenLines?.Line?.Value);
                    result.Add(new PreviewPara([new PreviewPart(text, null)], align, bold, italic, pt, false)
                    {
                        LeftIndentMm = Math.Max(0, Parse(props?.Indentation?.Left?.Value) * 25.4 / 1440),
                        FirstLineMm = Math.Max(0, Parse(props?.Indentation?.FirstLine?.Value) * 25.4 / 1440),
                        LineSpacing = line > 0 ? line / 240 : null,
                        Justify = jc == JustificationValues.Both,
                    });
                    continue;
                }

                var parts = new List<PreviewPart>();
                int last = 0;
                foreach (Match m in Rx.Matches(text)) {
                    if (m.Index > last)
                        parts.Add(new PreviewPart(text[last..m.Index], null));
                    parts.Add(new PreviewPart(m.Value, FieldId(ParseToken(m).Label)));
                    last = m.Index + m.Length;
                }
                if (last < text.Length)
                    parts.Add(new PreviewPart(text[last..], null));

                result.Add(new PreviewPara(parts, align, bold, italic, pt, false));
            }
        } catch (Exception ex) {
            // Предпросмотр не получится, но форма и сохранение продолжат работать
            System.Diagnostics.Debug.WriteLine("Ошибка: " + ex.Message);
        }

        return result;
    }

    // ===================================================================
    //  ПОМОЩНИКИ
    // ===================================================================

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

    // Читаем с FileShare.ReadWrite: шаблон может быть открыт в Word
    private static MemoryStream CopyToMemory(string file)
    {
        var ms = new MemoryStream();
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            fs.CopyTo(ms);
        ms.Position = 0;
        ValidateZipArchive(ms);
        return ms;
    }

    // Тело документа, колонтитулы
    private static IEnumerable<OpenXmlElement> Roots(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart;
        if (main is null)
            yield break;

        if (main.Document?.Body is { } body)
            yield return body;
        foreach (var h in main.HeaderParts)
            if (h.Header is { } header)
                yield return header;
        foreach (var f in main.FooterParts)
            if (f.Footer is { } footer)
                yield return footer;
    }

    // Text именно этого абзаца (без вложенных, например из надписей)
    private static List<Text> OwnTexts(Paragraph p)
        => p.Descendants<Text>().Where(t => t.Ancestors<Paragraph>().First() == p).ToList();

    private static string ParagraphText(Paragraph p) => string.Concat(OwnTexts(p).Select(t => t.Text ?? ""));
}
