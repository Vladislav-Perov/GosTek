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
public sealed record PreviewPara(List<PreviewPart> Parts, TextAlignment Align, bool Bold, bool Italic, double Pt, bool Blank);

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

    // [..] или {..}; внутри обязательно есть буква (чтобы не трогать сноски вида [1])
    private static readonly Regex Rx = new(@"\[([^\[\]]*\p{L}[^\[\]]*)\]|\{([^{}]*\p{L}[^{}]*)\}", RegexOptions.Compiled);
    private static readonly char[] Seps = { '-', '–', '—', ':', '|' };

    private static readonly string[] MultilineWords =
        { "текст", "описание", "причина", "содержание", "обстоятельств", "пояснение", "примечани", "комментари" };

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
                } catch {
                    // Битый или заблокированный файл пропускаем
                }
            }
        } catch {
            // Папка недоступна
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

    private static (string Label, string Hint) ParseToken(Match m)
    {
        var inner = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Trim();
        int i = inner.IndexOfAny(Seps);
        if (i <= 0)
            return (inner, "");
        return (inner[..i].Trim(), inner[(i + 1)..].Trim());
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

        using var ms = CopyToMemory(file);
        using var doc = WordprocessingDocument.Open(ms, false);

        foreach (var root in Roots(doc))
            foreach (var p in root.Descendants<Paragraph>()) {
                foreach (Match m in Rx.Matches(ParagraphText(p))) {
                    var (label, hint) = ParseToken(m);
                    var id = FieldId(label);
                    if (id.Length == 0)
                        continue;

                    var existing = list.FindIndex(f => f.Id == id);
                    if (existing >= 0) {
                        // Подсказка могла быть только в одном из вхождений
                        if (list[existing].Placeholder.Length == 0 && hint.Length > 0)
                            list[existing] = list[existing] with { Placeholder = hint };
                        continue;
                    }

                    list.Add(new TemplateField(id, label, hint, GuessSource(label), IsMultiline(label)));
                }
            }

        return list.ToArray();
    }

    private static bool IsMultiline(string label)
        => MultilineWords.Any(w => label.Contains(w, StringComparison.CurrentCultureIgnoreCase));

    // Что подставлять из профиля по названию поля
    private static ProfileSource GuessSource(string label)
    {
        var l = label.ToLowerInvariant();

        if (l.Contains("и.о") || l.Contains("и. о") || l.Contains("инициал"))
            return ProfileSource.ShortName;
        if (l.Contains("фио") || l.Contains("ф.и.о") || l.Contains("фамили"))
            return ProfileSource.FullName;
        if (l.Contains("паспорт"))
            return ProfileSource.Passport;
        if (l.Contains("адрес"))
            return ProfileSource.Address;

        return ProfileSource.None;
    }

    // ===================================================================
    //  ЗАПОЛНЕНИЕ .docx
    // ===================================================================

    /// <summary>Копирует шаблон в path и подставляет значения. Исходный шаблон не меняется.</summary>
    public static void FillTo(TemplateDef template, IReadOnlyDictionary<string, string> values, string path)
    {
        string Value(string label)
            => values.TryGetValue(FieldId(label), out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : Blank;

        using (var ms = CopyToMemory(template.FilePath)) {
            using (var doc = WordprocessingDocument.Open(ms, true)) {
                foreach (var root in Roots(doc))
                    foreach (var p in root.Descendants<Paragraph>().ToList())
                        FillParagraph(p, Value);

                doc.MainDocumentPart?.Document.Save();
                foreach (var h in doc.MainDocumentPart?.HeaderParts ?? Enumerable.Empty<HeaderPart>())
                    h.Header.Save();
                foreach (var f in doc.MainDocumentPart?.FooterParts ?? Enumerable.Empty<FooterPart>())
                    f.Footer.Save();
            }

            File.WriteAllBytes(path, ms.ToArray());
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

    public static List<PreviewPara> ReadPreview(string file)
    {
        var result = new List<PreviewPara>();

        try {
            using var ms = CopyToMemory(file);
            using var doc = WordprocessingDocument.Open(ms, false);
            var body = doc.MainDocumentPart?.Document.Body;
            if (body is null)
                return result;

            foreach (var p in body.Descendants<Paragraph>().Take(300)) {
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
        } catch {
            // Предпросмотр не получится, но форма и сохранение продолжат работать
        }

        return result;
    }

    // ===================================================================
    //  ПОМОЩНИКИ
    // ===================================================================

    // Читаем с FileShare.ReadWrite: шаблон может быть открыт в Word
    private static MemoryStream CopyToMemory(string file)
    {
        var ms = new MemoryStream();
        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            fs.CopyTo(ms);
        ms.Position = 0;
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
            yield return h.Header;
        foreach (var f in main.FooterParts)
            yield return f.Footer;
    }

    // Text именно этого абзаца (без вложенных, например из надписей)
    private static List<Text> OwnTexts(Paragraph p)
        => p.Descendants<Text>().Where(t => t.Ancestors<Paragraph>().First() == p).ToList();

    private static string ParagraphText(Paragraph p) => string.Concat(OwnTexts(p).Select(t => t.Text ?? ""));
}
