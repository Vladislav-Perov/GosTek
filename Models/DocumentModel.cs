namespace GosTek.Models;

/// <summary>Откуда поле берёт значение при нажатии «Заполнить из профиля».</summary>
public enum ProfileSource { None, FullName, ShortName, Passport, Address }

public record TemplateField(
    string Id,
    string Label,
    string Placeholder,
    ProfileSource Source = ProfileSource.None,
    bool Multiline = false);

public record TemplateDef(string Id, string Title, TemplateField[] Fields);

/// <summary>Строка в списке шаблонов (экран «Шаблоны» и «Быстрый старт» на главной).</summary>
public record TemplateItem(string Id, string Title, string Description, string Category);

public static class TemplateCatalog
{
    /// <summary>
    /// Все шаблоны для списков. Если для шаблона нет описания полей в Defs,
    /// при нажатии показывается заглушка «скоро будет добавлен».
    /// </summary>
    public static readonly TemplateItem[] Items =
    {
        new("explanatory", "Объяснительная записка",   "Причина опоздания, нарушения срока, отсутствия", "Организация"),
        new("ip",          "Заявление о регистрации ИП", "Для подачи в регистрирующий орган",           "Предприниматель"),
        new("title",       "Титульный лист работы",    "Курсовая, дипломная, отчёт по практике",         "Учёба"),
        new("contract",    "Договор оказания услуг",   "С реквизитами заказчика и исполнителя",          "Предприниматель"),
        new("proxy",       "Доверенность",             "На получение товара или представление интересов", "Организация"),
        new("practice",    "Отчёт по практике",        "Структура по требованиям кафедры",               "Учёба"),
    };

    // Id совпадают с Id в Items
    private static readonly TemplateDef[] Defs =
    {
        new("explanatory", "Объяснительная записка", new TemplateField[] {
            new("pos",     "Кому (должность)",         "Начальнику инспекции"),
            new("org",     "Организация получателя",   "Инспекция МНС по Центральному району г. Минска"),
            new("fio",     "От кого",                  "Фамилия Имя Отчество", ProfileSource.FullName),
            new("subject", "Тема",                     "О задержке сдачи отчётности"),
            new("text",    "Текст объяснения",         "Опишите суть своими словами", Multiline: true),
        }),

        new("ip", "Заявление о регистрации ИП", new TemplateField[] {
            new("organ",    "Регистрирующий орган", "Минский горисполком"),
            new("fio",      "ФИО заявителя",        "Фамилия Имя Отчество", ProfileSource.FullName),
            new("passport", "Паспорт",              "Серия и номер",        ProfileSource.Passport),
            new("addr",     "Адрес регистрации",    "Адрес места жительства", ProfileSource.Address),
            new("activity", "Вид деятельности",     "Разработка программного обеспечения"),
        }),

        new("title", "Титульный лист работы", new TemplateField[] {
            new("univ",    "Учебное заведение", "БГТУ"),
            new("faculty", "Факультет",         "Информационных технологий"),
            new("dept",    "Кафедра",           "Программной инженерии"),
            new("topic",   "Тема работы",       "Разработка мобильного приложения"),
            new("student", "Студент",           "Фамилия И. О.", ProfileSource.ShortName),
            new("head",    "Руководитель",      "Фамилия И. О."),
            new("year",    "Год",               "2026"),
        }),
    };

    /// <summary>null, если описания полей для этого шаблона ещё нет.</summary>
    public static TemplateDef? Find(string? id) => Defs.FirstOrDefault(t => t.Id == id);

    public static string FromProfile(ProfileSource source, ProfileData p) => source switch {
        ProfileSource.FullName => p.FullName,
        ProfileSource.ShortName => ShortName(p.FullName),
        ProfileSource.Passport => p.Passport,
        ProfileSource.Address => p.Address,
        _ => "",
    };

    // «Иванов Иван Иванович» -> «Иванов И. И.»
    public static string ShortName(string full)
    {
        var parts = full.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return "";

        var result = parts[0];
        for (int i = 1; i < parts.Length && i < 3; i++)
            result += $" {char.ToUpper(parts[i][0])}.";

        return result;
    }
}

/// <summary>
/// Введённые значения полей, пока приложение открыто. Только в памяти:
/// там паспорт и адрес, на диск без шифрования их класть не стоит.
/// Предпросмотр и экспорт читают отсюда.
/// </summary>
public static class DraftStore
{
    private static readonly Dictionary<string, Dictionary<string, string>> Data = new();

    public static Dictionary<string, string> For(string templateId)
    {
        if (!Data.TryGetValue(templateId, out var draft))
            Data[templateId] = draft = new Dictionary<string, string>();

        return draft;
    }
}
