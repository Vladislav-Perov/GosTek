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

/// <summary>
/// Хранение профиля только на устройстве. Данные лежат в SecureStorage
/// (Android Keystore) одним JSON-значением, то есть в зашифрованном виде.
/// </summary>
public static class ProfileStorage
{
    private const string Key = "user_profile_v1";

    public static async Task<ProfileData> LoadAsync()
    {
        try {
            var json = await SecureStorage.Default.GetAsync(Key);
            if (!string.IsNullOrEmpty(json))
                return JsonSerializer.Deserialize<ProfileData>(json) ?? new ProfileData();
        } catch {
            // Нет значения или хранилище недоступно, показываем пустой профиль
        }
        return new ProfileData();
    }

    public static Task SaveAsync(ProfileData profile)
        => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(profile));
}
