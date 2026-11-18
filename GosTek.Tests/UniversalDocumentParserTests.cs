/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Unit-тесты универсального парсера структуры документа (UniversalDocumentParser)
 * ==============================================================================
 */
using GosTek.Services;
using Xunit;

namespace GosTek.Tests;

public class UniversalDocumentParserTests
{
    [Fact]
    public void Parse_ApplicationWithHeader_IdentifiesHeaderTitleAndBodyCorrectly()
    {
        string rawDocument = @"Директору Филиал БГТУ
«Бобруйский государственный
лесотехнический колледж»
Кубраку А.В.
от Фамилия, инициалы родителя

Заявление

Прошу отпустить моего ребёнка Фамилия, инициалы ребёнка, обучающегося группы РС02-26, с учебных занятий дата в связи с причина.

Дата                 Подпись                 Инициалы, фамилия родителя";

        var blocks = UniversalDocumentParser.Parse(rawDocument);

        Assert.NotEmpty(blocks);

        // 1. Проверяем, что строки до слова "Заявление" распознаны как HeaderRight
        var headerBlocks = blocks.Where(b => b.Type == DocumentBlockType.HeaderRight).ToList();
        Assert.Equal(5, headerBlocks.Count);
        Assert.Contains("Директору Филиал БГТУ", headerBlocks[0].Text);
        Assert.Contains("Кубраку А.В.", headerBlocks[3].Text);
        Assert.Contains("от Фамилия, инициалы родителя", headerBlocks[4].Text);

        // 2. Проверяем заголовок
        var titleBlock = blocks.FirstOrDefault(b => b.Type == DocumentBlockType.Title);
        Assert.NotNull(titleBlock);
        Assert.Equal("Заявление", titleBlock.Text);

        // 3. Проверяем основной абзац
        var paragraphBlocks = blocks.Where(b => b.Type == DocumentBlockType.Paragraph).ToList();
        Assert.NotEmpty(paragraphBlocks);
        Assert.StartsWith("Прошу отпустить моего ребёнка", paragraphBlocks[0].Text);

        // 4. Проверяем строку подписи в подвале
        var signatureBlock = blocks.FirstOrDefault(b => b.Type == DocumentBlockType.SignatureRow);
        Assert.NotNull(signatureBlock);
        Assert.Contains("Дата", signatureBlock.Col1 ?? signatureBlock.Text);
    }
}
