using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

public static class TemplateCatalog
{
    // Id совпадают с Id в списке TemplatesPage
    private static readonly TemplateDef[] All =
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
    public static TemplateDef? Find(string? id) => All.FirstOrDefault(t => t.Id == id);

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
/// Предпросмотр и экспорт будут читать отсюда.
/// </summary>
public static class DraftStore
{
    private static readonly Dictionary<string, Dictionary<string, string>> Data = new();

    public static Dictionary<string, string> For(string templateId)
    {
        if (!Data.TryGetValue(templateId, out var draft)) {
            draft = new Dictionary<string, string>();
            Data[templateId] = draft;
        }
        return draft;
    }
}