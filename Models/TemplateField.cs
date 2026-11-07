namespace GosTek.Models;

/// <summary>Откуда поле берёт значение при нажатии «Заполнить из профиля».</summary>
public enum ProfileSource { None, FullName, ShortName, Passport, Address }

public record TemplateField(
    string Id,
    string Label,
    string Placeholder,
    ProfileSource Source = ProfileSource.None,
    bool Multiline = false);
