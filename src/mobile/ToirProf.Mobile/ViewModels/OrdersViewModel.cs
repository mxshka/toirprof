using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.ViewModels;

/// <summary>Заявки своей бригады: сначала критические, затем по сроку.</summary>
public sealed partial class OrdersViewModel : PageViewModel
{
    public OrdersViewModel(ShellViewModel shell)
        : base(shell)
    {
        Reload();
    }

    public override string Title => "Заявки бригады";

    public ObservableCollection<MobileOrder> Items { get; } = [];

    [ObservableProperty]
    private bool _showDone;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _crewCaption = string.Empty;

    public bool IsEmpty => Items.Count == 0;

    partial void OnShowDoneChanged(bool value) => Reload();

    public void Reload()
    {
        var all = Shell.Store.Orders;
        Items.Clear();
        foreach (var order in all.Where(o => ShowDone || o.Status != "Выполнена"))
        {
            Items.Add(order);
        }

        var inWork = all.Count(o => o.Status == "В работе");
        var critical = all.Count(o => o.Priority == "Критический" && o.Status != "Выполнена");
        var when = Shell.Store.OrdersLoadedAt is { } at ? $"обновлено {at:HH:mm}" : "сохранённые данные";
        Summary = $"Активных {all.Count(o => o.Status != "Выполнена")} · в работе {inWork} · критических {critical} · {when}";
        CrewCaption = Shell.Store.Me is { } me ? $"{me.FullName} · {me.CrewsCaption}" : string.Empty;
        OnPropertyChanged(nameof(IsEmpty));
    }

    public override Task OnShowAsync()
    {
        Reload();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task RefreshAsync() => BusyAsync(async () =>
    {
        await Shell.Service.FlushQueueAsync();
        var online = await Shell.Service.RefreshAsync();
        Shell.UpdateConnection();
        Reload();
        if (!online)
        {
            Shell.ShowToast("Нет связи — показаны сохранённые заявки.");
        }
    });

    [RelayCommand]
    private void Open(MobileOrder? order)
    {
        if (order is not null)
        {
            Shell.Navigate(new OrderDetailViewModel(Shell, order));
        }
    }
}
