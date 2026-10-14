using GosTek.Models;

namespace GosTek;

/// <summary>
/// Собирает итоговый .docx: копия шаблона из папки шаблонов, в которой
/// поля [Название-пример] заменены введёнными значениями. Оформление шаблона не меняется.
/// Пустое поле превращается в линию «__________» для ручного заполнения.
/// </summary>
public static class DocxExporter
{
    public static void Save(TemplateDef template, IReadOnlyDictionary<string, string> values, string path)
        => TemplateEngine.FillTo(template, values, path);
}
