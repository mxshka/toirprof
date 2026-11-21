using Avalonia.Controls;
using Avalonia.Controls.Templates;
using ToirProf.Mobile.ViewModels;

namespace ToirProf.Mobile;

/// <summary>Экран по модели: ToirProf.Mobile.ViewModels.XxxViewModel → ToirProf.Mobile.Views.XxxView.</summary>
public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? data)
    {
        if (data is null)
        {
            return null;
        }

        var name = data.GetType().FullName!.Replace("ViewModels", "Views").Replace("ViewModel", "View");
        var type = typeof(ViewLocator).Assembly.GetType(name);
        return type is null
            ? new TextBlock { Text = "Нет экрана: " + name }
            : (Control)Activator.CreateInstance(type)!;
    }

    public bool Match(object? data) => data is PageViewModel;
}
