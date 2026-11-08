/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Сервис управления списком избранных шаблонов документов пользователя
 * СОДЕРЖИМОЕ: FavoritesService
 * ЗАВИСИМОСТИ: Microsoft.Maui.Storage
 * ==============================================================================
 */
namespace GosTek.Services;

/// <summary>
/// Сервис для сохранения, проверки и переключения избранных шаблонов.
/// Поддерживает внедрение IPreferences для полной тестируемости.
/// </summary>
public class FavoritesService
{
    public const string FavoritesKey = "favorite_templates";

    private static FavoritesService? _instance;
    public static FavoritesService Instance => _instance ??= new FavoritesService();

    public static IPreferences? PreferencesProvider { get; set; }
    private static IPreferences Prefs => PreferencesProvider ?? Preferences.Default;

    /// <summary>Событие изменения списка избранного (для реактивного обновления UI).</summary>
    public event Action? Changed;

    /// <summary>Возвращает список ID всех избранных шаблонов в порядке добавления.</summary>
    public IReadOnlyList<string> GetAll()
    {
        try
        {
            var raw = Prefs.Get(FavoritesKey, "");
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>Проверяет, находится ли шаблон с указанным ID в избранном.</summary>
    public bool IsFavorite(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return false;

        return GetAll().Contains(templateId);
    }

    /// <summary>
    /// Переключает состояние избранного для шаблона.
    /// Возвращает true, если шаблон был добавлен в избранное, и false, если удалён.
    /// </summary>
    public bool Toggle(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
            return false;

        var list = GetAll().ToList();
        bool added;

        if (list.Contains(templateId))
        {
            list.Remove(templateId);
            added = false;
        }
        else
        {
            list.Insert(0, templateId);
            added = true;
        }

        try
        {
            Prefs.Set(FavoritesKey, string.Join(",", list));
        }
        catch
        {
            // Подавление ошибок хранилища
        }

        Changed?.Invoke();
        return added;
    }

    /// <summary>Очищает весь список избранных шаблонов.</summary>
    public void Clear()
    {
        try
        {
            Prefs.Remove(FavoritesKey);
        }
        catch
        {
        }

        Changed?.Invoke();
    }
}
