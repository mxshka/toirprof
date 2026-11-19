using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ToirProf.Desktop.Services;
using ToirProf.Desktop.ViewModels.Screens;

namespace ToirProf.Desktop.Views;

public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();

        // Работа в модальном окне — тоже активность: автоблокировка не
        // должна срабатывать, пока мастер заполняет форму.
        Root.AddHandler(InputElement.PointerMovedEvent, (_, _) => ActivityTracker.Touch(), RoutingStrategies.Tunnel, true);
        Root.AddHandler(InputElement.KeyDownEvent, (_, _) => ActivityTracker.Touch(), RoutingStrategies.Tunnel, true);
    }

    public DialogWindow(DialogViewModel model)
        : this()
    {
        DataContext = model;
        Width = model.DialogWidth;
        model.CloseRequested += () => Close();
    }
}
