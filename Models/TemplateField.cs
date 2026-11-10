/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Описание поля ввода шаблона, источника автоподстановки и режима расширения
 * СОДЕРЖИМОЕ: TemplateField, ProfileSource, FieldExpansionMode
 * ЗАВИСИМОСТИ: .NET BCL
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>Откуда поле берёт значение при нажатии «Заполнить из профиля».</summary>
public enum ProfileSource { None, FullName, ShortName, Passport, Address }

/// <summary>Режим детализации текста поля (например, [Поле|расписать] или [Поле|кратко]).</summary>
public enum FieldExpansionMode { Normal, Elaborate, Brief }

public record TemplateField(
    string Id,
    string Label,
    string Placeholder,
    ProfileSource Source = ProfileSource.None,
    bool Multiline = false,
    FieldExpansionMode ExpansionMode = FieldExpansionMode.Normal)
{
    /// <summary>Требует ли поле официального развёрнутого описания от ИИ (синтаксис [Поле|расписать]).</summary>
    public bool RequiresExpansion => ExpansionMode == FieldExpansionMode.Elaborate;
}
