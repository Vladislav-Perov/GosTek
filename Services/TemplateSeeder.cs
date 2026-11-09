/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис инициализации и копирования эталонных шаблонов ГОСТ при первом запуске приложения
 * СОДЕРЖИМОЕ: TemplateSeeder
 * ЗАВИСИМОСТИ: DocumentFormat.OpenXml, GosTek.Models.TemplateCatalog, GosTek.SettingsService
 * ==============================================================================
 */
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GosTek.Models;

namespace GosTek.Services;

/// <summary>
/// Автоматически развертывает базовые предустановленные шаблоны .docx при первом запуске приложения,
/// если рабочая директория шаблонов пользователя пуста.
/// </summary>
public static class TemplateSeeder
{
    public const string VacationRawAsset = "vacation_template.docx";
    public const string ContractRawAsset = "contract_template.docx";
    public const string ExplanationRawAsset = "explanation_template.docx";

    public const string VacationTargetName = "Заявления_Заявление_на_отпуск.docx";
    public const string ContractTargetName = "Договоры_Договор_купли_продажи.docx";
    public const string ExplanationTargetName = "Служебные_Объяснительная_записка.docx";

    public static readonly (string RawAsset, string TargetFileName)[] DefaultTemplates =
    {
        (VacationRawAsset, VacationTargetName),
        (ContractRawAsset, ContractTargetName),
        (ExplanationRawAsset, ExplanationTargetName)
    };

    /// <summary>
    /// Проверяет наличие шаблонов в рабочей папке и при их отсутствии копирует дефолтные файлы.
    /// </summary>
    public static async Task EnsureDefaultTemplatesAsync()
    {
        try {
            string folder = SettingsService.TemplatesFolder;
            Directory.CreateDirectory(folder);

            // Если в каталоге уже есть файлы docx, пропускаем сидирование
            var existing = Directory.EnumerateFiles(folder, "*.docx", SearchOption.AllDirectories).Any();
            if (existing)
                return;

            foreach (var (rawAsset, targetFileName) in DefaultTemplates) {
                string targetPath = Path.Combine(folder, targetFileName);
                if (File.Exists(targetPath))
                    continue;

                bool copied = false;
                try {
                    // 1. Попытка скопировать из ресурсов приложения (Resources/Raw)
                    using var stream = await FileSystem.OpenAppPackageFileAsync(rawAsset);
                    using var fs = File.Create(targetPath);
                    await stream.CopyToAsync(fs);
                    copied = true;
                } catch {
                    copied = false;
                }

                // 2. Если файл отсутствует в ресурсах пакета, генерируем эталонный документ OpenXML
                if (!copied)
                    CreateTemplateDocument(targetPath, targetFileName);
            }

            TemplateCatalog.Refresh();
        } catch (Exception ex) {
            System.Diagnostics.Debug.WriteLine($"[TemplateSeeder] Ошибка сидирования шаблонов: {ex.Message}");
        }
    }

    /// <summary>
    /// Генерирует структуру .docx документа с токенами ГОСТ.
    /// </summary>
    public static void CreateTemplateDocument(string outputPath, string templateName)
    {
        string dir = Path.GetDirectoryName(outputPath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        if (templateName.Contains("отпуск", StringComparison.OrdinalIgnoreCase) ||
            templateName.Contains("vacation", StringComparison.OrdinalIgnoreCase))
            BuildVacationTemplate(body);
        else if (templateName.Contains("Договор", StringComparison.OrdinalIgnoreCase) ||
                 templateName.Contains("contract", StringComparison.OrdinalIgnoreCase))
            BuildSalesContractTemplate(body);
        else
            BuildExplanationTemplate(body);

        mainPart.Document.Save();
    }

    private static void BuildVacationTemplate(Body body)
    {
        AddParagraph(body, "Руководителю [Организация]", JustificationValues.Right);
        AddParagraph(body, "[Должность]", JustificationValues.Right);
        AddParagraph(body, "[ФИО]", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ЗАЯВЛЕНИЕ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Прошу предоставить мне трудовой отпуск с [Дата начала-00.00.0000] продолжительностью [Количество дней-14] календарных дней.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Дата-00.00.0000]                                                              [ФИО]", JustificationValues.Left);
    }

    private static void BuildSalesContractTemplate(Body body)
    {
        AddParagraph(body, "ДОГОВОР КУПЛИ-ПРОДАЖИ", JustificationValues.Center, isBold: true);
        AddParagraph(body, "г. [Город-Минск]                                                  [Дата-00.00.0000]", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Организация], именуемое в дальнейшем «Продавец», в лице представителя [ФИО], с одной стороны, и гражданин [Покупатель ФИО], с другой стороны, заключили настоящий договор о нижеследующем:", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "1. Предмет договора: Продавец обязуется передать в собственность, а Покупатель принять и оплатить [Наименование товара].", JustificationValues.Left);
        AddParagraph(body, "2. Сумма договора составляет [Сумма-0.00] белорусских рублей.", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "РЕКВИЗИТЫ СТОРОН:", JustificationValues.Left, isBold: true);
        AddParagraph(body, "Продавец: [Организация], УНП: [УНП], Юр. адрес: [Юридический адрес], Р/с: [Расчётный счёт], Тел: [Телефон].", JustificationValues.Left);
        AddParagraph(body, "Покупатель: [Покупатель ФИО], Паспорт: [Паспорт].", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Подписи сторон: ______________ / [ФИО]                 ______________ / [Покупатель ФИО]", JustificationValues.Left);
    }

    private static void BuildExplanationTemplate(Body body)
    {
        AddParagraph(body, "Руководителю [Организация]", JustificationValues.Right);
        AddParagraph(body, "от [Должность]", JustificationValues.Right);
        AddParagraph(body, "[ФИО]", JustificationValues.Right);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА", JustificationValues.Center, isBold: true);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "Касательно [Причина или событие] сообщаю следующее: [Текст объяснения обстоятельств].", JustificationValues.Left);
        AddParagraph(body, "", JustificationValues.Left);
        AddParagraph(body, "[Дата-00.00.0000]                                                              [ФИО]", JustificationValues.Left);
    }

    private static void AddParagraph(Body body, string text, JustificationValues align, bool isBold = false)
    {
        var p = body.AppendChild(new Paragraph());
        var pPr = p.AppendChild(new ParagraphProperties());
        pPr.AppendChild(new Justification { Val = align });

        var run = p.AppendChild(new Run());
        if (isBold) {
            var rPr = run.AppendChild(new RunProperties());
            rPr.AppendChild(new Bold());
        }
        run.AppendChild(new Text(text));
    }
}
