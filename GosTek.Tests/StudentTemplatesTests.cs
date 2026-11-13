/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты для студенческих шаблонов документов DOCX (Фаза 13)
 * СОДЕРЖИМОЕ: StudentTemplatesTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Services.TemplateSeeder, GosTek.TemplateEngine, DocumentFormat.OpenXml
 * ==============================================================================
 */
using DocumentFormat.OpenXml.Packaging;
using GosTek.Models;
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class StudentTemplatesTests
{
    private static readonly (string RawAsset, string TargetFileName)[] StudentTemplates =
    {
        (TemplateSeeder.StudyCertificateRawAsset, TemplateSeeder.StudyCertificateTargetName),
        (TemplateSeeder.FinancialAidRawAsset, TemplateSeeder.FinancialAidTargetName),
        (TemplateSeeder.DormitoryRawAsset, TemplateSeeder.DormitoryTargetName),
        (TemplateSeeder.AcademicLeaveRawAsset, TemplateSeeder.AcademicLeaveTargetName),
        (TemplateSeeder.StudentExplanationRawAsset, TemplateSeeder.StudentExplanationTargetName),
        (TemplateSeeder.IndividualScheduleRawAsset, TemplateSeeder.IndividualScheduleTargetName)
    };

    [Fact]
    public void StudentTemplates_Scan_DiscoversAllTemplatesAndFields()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"student_scan_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            foreach (var (_, targetFileName) in StudentTemplates)
            {
                string filePath = Path.Combine(tempDir, targetFileName);
                TemplateSeeder.CreateTemplateDocument(filePath, targetFileName);
            }

            var scanned = TemplateEngine.Scan(tempDir);
            Assert.Equal(6, scanned.Count);

            foreach (var doc in scanned)
            {
                Assert.Equal("Учёба", doc.Category);
                Assert.False(string.IsNullOrWhiteSpace(doc.Title));
                Assert.True(doc.Fields.Length > 0, $"Шаблон {doc.Title} не содержит распознанных полей.");

                // Все студенческие документы содержат поле ФИО (fullname)
                Assert.Contains(doc.Fields, f => f.Id == "fullname");
            }

            Assert.Contains(scanned, d => d.Title == "Справка с места учёбы");
            Assert.Contains(scanned, d => d.Title == "Заявление на материальную помощь");
            Assert.Contains(scanned, d => d.Title == "Заявление на общежитие");
            Assert.Contains(scanned, d => d.Title == "Заявление на академический отпуск");
            Assert.Contains(scanned, d => d.Title == "Объяснительная записка в деканат");
            Assert.Contains(scanned, d => d.Title == "Индивидуальный график сдачи");
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void StudentTemplates_FillTo_ReplacesAllPlaceholdersWithoutCorruptingDocx()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"student_fill_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var values = new Dictionary<string, string>
            {
                ["fullname"] = "Казимирский Артур Дмитриевич",
                ["group"] = "10701121",
                ["specialty"] = "Программная инженерия",
                ["faculty"] = "ФИТР",
                ["course"] = "3",
                ["university"] = "Белорусский национальный технический университет",
                ["studyform"] = "дневная бюджетная",
                ["issuereason"] = "по месту требования",
                ["date"] = "15.09.2026",
                ["reason"] = "в связи со сложным материальным положением и по семейным обстоятельствам",
                ["phone"] = "+375 (29) 123-45-67",
                ["deanname"] = "Харитончик С.В.",
                ["rectorname"] = "Харитончик С.В.",
                ["commissionhead"] = "Декан ФИТР",
                ["homeaddress"] = "г. Брест, ул. Советская, д. 5",
                ["academicyear"] = "2026/2027",
                ["privilegecategory"] = "без льгот",
                ["startdate"] = "01.10.2026",
                ["enddate"] = "30.06.2027",
                ["attacheddocs"] = "Справка ВКК №12",
                ["misseddates"] = "12.09.2026 - 14.09.2026",
                ["missedclasses"] = "учебные занятия",
                ["semester"] = "осенний семестр",
                ["scheduledetails"] = "Математика - 20.09.2026, Физика - 25.09.2026",
                ["deadlinedate"] = "30.09.2026",
                ["декан фио"] = "А.А. Гусев",
                ["секретарь фио"] = "Е.В. Мороз",
                ["зав. кафедрой фио"] = "В.В. Потапов",
                ["подтверждающие документы"] = "Справка о доходах за последние 6 месяцев"
            };

            foreach (var (_, targetFileName) in StudentTemplates)
            {
                string templatePath = Path.Combine(tempDir, targetFileName);
                TemplateSeeder.CreateTemplateDocument(templatePath, targetFileName);

                var (cat, title) = TemplateEngine.ParseName(Path.GetFileNameWithoutExtension(templatePath), null);
                var def = new TemplateDef(Guid.NewGuid().ToString("N")[..10], title, cat, templatePath, Array.Empty<TemplateField>());

                string outputPath = Path.Combine(tempDir, $"filled_{targetFileName}");
                TemplateEngine.FillTo(def, values, outputPath);

                Assert.True(File.Exists(outputPath), $"Файл {outputPath} не был создан.");

                // Проверяем, что полученный .docx валиден с точки зрения OpenXml
                using var doc = WordprocessingDocument.Open(outputPath, false);
                Assert.NotNull(doc.MainDocumentPart?.Document?.Body);

                string bodyText = doc.MainDocumentPart.Document.Body.InnerText;

                // Проверяем подстановку ключевых полей
                Assert.Contains("Казимирский Артур Дмитриевич", bodyText);
                Assert.Contains("10701121", bodyText);
                Assert.Contains("15.09.2026", bodyText);

                // Проверяем, что не осталось плейсхолдеров вида {{FullName}} или {{Group}}
                Assert.DoesNotContain("{{FullName}}", bodyText);
                Assert.DoesNotContain("{{Group}}", bodyText);
                Assert.DoesNotContain("{{Date}}", bodyText);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void TemplateEngine_DoubleBraceTokens_CorrectlyParsedAndSupported()
    {
        var (label1, hint1, mode1) = TemplateEngine.ParseTokenDetails("FullName");
        Assert.Equal("FullName", label1);
        Assert.Equal("", hint1);
        Assert.Equal(FieldExpansionMode.Normal, mode1);

        var (label2, hint2, mode2) = TemplateEngine.ParseTokenDetails("Reason|расписать");
        Assert.Equal("Reason", label2);
        Assert.Equal(FieldExpansionMode.Elaborate, mode2);

        var (label3, hint3, mode3) = TemplateEngine.ParseTokenDetails("Group-10701121");
        Assert.Equal("Group", label3);
        Assert.Equal("10701121", hint3);
    }

    [Fact]
    public void TemplateEngine_ResolveFromProfile_HandlesStudentFields()
    {
        var profile = new ProfileData
        {
            FullName = "Сидоров Петр Алексеевич",
            OrgName = "Белорусский государственный университет",
            Address = "г. Минск, ул. Бобруйская, 5",
            Phone = "+375 (29) 999-88-77",
            CustomFields = new Dictionary<string, string>
            {
                ["Group"] = "10701121",
                ["Faculty"] = "ФИТР",
                ["Specialty"] = "Программная инженерия"
            }
        };

        Assert.Equal("Сидоров Петр Алексеевич", TemplateEngine.ResolveFromProfile("FullName", profile));
        Assert.Equal("Сидоров Петр Алексеевич", TemplateEngine.ResolveFromProfile("ФИО", profile));
        Assert.Equal("Белорусский государственный университет", TemplateEngine.ResolveFromProfile("University", profile));
        Assert.Equal("+375 (29) 999-88-77", TemplateEngine.ResolveFromProfile("Phone", profile));
        Assert.Equal("10701121", TemplateEngine.ResolveFromProfile("Group", profile));
        Assert.Equal("ФИТР", TemplateEngine.ResolveFromProfile("Faculty", profile));
        Assert.Equal("Программная инженерия", TemplateEngine.ResolveFromProfile("Specialty", profile));
    }
}
