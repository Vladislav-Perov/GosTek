/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Описание шаблона документа и элемента отображения в каталоге
 * СОДЕРЖИМОЕ: TemplateDef, TemplateItem
 * ЗАВИСИМОСТИ: GosTek.Models.TemplateField
 * ==============================================================================
 */
namespace GosTek.Models;

public record TemplateDef(string Id, string Title, string Category, string FilePath, TemplateField[] Fields);

/// <summary>Строка в списке шаблонов (экран «Шаблоны» и «Быстрый старт» на главной).</summary>
public record TemplateItem(string Id, string Title, string Description, string Category);
