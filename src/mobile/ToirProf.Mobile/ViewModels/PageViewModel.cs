using CommunityToolkit.Mvvm.ComponentModel;

namespace ToirProf.Mobile.ViewModels;

/// <summary>Основа экрана: заголовок, признак загрузки и доступ к оболочке.</summary>
public abstract partial class PageViewModel(ShellViewModel shell) : ObservableObject
{
    public ShellViewModel Shell { get; } = shell;

    public abstract string Title { get; }

    /// <summary>Показывать ли нижние вкладки (на экране входа их нет).</summary>
    public virtual bool ShowTabs => true;

    /// <summary>Вкладка, которую подсвечивать: orders, scan, new, more.</summary>
    public virtual string Tab => "orders";

    [ObservableProperty]
    private bool _isBusy;

    public virtual Task OnShowAsync() => Task.CompletedTask;

    protected async Task BusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Shell.ShowToast("Ошибка: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
