namespace GosTek.Models;

/// <summary>Откуда поле берёт значение при нажатии «Заполнить из профиля».</summary>
public enum ProfileSource { None, FullName, ShortName, Passport, Address }

public record TemplateField(
    string Id,
    string Label,
    string Placeholder,
    ProfileSource Source = ProfileSource.None,
    bool Multiline = false);

public record TemplateDef(string Id, string Title, string Category, string FilePath, TemplateField[] Fields);

/// <summary>Строка в списке шаблонов (экран «Шаблоны» и «Быстрый старт» на главной).</summary>
public record TemplateItem(string Id, string Title, string Description, string Category);

/// <summary>
/// Каталог шаблонов = .docx файлы из папки, указанной в настройках.
/// Имя файла: «Категория_Название.docx» (подробнее в TemplateEngine).
/// </summary>
public static class TemplateCatalog
{
    public const string NoCategory = "Прочее";

    private static Dictionary<string, TemplateDef> _defs = new();

    public static TemplateItem[] Items { get; private set; } = Array.Empty<TemplateItem>();

    public static string[] Categories =>
        Items.Select(i => i.Category).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// <summary>Заново просканировать папку шаблонов (вызывается при открытии экранов).</summary>
    public static void Refresh()
    {
        var defs = TemplateEngine.Scan(SettingsService.TemplatesFolder);
        _defs = defs.ToDictionary(d => d.Id);
        Items = defs.Select(d => new TemplateItem(d.Id, d.Title,
            d.Fields.Length == 0 ? "Без полей для заполнения" : $"Полей для заполнения: {d.Fields.Length}",
            d.Category)).ToArray();
    }

    /// <summary>
    /// Быстрый «отпечаток» папки шаблонов (имена, размеры, даты). Если он изменился,
    /// в папку добавили, удалили или заменили файл. Работает и на Windows, и на Android.
    /// </summary>
    public static string FolderSignature()
    {
        try {
            var folder = SettingsService.TemplatesFolder;
            if (!Directory.Exists(folder))
                return "";

            var sb = new System.Text.StringBuilder();
            foreach (var file in Directory.EnumerateFiles(folder, "*.docx", SearchOption.AllDirectories).OrderBy(f => f)) {
                if (Path.GetFileName(file).StartsWith("~$"))
                    continue;
                var info = new FileInfo(file);
                sb.Append(file).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append(';');
            }
            return sb.ToString();
        } catch {
            return "";
        }
    }

    /// <summary>null, если такого шаблона больше нет (файл удалили или переименовали).</summary>
    public static TemplateDef? Find(string? id)
    {
        if (id is null)
            return null;
        if (_defs.Count == 0)
            Refresh();
        return _defs.TryGetValue(id, out var d) ? d : null;
    }

    /// <summary>Значение по умолчанию: сегодняшняя дата для полей вида [Дата-00.00.0000].</summary>
    public static string? DefaultFor(TemplateField f)
        => f.Label.Contains("дата", StringComparison.CurrentCultureIgnoreCase)
           && System.Text.RegularExpressions.Regex.IsMatch(f.Placeholder, @"^[0-9дмгДМГ]{2}\.[0-9дмгДМГ]{2}\.[0-9дмгДМГ]{4}$")
            ? DateTime.Today.ToString("dd.MM.yyyy")
            : null;

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

    /// <summary>Забыть все введённые значения (кнопка «Удалить все данные»).</summary>
    public static void Clear() => Data.Clear();
}
