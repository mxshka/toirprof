using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Любое движение мыши или нажатие клавиши сбрасывает таймер
        // автоблокировки. Обработчики туннельные и видят даже то, что
        // уже обработали дочерние элементы.
        Root.AddHandler(InputElement.PointerMovedEvent, (_, _) => ActivityTracker.Touch(), RoutingStrategies.Tunnel, true);
        Root.AddHandler(InputElement.PointerPressedEvent, (_, _) => ActivityTracker.Touch(), RoutingStrategies.Tunnel, true);
        Root.AddHandler(InputElement.KeyDownEvent, (_, _) => ActivityTracker.Touch(), RoutingStrategies.Tunnel, true);

        // Размер окна — тоже пользовательская настройка.
        var settings = UserSettings.Current;
        if (settings.WindowWidth is > 900 and < 5000 && settings.WindowHeight is > 600 and < 4000)
        {
            Width = settings.WindowWidth.Value;
            Height = settings.WindowHeight.Value;
        }

        Closing += (_, _) =>
        {
            settings.WindowWidth = Width;
            settings.WindowHeight = Height;
            settings.Save();
        };
    }
}
