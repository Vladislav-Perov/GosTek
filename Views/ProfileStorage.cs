/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Защищенное хранение персональных данных профиля и кастомных полей в зашифрованном SecureStorage
 * СОДЕРЖИМОЕ: ProfileData, ProfileStorage, ProfileLoadStatus
 * ЗАВИСИМОСТИ: Microsoft.Maui.Storage.SecureStorage, GosTek.Views.ProfileRules
 * ==============================================================================
 */
using System.Text.Json;

namespace GosTek;

public record ProfileData(
    string FullName = "",
    string OrgName = "",
    string Unp = "",
    string Address = "",
    string Phone = "",
    string Passport = "",
    string Account = "",
    Dictionary<string, string>? CustomFields = null)
{
    public Dictionary<string, string> CustomFields { get; init; } =
        CustomFields != null
            ? new Dictionary<string, string>(CustomFields, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Итог чтения профиля: нет данных — это не то же самое, что не удалось прочитать.</summary>
public enum ProfileLoadStatus { Ok, Empty, Failed }

/// <summary>
/// Хранение профиля только на устройстве. Данные лежат в SecureStorage
/// (Android Keystore) одним JSON-значением, то есть в зашифрованном виде.
/// Поддерживает базовые белорусские реквизиты и динамические CustomFields.
/// </summary>
public static class ProfileStorage
{
    private const string Key = "user_profile_v1";

    /// <summary>
    /// Читает профиль и сообщает, как прошло чтение. Failed: данные есть, но расшифровать
    /// или разобрать их не удалось (повреждены, перенесены с другого устройства).
    /// </summary>
    public static async Task<(ProfileData Data, ProfileLoadStatus Status)> LoadWithStatusAsync()
    {
        string? json;
        try {
            json = await SecureStorage.Default.GetAsync(Key);
        } catch {
            return (new ProfileData(), ProfileLoadStatus.Failed);
        }

        if (string.IsNullOrEmpty(json))
            return (new ProfileData(CustomFields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)), ProfileLoadStatus.Empty);

        try {
            var data = JsonSerializer.Deserialize<ProfileData>(json);
            if (data is null)
                return (new ProfileData(CustomFields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)), ProfileLoadStatus.Failed);

            var custom = data.CustomFields is not null
                ? new Dictionary<string, string>(data.CustomFields, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return (data with { CustomFields = custom }, ProfileLoadStatus.Ok);
        } catch (JsonException) {
            return (new ProfileData(CustomFields: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)), ProfileLoadStatus.Failed);
        }
    }

    public static async Task<ProfileData> LoadAsync()
        => (await LoadWithStatusAsync()).Data;

    /// <summary>Удаляет профиль. Работает и для нечитаемого профиля: так его можно «починить».</summary>
    public static void Clear()
    {
        SecureStorage.Default.Remove(Key);
        GosTek.Views.ProfileRules.ForgetAccepted();
    }

    public static Task SaveAsync(ProfileData profile)
        => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(profile));
}
