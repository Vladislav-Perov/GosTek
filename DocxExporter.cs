using Android.Hardware;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;
using Microsoft.Maui.Controls.Platform;
using System;
using System.Collections.Generic;
using System.Text;
using static Android.Icu.Util.LocaleData;
using static System.Net.Mime.MediaTypeNames;

namespace GosTek;

/// <summary>Собирает .docx по шаблону и введённым значениям (тот же вид, что в предпросмотре).</summary>
public static class DocxExporter
{
    private const string FontName = "Times New Roman";
    private const int HalfPoints = 28;          // 14 pt (размер задаётся в половинках пункта)
    private const int RightBlockIndent = 4820;  // блок «Кому / от кого»: ~8,5 см от левого поля
    private const int TextWidth = 9638;         // ширина текста между полями (twips)
    private const int FirstLineIndent = 709;    // абзацный отступ 1,25 см
    private const int OneAndHalf = 360;         // полуторный интервал

    public static void Save(TemplateDef template, IReadOnlyDictionary<string, string> values, string path)
    {
        // Пустое значение превращаем в линию для ручного заполнения
        string V(string id) =>
            values.TryGetValue(id, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : "__________";

        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = doc.AddMainDocumentPart();
        var body = new Body();
        main.Document = new Document(body);

        switch (template.Id) {
            case "explanatory": {
                    body.Append(P($"{V("pos")}\n{V("org")}\nот {V("fio")}",
                        JustificationValues.Left, left: RightBlockIndent, after: 360));
                    body.Append(P("Объяснительная записка", JustificationValues.Center, bold: true, after: 120));
                    body.Append(P(V("subject"), JustificationValues.Center, italic: true, after: 240));

                    values.TryGetValue("text", out var text);
                    var paragraphs = (text ?? "").Split(new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    if (paragraphs.Length == 0)
                        body.Append(P("__________", JustificationValues.Both, firstLine: FirstLineIndent, line: OneAndHalf));
                    else
                        foreach (var p in paragraphs)
                            body.Append(P(p, JustificationValues.Both, firstLine: FirstLineIndent, line: OneAndHalf));

                    body.Append(Signature(values));
                    break;
                }

            case "ip": {
                    body.Append(P($"{V("organ")}\nот {V("fio")}\nпаспорт {V("passport")}",
                        JustificationValues.Left, left: RightBlockIndent, after: 360));
                    body.Append(P("ЗАЯВЛЕНИЕ", JustificationValues.Center, bold: true, after: 240));
                    body.Append(P(
                        "Прошу зарегистрировать меня в качестве индивидуального предпринимателя. " +
                        $"Адрес места жительства: {V("addr")}. Предполагаемый вид деятельности: {V("activity")}.",
                        JustificationValues.Both, firstLine: FirstLineIndent, line: OneAndHalf));
                    body.Append(Signature(values));
                    break;
                }

            case "title": {
                    body.Append(P("Министерство образования Республики Беларусь", JustificationValues.Center));
                    body.Append(P(V("univ"), JustificationValues.Center, bold: true));
                    body.Append(P($"Факультет {V("faculty")}", JustificationValues.Center));
                    body.Append(P($"Кафедра {V("dept")}", JustificationValues.Center));
                    body.Append(P("КУРСОВАЯ РАБОТА", JustificationValues.Center, bold: true, before: 3600, after: 240));
                    body.Append(P($"Тема: {V("topic")}", JustificationValues.Center, after: 3600));
                    body.Append(P($"Выполнил: {V("student")}\nРуководитель: {V("head")}",
                        JustificationValues.Right, after: 3600));
                    body.Append(P($"Минск, {V("year")}", JustificationValues.Center));
                    break;
                }

            default:
                body.Append(P(template.Title, JustificationValues.Center, bold: true));
                break;
        }

        // А4, поля: левое 3 см, правое 1 см, верх и низ по 2 см
        body.Append(new SectionProperties(
            new PageSize { Width = 11906U, Height = 16838U },
            new PageMargin { Top = 1134, Right = 567U, Bottom = 1134, Left = 1701U, Header = 709U, Footer = 709U, Gutter = 0U }));

        main.Document.Save();
    }

    // Дата слева, подпись справа (табуляция по правому краю)
    private static Paragraph Signature(IReadOnlyDictionary<string, string> values)
    {
        values.TryGetValue("fio", out var fio);
        var who = string.IsNullOrWhiteSpace(fio) ? "__________" : TemplateCatalog.ShortName(fio);

        var props = new ParagraphProperties(
            new Tabs(new TabStop { Val = TabStopValues.Right, Position = TextWidth }),
            new SpacingBetweenLines { Before = "720", After = "0" });

        var p = new Paragraph(props);
        p.Append(Runs(DateTime.Today.ToString("dd.MM.yyyy"), false, false));
        p.Append(new Run(new TabChar()));
        p.Append(Runs("____________ " + who, false, false));
        return p;
    }

    // Абзац. Порядок свойств соответствует схеме Word: интервалы, отступы, выравнивание
    private static Paragraph P(
        string text,
        JustificationValues align,
        bool bold = false,
        bool italic = false,
        int firstLine = 0,
        int left = 0,
        int before = 0,
        int after = 0,
        int line = 240)
    {
        var props = new ParagraphProperties();
        props.Append(new SpacingBetweenLines {
            Before = before.ToString(),
            After = after.ToString(),
            Line = line.ToString(),
            LineRule = LineSpacingRuleValues.Auto,
        });

        if (left != 0 || firstLine != 0)
            props.Append(new Indentation { Left = left.ToString(), FirstLine = firstLine.ToString() });

        props.Append(new Justification { Val = align });

        var p = new Paragraph(props);
        p.Append(Runs(text, bold, italic));
        return p;
    }

    // Текст с переносами строк: каждая строка после первой начинается с разрыва
    private static List<Run> Runs(string text, bool bold, bool italic)
    {
        var result = new List<Run>();
        var lines = text.Replace("\r", "").Split('\n');

        for (int i = 0; i < lines.Length; i++) {
            var rp = new RunProperties();
            rp.Append(new RunFonts { Ascii = FontName, HighAnsi = FontName, ComplexScript = FontName });
            if (bold)
                rp.Append(new Bold());
            if (italic)
                rp.Append(new Italic());
            rp.Append(new FontSize { Val = HalfPoints.ToString() });
            rp.Append(new FontSizeComplexScript { Val = HalfPoints.ToString() });

            var run = new Run(rp);
            if (i > 0)
                run.Append(new Break());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });

            result.Add(run);
        }

        return result;
    }
}