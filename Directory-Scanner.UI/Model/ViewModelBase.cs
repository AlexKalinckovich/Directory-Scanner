using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Directory_Scanner.UI.Model;

public class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!Equals(field, value))
        {
            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}