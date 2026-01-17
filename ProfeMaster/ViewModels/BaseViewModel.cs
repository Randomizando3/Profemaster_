using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProfeMaster.ViewModels;

public abstract class BaseViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    string _status = "";
    public string Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    protected bool SetProperty<T>(ref T backing, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(backing, value)) return false;
        backing = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
