using System.Text.Json;

namespace GosTek;

public record ProfileData(
    string FullName = "",
    string OrgName = "",
    string Unp = "",
    string Address = "",
    string Phone = "",
    string Passport = "",
    string Account = "");

/// <summary>Итог чтения профиля: нет данных — это не то же самое, что не удалось прочитать.</summary>
public enum ProfileLoadStatus { Ok, Empty, Failed }

/// <summary>
/// Хранение профиля только на устройстве. Данные лежат в SecureStorage
/// (Android Keystore) одним JSON-значением, то есть в зашифрованном виде.
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
            return (new ProfileData(), ProfileLoadStatus.Empty);

        try {
            var data = JsonSerializer.Deserialize<ProfileData>(json);
            return data is null
                ? (new ProfileData(), ProfileLoadStatus.Failed)
                : (data, ProfileLoadStatus.Ok);
        } catch (JsonException) {
            return (new ProfileData(), ProfileLoadStatus.Failed);
        }
    }

    public static async Task<ProfileData> LoadAsync()
        => (await LoadWithStatusAsync()).Data;

    /// <summary>Удаляет профиль. Работает и для нечитаемого профиля: так его можно «починить».</summary>
    public static void Clear() => SecureStorage.Default.Remove(Key);

    public static Task SaveAsync(ProfileData profile)
        => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(profile));
}
