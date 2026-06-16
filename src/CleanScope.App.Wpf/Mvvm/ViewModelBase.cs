using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CleanScope.App.Wpf.Mvvm;

/// <summary>最小 MVVM 基类: INotifyPropertyChanged + SetField 辅助 (不引第三方 MVVM 框架)。</summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
