namespace GosTek.Views;

/// <summary>Окна-заглушки для разделов, которых ещё нет.</summary>
public static class Stubs
{
    private static readonly Dictionary<string, (string Title, string Text)> Items = new() {
        ["check"] = ("Проверка документа",
            "Скоро здесь можно будет загрузить документ и получить список ошибок оформления: поля, шрифты, реквизиты и отступы."),
    };

    /// <summary>Заглушка раздела по ключу; для неизвестного ключа — общее «Скоро».</summary>
    public static Task ShowAsync(string? key)
    {
        var (title, text) = key is not null && Items.TryGetValue(key, out var item)
            ? item
            : ("Скоро", "Этот раздел скоро будет добавлен.");

        return Alert(title, text);
    }

    /// <summary>Заглушка шаблона, для которого ещё нет описания полей.</summary>
    public static Task ShowTemplateAsync(string title)
        => Alert(title, "Файл шаблона не найден или не открывается. Проверьте папку шаблонов в настройках.");

    private static Task Alert(string title, string text)
        => Dialogs.AlertAsync(title, text);
}
