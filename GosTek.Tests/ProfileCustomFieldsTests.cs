/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модульные тесты пользовательских реквизитов профиля (CustomFields) и автоподстановки в шаблоны
 * СОДЕРЖИМОЕ: ProfileCustomFieldsTests
 * ЗАВИСИМОСТИ: xUnit, GosTek.Views.ProfileData, GosTek.TemplateEngine
 * ==============================================================================
 */
using System.Text.Json;
using GosTek.Views;
using Xunit;

namespace GosTek.Tests;

public class ProfileCustomFieldsTests
{
    [Fact]
    public void ProfileData_CustomFields_InitializedByDefault()
    {
        var profile = new ProfileData(
            FullName: "Иванов Иван Иванович",
            OrgName: "ООО Вектор",
            Unp: "123456789",
            Address: "г. Минск, ул. Ленина, 1",
            Phone: "+375291234567",
            Passport: "MP1234567",
            Account: "BY00UNBS00000000000000000000");

        Assert.NotNull(profile.CustomFields);
        Assert.Empty(profile.CustomFields);
    }

    [Fact]
    public void ProfileStorage_CustomFields_SerializationRoundtrip()
    {
        var custom = new Dictionary<string, string>
        {
            ["Должность"] = "Главный бухгалтер",
            ["Номер договора"] = "Д-45/2026",
            ["Отдел"] = "Финансово-экономический"
        };

        var original = new ProfileData(
            FullName: "Петров Петр Петрович",
            OrgName: "ЗАО Технологии",
            Unp: "987654321",
            Address: "г. Брест, ул. Советская, 10",
            Phone: "+375339876543",
            Passport: "AB7654321",
            Account: "BY11TEST11112222333344445555",
            CustomFields: custom);

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<ProfileData>(json);

        Assert.NotNull(restored);
        Assert.Equal(original.FullName, restored.FullName);
        Assert.Equal(original.OrgName, restored.OrgName);
        Assert.NotNull(restored.CustomFields);
        Assert.Equal(3, restored.CustomFields.Count);
        Assert.Equal("Главный бухгалтер", restored.CustomFields["Должность"]);
        Assert.Equal("Д-45/2026", restored.CustomFields["Номер договора"]);
        Assert.Equal("Финансово-экономический", restored.CustomFields["Отдел"]);
    }

    [Fact]
    public void ProfileStorage_CustomFields_BackwardCompatibility()
    {
        // JSON старой версии приложения (без поля CustomFields)
        var oldJson = "{" +
            "\"FullName\":\"Сидоров Сергей Сергеевич\"," +
            "\"OrgName\":\"ИП Сидоров\"," +
            "\"Unp\":\"191234567\"," +
            "\"Address\":\"г. Гродно, ул. Ожешко, 5\"," +
            "\"Phone\":\"+375297654321\"," +
            "\"Passport\":\"KH9876543\"," +
            "\"Account\":\"BY22ALFA11112222333344445555\"" +
            "}";

        var profile = JsonSerializer.Deserialize<ProfileData>(oldJson);

        Assert.NotNull(profile);
        Assert.Equal("Сидоров Сергей Сергеевич", profile.FullName);
        Assert.NotNull(profile.CustomFields);
        Assert.Empty(profile.CustomFields);
    }

    [Fact]
    public void TemplateEngine_ResolveFromProfile_StandardFields()
    {
        var profile = new ProfileData(
            FullName: "Иванов Иван Иванович",
            OrgName: "ООО Вектор",
            Unp: "123456789",
            Address: "г. Минск, ул. Ленина, 1",
            Phone: "+375291234567",
            Passport: "MP1234567",
            Account: "BY00UNBS00000000000000000000");

        Assert.Equal("Иванов Иван Иванович", TemplateEngine.ResolveFromProfile("ФИО", profile));
        Assert.Equal("Иванов Иван Иванович", TemplateEngine.ResolveFromProfile("Фамилия Имя Отчество", profile));
        Assert.Equal("ООО Вектор", TemplateEngine.ResolveFromProfile("Наименование", profile));
        Assert.Equal("ООО Вектор", TemplateEngine.ResolveFromProfile("Организация", profile));
        Assert.Equal("123456789", TemplateEngine.ResolveFromProfile("УНП", profile));
        Assert.Equal("г. Минск, ул. Ленина, 1", TemplateEngine.ResolveFromProfile("Юридический адрес", profile));
        Assert.Equal("+375291234567", TemplateEngine.ResolveFromProfile("Телефон", profile));
        Assert.Equal("MP1234567", TemplateEngine.ResolveFromProfile("Паспорт", profile));
        Assert.Equal("BY00UNBS00000000000000000000", TemplateEngine.ResolveFromProfile("Расчётный счёт", profile));
    }

    [Fact]
    public void TemplateEngine_ResolveFromProfile_CustomFields()
    {
        var profile = new ProfileData(
            FullName: "Иванов Иван Иванович",
            OrgName: "ООО Вектор",
            Unp: "123456789",
            Address: "г. Минск, ул. Ленина, 1",
            Phone: "+375291234567",
            Passport: "MP1234567",
            Account: "BY00UNBS00000000000000000000",
            CustomFields: new Dictionary<string, string>
            {
                ["Должность"] = "Директор",
                ["БИК Банка"] = "UNBSBY2X"
            });

        Assert.Equal("Директор", TemplateEngine.ResolveFromProfile("Должность", profile));
        Assert.Equal("UNBSBY2X", TemplateEngine.ResolveFromProfile("БИК Банка", profile));
        Assert.Null(TemplateEngine.ResolveFromProfile("Несуществующее поле", profile));
    }

    [Fact]
    public void TemplateEngine_ResolveCustomField_CaseInsensitiveAndTrimming()
    {
        var custom = new Dictionary<string, string>
        {
            ["Должность"] = "Инженер-программист"
        };

        Assert.Equal("Инженер-программист", TemplateEngine.ResolveCustomField("должность", custom));
        Assert.Equal("Инженер-программист", TemplateEngine.ResolveCustomField("  ДОЛЖНОСТЬ  ", custom));
        Assert.Null(TemplateEngine.ResolveCustomField("Звание", custom));
        Assert.Null(TemplateEngine.ResolveCustomField("Должность", null));
    }
}
