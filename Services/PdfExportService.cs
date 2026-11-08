/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Нативная блочная генерация документов PDF по правилам ГОСТ на базе QuestPDF (Community License)
 * СОДЕРЖИМОЕ: PdfExportService
 * ЗАВИСИМОСТИ: QuestPDF, GosTek.Models, GosTek.TemplateEngine
 * ==============================================================================
 */
using System.Diagnostics;
using GosTek.Models;
using GosTek.Views;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using QuestColors = QuestPDF.Helpers.Colors;

namespace GosTek.Services;

/// <summary>
/// Сервис нативной генерации и экспорта документов в формат PDF на базе движка QuestPDF (Community License).
/// Документы формируются локально на устройстве без передачи во внешние сети,
/// с соблюдением пропорций листа А4 и стандартных полей оформления по ГОСТ.
/// </summary>
public static class PdfExportService
{
    private const string BlankPlaceholder = "__________";

    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.UseSystemFonts = true;
        QuestPDF.Settings.ThrowOnMissingFontFamilies = false;
    }

    /// <summary>
    /// Генерирует PDF-документ из шаблона и переданных значений в указанный выходной поток.
    /// </summary>
    public static void GeneratePdf(TemplateDef template, IReadOnlyDictionary<string, string> values, Stream outputStream)
    {
        List<PreviewPara> paras = new();
        if (!string.IsNullOrEmpty(template.FilePath) && File.Exists(template.FilePath))
        {
            paras = TemplateEngine.ReadPreview(template.FilePath);
        }

        GeneratePdfCore(template.Title, template.Fields, paras, values, outputStream);
    }

    /// <summary>
    /// Генерирует PDF-документ из шаблона и сохраняет его в файл по указанному пути.
    /// </summary>
    public static void GeneratePdf(TemplateDef template, IReadOnlyDictionary<string, string> values, string outputPath)
    {
        using var stream = File.Create(outputPath);
        GeneratePdf(template, values, stream);
    }

    /// <summary>
    /// Генерирует PDF напрямую из списка параграфов разметки предпросмотра (PreviewPara).
    /// </summary>
    public static void GeneratePdf(string title, IReadOnlyList<PreviewPara> paragraphs, IReadOnlyDictionary<string, string> values, Stream outputStream)
    {
        GeneratePdfCore(title, Array.Empty<TemplateField>(), paragraphs, values, outputStream);
    }

    private static void GeneratePdfCore(
        string title,
        IReadOnlyList<TemplateField> fields,
        IReadOnlyList<PreviewPara> paragraphs,
        IReadOnlyDictionary<string, string> values,
        Stream outputStream)
    {
        string GetValue(string fieldId)
        {
            if (values.TryGetValue(fieldId, out var v) && !string.IsNullOrWhiteSpace(v))
                return v.Trim();
            return BlankPlaceholder;
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);

                // Стандартные поля страницы (ГОСТ: Левое 30 мм, Правое 15 мм, Верхнее 20 мм, Нижнее 20 мм)
                page.MarginLeft(30, Unit.Millimetre);
                page.MarginRight(15, Unit.Millimetre);
                page.MarginTop(20, Unit.Millimetre);
                page.MarginBottom(20, Unit.Millimetre);

                page.PageColor(QuestColors.White);
                page.DefaultTextStyle(x => x.FontFamily("PT Sans", "Times New Roman").FontSize(14).FontColor(QuestColors.Black));

                page.Content().Column(col =>
                {
                    if (paragraphs.Count > 0)
                    {
                        foreach (var para in paragraphs)
                        {
                            if (para.Blank)
                            {
                                col.Item().PaddingBottom(8);
                                continue;
                            }

                            col.Item().PaddingBottom(4).Text(text =>
                            {
                                if (para.Align == Microsoft.Maui.TextAlignment.Center)
                                    text.AlignCenter();
                                else if (para.Align == Microsoft.Maui.TextAlignment.End)
                                    text.AlignRight();
                                else
                                    text.AlignLeft();

                                foreach (var part in para.Parts)
                                {
                                    string content = part.FieldId is not null
                                        ? GetValue(part.FieldId)
                                        : part.Text;

                                    if (string.IsNullOrEmpty(content))
                                        continue;

                                    var span = text.Span(content);
                                    if (para.Bold) span.Bold();
                                    if (para.Italic) span.Italic();
                                    if (para.Pt > 0) span.FontSize((float)para.Pt);
                                }
                            });
                        }
                    }
                    else
                    {
                        // Резервная блочная верстка по переданным полям модели (если шаблон без docx на диске)
                        if (!string.IsNullOrWhiteSpace(title))
                        {
                            col.Item().PaddingBottom(14).AlignCenter().Text(title).Bold().FontSize(16);
                        }

                        foreach (var field in fields)
                        {
                            string val = GetValue(field.Id);
                            col.Item().PaddingBottom(6).Text(text =>
                            {
                                text.Span($"{field.Label}: ").Bold();
                                text.Span(val);
                            });
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber().FontSize(10);
                });
            });
        });

        doc.GeneratePdf(outputStream);
    }

    /// <summary>
    /// Генерирует PDF во временный файл в кэше и открывает его через системный механизм предпросмотра.
    /// </summary>
    public static async Task<bool> ExportAsync(TemplateDef template, IReadOnlyDictionary<string, string> values, string name)
    {
        var fileName = ExportService.SafeFileName(name, ExportService.DefaultFileName(template)) + ".pdf";
        var path = Path.Combine(FileSystem.CacheDirectory, fileName);

        await Task.Run(() => GeneratePdf(template, values, path));
        await ExportService.OpenFileAsync(path);
        return true;
    }
}
