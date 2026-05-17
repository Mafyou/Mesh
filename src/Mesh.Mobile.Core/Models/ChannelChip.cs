using System.ComponentModel;

namespace Mesh.Mobile.Core.Models;

public class ChannelChip(byte id, string name) : INotifyPropertyChanged
{
    public byte Id { get; } = id;
    public string Name { get; } = name;

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
