namespace GosTek.Views;

/// <summary>Окна-заглушки для разделов, которых ещё нет.</summary>
public static class Stubs
{
    private static readonly Dictionary<string, (string Title, string Text)> Items = new() {
        ["check"] = ("✔️ Проверка документа",
            "Скоро здесь можно будет загрузить документ и получить список ошибок оформления: поля, шрифты, реквизиты и отступы."),

        ["scan"] = ("📷 Сканирование",
            "Скоро здесь появится сканирование документа камерой с распознаванием текста и автозаполнением данных."),

        ["profile"] = ("👤 Мой профиль",
            "Скоро здесь будут ваши данные и реквизиты, в том числе расчётный счёт, который сейчас не заполнен."),

        ["explanatory"] = ("📄 Объяснительная записка",
            "Скоро здесь будет готовый шаблон: причина опоздания, нарушения срока или отсутствия."),

        ["ip"] = ("📄 Заявление о регистрации ИП",
            "Скоро здесь будет шаблон заявления для подачи в регистрирующий орган."),
    };

    public static Task ShowAsync(string? key)
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null)
            return Task.CompletedTask;

        var (title, text) = key is not null && Items.TryGetValue(key, out var item)
            ? item
            : ("Скоро", "Этот раздел скоро будет добавлен.");

        return page.DisplayAlertAsync(title, text, "Понятно");
    }
}