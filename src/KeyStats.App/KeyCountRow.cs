using System.ComponentModel;
using System.Runtime.CompilerServices;
using KeyStats.Core;

namespace KeyStats.App;

public sealed class KeyCountRow(KeyDefinition definition) : INotifyPropertyChanged
{
    private ulong _count;
    private bool _isPressed;

    public KeyId Id => definition.Id;
    public string DisplayName => definition.DisplayName;
    public string Category => definition.Category;
    public int DisplayOrder => definition.DisplayOrder;

    public ulong Count
    {
        get => _count;
        set
        {
            if (_count == value)
            {
                return;
            }

            _count = value;
            OnPropertyChanged();
        }
    }

    public bool IsPressed
    {
        get => _isPressed;
        set
        {
            if (_isPressed == value)
            {
                return;
            }

            _isPressed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PressedText));
        }
    }

    public string PressedText => IsPressed ? "按下中" : string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
