using System.ComponentModel;

namespace Mesh.Mobile.Core.Models;

public class NodeContact : INotifyPropertyChanged
{
    public byte Id { get; init; }
    public string Label => Id == 0xFF ? "Tous" : $"0x{Id:X2}";

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
