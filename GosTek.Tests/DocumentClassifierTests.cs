/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты классификатора типов документов DocumentClassifier
 * СОДЕРЖИМОЕ: DocumentClassifierTests
 * ЗАВИСИМОСТИ: Xunit, GosTek.Services
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class DocumentClassifierTests
{
    private readonly IDocumentClassifier _classifier = new DocumentClassifier();

    [Theory]
    [InlineData("ЗАЯВЛЕНИЕ\nПрошу предоставить очередной оплачиваемый отпуск.", "Заявление", "Заявления и бланки")]
    [InlineData("заявление о государственной регистрации физического лица в качестве индивидуального предпринимателя", "Заявление", "Заявления и бланки")]
    [InlineData("ДОГОВОР АРЕНДЫ НЕЖИЛОГО ПОМЕЩЕНИЯ № 42", "Договор", "Договор", true)]
    [InlineData("трудовой договор между работником и работодателем", "Договор", "Договоры")]
    [InlineData("ПРИКАЗ № 104-к\nг. Минск\nПРИКАЗЫВАЮ:", "Приказ", "Приказы")]
    [InlineData("АКТ ПРИЕМА-ПЕРЕДАЧИ ВЫПОЛНЕННЫХ РАБОТ", "Акт", "Акты")]
    [InlineData("ОБЪЯСНИТЕЛЬНАЯ ЗАПИСКА\nЯ, Иванов И.И., опоздал по причине...", "Объяснительная", "Заявления и бланки")]
    [InlineData("СПРАВКА С МЕСТА РАБОТЫ\nВыдана для предъявления по месту требования.", "Справка", "Справки")]
    [InlineData("ОТЧЕТ ПО ПРАКТИКЕ\nСтудента 4 курса факультета ИТ", "Отчет", "Отчеты")]
    [InlineData("КУРСОВАЯ РАБОТА\nПояснительная записка по дисциплине Базы Данных", "Курсовая работа", "Учебные работы, БГТУ")]
    [InlineData("ДИПЛОМНЫЙ ПРОЕКТ\nТема: Разработка информационной системы", "Дипломный проект", "Учебные работы, БГТУ")]
    [InlineData("ВЫПУСКНАЯ КВАЛИФИКАЦИОННАЯ РАБОТА", "Дипломный проект", "Учебные работы, БГТУ")]
    [InlineData("УСТАВ ОБЩЕСТВА С ОГРАНИЧЕННОЙ ОТВЕТСТВЕННОСТЬЮ «ГОСТЕК»", "Устав", "Учредительные документы")]
    public void Classify_RecognizesStandardDocumentTypes(string text, string expectedType, string expectedCategory, bool partialCategory = false)
    {
        var result = _classifier.Classify(text);

        Assert.True(result.IsDetected);
        Assert.Equal(expectedType, result.DocumentType);
        if (partialCategory)
            Assert.Contains(expectedCategory, result.Category);
        else
            Assert.Equal(expectedCategory, result.Category);
        Assert.True(result.Confidence >= 0.75);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \t\r\n  ")]
    public void Classify_ReturnsUndetectedOnNullOrEmpty(string? text)
    {
        var result = _classifier.Classify(text);

        Assert.False(result.IsDetected);
        Assert.Equal(string.Empty, result.DocumentType);
        Assert.Equal(string.Empty, result.Category);
        Assert.Equal(0.0, result.Confidence);
    }

    [Fact]
    public void Classify_HandlesJunkOcrTextWithoutFalsePositives()
    {
        string junkOcr = "1928374 %%$$ random characters ... ;;; ::: /// [[[ ]]] abc def xyz";
        var result = _classifier.Classify(junkOcr);

        Assert.False(result.IsDetected);
        Assert.Null(_classifier.DetectCategory(junkOcr));
    }

    [Fact]
    public void Classify_DoesNotTriggerOnEmbeddedSubstrings()
    {
        // Слово "АКТ" не должно срабатывать внутри слов "ФАКТИЧЕСКИ", "КОНТАКТ", "ПРАКТИКА"
        string subWordText = "Фактически данный контакт был установлен во время прохождения практики на предприятии.";
        var result = _classifier.Classify(subWordText);

        Assert.False(result.IsDetected);
    }

    [Fact]
    public void DetectCategory_ReturnsCategoryStringWhenDetected()
    {
        string text = "Заявление о переводе на заочную форму обучения";
        var category = _classifier.DetectCategory(text);

        Assert.Equal("Заявления и бланки", category);
    }

    [Fact]
    public void Classify_HeaderPositionYieldsHigherConfidence()
    {
        string inHeader = "ЗАЯВЛЕНИЕ\nПрошу предоставить отпуск с 10.05.2026";
        string inFooter = new string('x', 400) + "\nЗАЯВЛЕНИЕ";

        var resHeader = _classifier.Classify(inHeader);
        var resFooter = _classifier.Classify(inFooter);

        Assert.True(resHeader.IsDetected);
        Assert.True(resFooter.IsDetected);
        Assert.True(resHeader.Confidence > resFooter.Confidence);
    }
}
