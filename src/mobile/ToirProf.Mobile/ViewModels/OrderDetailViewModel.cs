using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Карточка заявки: начать работы, завершить с причиной и кратким отчётом,
/// сфотографировать дефект «до» и результат «после».
/// </summary>
public sealed partial class OrderDetailViewModel(ShellViewModel shell, MobileOrder order) : PageViewModel(shell)
{
    public MobileOrder Order { get; } = order;

    public override string Title => Order.Number;

    public ObservableCollection<PhotoInfo> Photos { get; } = [];

    [ObservableProperty]
    private bool _isCompleting;

    [ObservableProperty]
    private string _reason = string.Empty;

    [ObservableProperty]
    private string _report = string.Empty;

    [ObservableProperty]
    private string? _formError;

    public string Status => Order.Status + (Order.HasPending ? " · ждёт отправки" : string.Empty);

    public bool CanStart => Order.CanStart;

    public bool CanComplete => Order.CanComplete;

    public string Dates =>
        $"План: {Order.PlannedStart?.ToString("dd.MM.yyyy") ?? "—"} — {Order.PlannedEnd?.ToString("dd.MM.yyyy") ?? "—"}" +
        (Order.ActualStart is { } s ? $"\nНачато: {s.ToLocalTime():dd.MM HH:mm}" : string.Empty);

    public string PhotosCaption => Photos.Count > 0
        ? $"Фото: {Photos.Count}"
        : Order.PhotoCount > 0 ? $"Фото на сервере: {Order.PhotoCount}" : "Фото пока нет";

    public override async Task OnShowAsync()
    {
        var result = await Shell.Api.GetAsync<List<PhotoInfo>>($"/api/work-orders/{Order.Id}/photos");
        if (result.Success && result.Data is not null)
        {
            Photos.Clear();
            foreach (var photo in result.Data)
            {
                Photos.Add(photo);
            }
        }

        OnPropertyChanged(nameof(PhotosCaption));
    }

    [RelayCommand]
    private Task StartAsync() => BusyAsync(async () =>
    {
        Shell.ShowToast(await Shell.Service.StartAsync(Order));
        Refresh();
    });

    [RelayCommand]
    private void BeginComplete()
    {
        FormError = null;
        Reason = Order.FailureReason ?? string.Empty;
        IsCompleting = true;
    }

    [RelayCommand]
    private Task CompleteAsync() => BusyAsync(async () =>
    {
        if (string.IsNullOrWhiteSpace(Reason))
        {
            FormError = "Укажите причину неисправности — без неё заявку не закрыть.";
            return;
        }

        Shell.ShowToast(await Shell.Service.CompleteAsync(Order, Reason, Report));
        IsCompleting = false;
        Refresh();
    });

    [RelayCommand]
    private Task PhotoBeforeAsync() => PhotoAsync("before");

    [RelayCommand]
    private Task PhotoAfterAsync() => PhotoAsync("after");

    [RelayCommand]
    private void OpenEquipment() => Shell.Navigate(new EquipmentViewModel(Shell, Order.InventoryNumber));

    private Task PhotoAsync(string kind) => BusyAsync(async () =>
    {
        var photo = await Shell.Platform.TakePhotoAsync();
        if (photo is null)
        {
            Shell.ShowToast("Съёмка отменена.");
            return;
        }

        Shell.ShowToast(await Shell.Service.AttachPhotoAsync(Order.Id, null, kind, photo, null));
        await OnShowAsync();
        Refresh();
    });

    private void Refresh()
    {
        Shell.UpdateConnection();
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanComplete));
        OnPropertyChanged(nameof(Dates));
        OnPropertyChanged(nameof(PhotosCaption));
    }
}
