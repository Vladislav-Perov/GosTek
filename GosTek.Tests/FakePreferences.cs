using Microsoft.Maui.Storage;

namespace GosTek.Tests;

public sealed class FakePreferences : IPreferences
{
    private readonly Dictionary<string, object> _store = new();

    public bool ContainsKey(string key, string? sharedName = null)
        => _store.ContainsKey(key);

    public void Remove(string key, string? sharedName = null)
        => _store.Remove(key);

    public void Clear(string? sharedName = null)
        => _store.Clear();

    public void Set<T>(string key, T value, string? sharedName = null)
    {
        if (value is null)
            _store.Remove(key);
        else
            _store[key] = value;
    }

    public T Get<T>(string key, T defaultValue, string? sharedName = null)
    {
        if (_store.TryGetValue(key, out var val) && val is T typed)
            return typed;
        return defaultValue;
    }
}
