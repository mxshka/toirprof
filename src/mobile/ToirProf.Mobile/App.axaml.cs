using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ToirProf.Mobile.Services;
using ToirProf.Mobile.ViewModels;
using ToirProf.Mobile.Views;

namespace ToirProf.Mobile;

public partial class App : Application
{
    /// <summary>
    /// Возможности устройства. Android-проект подставляет AndroidPlatform до
    /// запуска Avalonia; на компьютере остаётся PreviewPlatform.
    /// </summary>
    public static IPlatformServices Platform { get; set; } = new PreviewPlatform();

    private ShellViewModel? _shell;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _shell ??= new ShellViewModel(Platform);

        if (ApplicationLifetime is IActivityApplicationLifetime activity)
        {
            activity.MainViewFactory = () => new MainView { DataContext = _shell };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView { DataContext = _shell };
        }
        else if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new Window
            {
                Title = "ТОиР-Проф · Бригада",
                Width = 400,
                Height = 820,
                Content = new MainView { DataContext = _shell },
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
