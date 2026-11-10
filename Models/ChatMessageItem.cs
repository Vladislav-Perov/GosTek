/*
 * ==============================================================================
 * НАЗНАЧЕНИЕ: Модель элементов переписки с ИИ-помощником и карточки предложенных полей
 * СОДЕРЖИМОЕ: ChatMessageItem, ProposedFieldItem
 * ЗАВИСИМОСТИ: System.ComponentModel
 * ==============================================================================
 */
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GosTek.Models;

/// <summary>
/// Предложенное ИИ значение для одного поля шаблона.
/// </summary>
public class ProposedFieldItem
{
    public string FieldName { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// Сообщение в диалоговом окне ИИ-помощника.
/// </summary>
public class ChatMessageItem : INotifyPropertyChanged
{
    private string _text = string.Empty;
    private bool _isApplied;
    private bool _isCancelled;

    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    public bool IsUser { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string FormattedTime => CreatedAt.ToString("HH:mm");

    public List<ProposedFieldItem>? ProposedFields { get; set; }

    public bool HasProposedFields => ProposedFields is { Count: > 0 };

    public bool IsApplied
    {
        get => _isApplied;
        set
        {
            if (SetProperty(ref _isApplied, value))
            {
                OnPropertyChanged(nameof(CanAct));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool IsCancelled
    {
        get => _isCancelled;
        set
        {
            if (SetProperty(ref _isCancelled, value))
            {
                OnPropertyChanged(nameof(CanAct));
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public bool CanAct => HasProposedFields && !IsApplied && !IsCancelled;

    public string StatusText => IsApplied
        ? "✅ Данные применены к документу"
        : (IsCancelled ? "✕ Применение отменено" : string.Empty);

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
