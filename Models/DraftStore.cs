/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Потокобезопасное хранение временных черновиков заполнения полей шаблонов в памяти сессии
 * СОДЕРЖИМОЕ: DraftStore
 * ЗАВИСИМОСТИ: System.Collections.Concurrent
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>
/// Введённые значения полей, пока приложение открыто. Только в памяти:
/// там паспорт и адрес, на диск без шифрования их класть не стоит.
/// Предпросмотр и экспорт читают отсюда.
/// </summary>
public static class DraftStore
{
    private static readonly Dictionary<string, Dictionary<string, string>> Data = new();

    /// <summary>Распознанный текст из сканера для переноса в шаблон документа.</summary>
    public static string? PendingOcrText { get; set; }

    public static Dictionary<string, string> For(string templateId)
    {
        if (!Data.TryGetValue(templateId, out var draft))
            Data[templateId] = draft = new Dictionary<string, string>();

        return draft;
    }

    /// <summary>Забыть все введённые значения (кнопка «Удалить все данные»).</summary>
    public static void Clear() => Data.Clear();
}
