namespace GosTek;

/// <summary>Как называть файл по умолчанию.</summary>
public enum NameMode { Template, TemplateDate }

/// <summary>Что делать, если файл с таким именем уже есть.</summary>
public enum ConflictMode { Number, Overwrite, Ask }

/// <summary>Что делать после сохранения файла.</summary>
public enum AfterSaveAction { Ask, Open, Share, Nothing }

/// <summary>
/// Настройки приложения. Хранятся в Preferences: здесь нет секретов
/// (профиль лежит отдельно, в SecureStorage). Любое изменение вызывает Changed.
/// </summary>
public static class SettingsService
{
    private const string KeyFolder = "set_save_folder";
    private const string KeyName = "set_name_mode";
    private const string KeyConflict = "set_conflict_mode";
    private const string KeyAfterSave = "set_after_save";

    /// <summary>Изменилась любая настройка. На событие подписана панель настроек.</summary>
    public static event Action? Changed;

    /// <summary>Папка по умолчанию на ПК: Документы\GosTek.</summary>
    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GosTek");

    /// <summary>Папка сохранения на ПК. На Android не используется: там всегда Документы/GosTek.</summary>
    public static string SaveFolder
    {
        get {
            var path = Preferences.Default.Get(KeyFolder, "");
            return string.IsNullOrWhiteSpace(path) ? DefaultFolder : path;
        }
        set {
            if (string.IsNullOrWhiteSpace(value) || value == DefaultFolder)
                Preferences.Default.Remove(KeyFolder);
            else
                Preferences.Default.Set(KeyFolder, value);

            Changed?.Invoke();
        }
    }

    public static bool IsDefaultFolder => string.IsNullOrWhiteSpace(Preferences.Default.Get(KeyFolder, ""));

    public static NameMode Name
    {
        get => ReadEnum(KeyName, NameMode.Template);
        set => WriteEnum(KeyName, value);
    }

    public static ConflictMode Conflict
    {
        get => ReadEnum(KeyConflict, ConflictMode.Number);
        set => WriteEnum(KeyConflict, value);
    }

    public static AfterSaveAction AfterSave
    {
        get => ReadEnum(KeyAfterSave, AfterSaveAction.Ask);
        set => WriteEnum(KeyAfterSave, value);
    }

    // Если в Preferences лежит число вне диапазона (например, после обновления), берём значение по умолчанию
    private static T ReadEnum<T>(string key, T fallback) where T : struct, Enum
    {
        var raw = Preferences.Default.Get(key, Convert.ToInt32(fallback));
        return Enum.IsDefined(typeof(T), raw) ? (T)Enum.ToObject(typeof(T), raw) : fallback;
    }

    private static void WriteEnum<T>(string key, T value) where T : struct, Enum
    {
        Preferences.Default.Set(key, Convert.ToInt32(value));
        Changed?.Invoke();
    }
}
