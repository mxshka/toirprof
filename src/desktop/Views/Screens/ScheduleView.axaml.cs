using Avalonia.Controls;
using Avalonia.Input;
using ToirProf.Desktop.ViewModels.Screens;

namespace ToirProf.Desktop.Views.Screens;

public partial class ScheduleView : UserControl
{
    public ScheduleView()
    {
        InitializeComponent();
    }

    /// <summary>Двойной щелчок по дню календаря — добавить запись графика на эту дату.</summary>
    private void OnDayDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ScheduleViewModel vm && (sender as Control)?.DataContext is CalendarDay day)
        {
            vm.AddEntryCommand.Execute(day);
        }
    }
}
