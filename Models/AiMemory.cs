/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Хранилище контекстной структурированной памяти ИИ-ассистента
 * СОДЕРЖИМОЕ: AiMemory, AiMemoryFact
 * ЗАВИСИМОСТИ: .NET BCL
 * ==============================================================================
 */
namespace GosTek.Models;

/// <summary>
/// Единичный факт, зафиксированный в памяти ИИ-ассистента в контексте текущего документа.
/// </summary>
public record AiMemoryFact(string Key, string Value, DateTime Timestamp);

/// <summary>
/// Потокобезопасный менеджер памяти ИИ-ассистента.
/// Очищается при смене шаблона и при экспорте готового документа.
/// </summary>
public class AiMemory
{
    private readonly List<AiMemoryFact> _facts = new();
    private readonly object _lock = new();

    public static AiMemory Instance { get; set; } = new();

    /// <summary>Событие обновления списка фактов памяти.</summary>
    public event Action? Changed;

    /// <summary>Все текущие факты в памяти.</summary>
    public IReadOnlyList<AiMemoryFact> Facts
    {
        get
        {
            lock (_lock)
                return _facts.ToList();
        }
    }

    /// <summary>Добавляет или обновляет факт в памяти.</summary>
    public void AddFact(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return;

        lock (_lock)
        {
            _facts.RemoveAll(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            _facts.Add(new AiMemoryFact(key.Trim(), value.Trim(), DateTime.Now));
        }
        Changed?.Invoke();
    }

    /// <summary>Удаляет факт из памяти по ключу или значению.</summary>
    public void RemoveFact(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        bool removed;
        lock (_lock)
        {
            int count = _facts.RemoveAll(f =>
                string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f.Value, key, StringComparison.OrdinalIgnoreCase));
            removed = count > 0;
        }

        if (removed)
            Changed?.Invoke();
    }

    /// <summary>Полностью очищает память ассистента.</summary>
    public void Clear()
    {
        bool hadFacts;
        lock (_lock)
        {
            hadFacts = _facts.Count > 0;
            _facts.Clear();
        }

        if (hadFacts)
            Changed?.Invoke();
    }

    /// <summary>Форматирует факты для системного промпта или сетевого запроса.</summary>
    public List<string> FormatForPrompt()
    {
        lock (_lock)
        {
            return _facts.Select(f => $"{f.Key}: {f.Value}").ToList();
        }
    }
}
