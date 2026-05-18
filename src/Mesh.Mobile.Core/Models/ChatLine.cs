using System.ComponentModel;

namespace Mesh.Mobile.Core.Models;

public abstract class ChatLine { }

public sealed class MessageLine(MessageItem item) : ChatLine, INotifyPropertyChanged
{
    public MessageItem Item { get; } = item;

    private bool _isFailed;
    public bool IsFailed
    {
        get => _isFailed;
        set
        {
            if (_isFailed == value) return;
            _isFailed = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFailed)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DateLine(string label) : ChatLine
{
    public string Label { get; } = label;
}
