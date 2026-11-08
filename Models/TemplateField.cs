/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Описание поля ввода шаблона и источника автоподстановки из профиля
 * СОДЕРЖИМОЕ: TemplateField, ProfileSource
 * ЗАВИСИМОСТИ: .NET BCL
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>Откуда поле берёт значение при нажатии «Заполнить из профиля».</summary>
public enum ProfileSource { None, FullName, ShortName, Passport, Address }

public record TemplateField(
    string Id,
    string Label,
    string Placeholder,
    ProfileSource Source = ProfileSource.None,
    bool Multiline = false);
