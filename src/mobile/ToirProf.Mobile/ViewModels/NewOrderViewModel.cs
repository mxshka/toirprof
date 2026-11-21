using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Models;
using ToirProf.Mobile.Services;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Новая заявка с места: оборудование, суть неисправности, фото дефекта.
/// Координаты проставляются автоматически по геолокации. Без связи заявка
/// сохраняется черновиком или ставится в очередь.
/// </summary>
public sealed partial class NewOrderViewModel : PageViewModel
{
    private readonly Guid? _equipmentId;

    public NewOrderViewModel(ShellViewModel shell, Guid? equipmentId)
        : base(shell)
    {
        _equipmentId = equipmentId;
        foreach (var e in shell.Store.Equipment)
        {
            Equipment.Add(e);
        }

        foreach (var d in shell.Store.Drafts.OrderByDescending(d => d.CreatedAt))
        {
            Drafts.Add(d);
        }

        SelectedEquipment = Equipment.FirstOrDefault(e => e.Id == equipmentId);
    }

    public override string Title => "Новая заявка";

    public override string Tab => "new";

    public ObservableCollection<EquipmentOption> Equipment { get; } = [];

    public ObservableCollection<OrderDraft> Drafts { get; } = [];

    public IReadOnlyList<string> Types { get; } = ["Аварийная", "Инициативная"];

    public IReadOnlyList<string> Priorities { get; } = ["Критический", "Высокий", "Средний", "Низкий"];

    [ObservableProperty]
    private EquipmentOption? _selectedEquipment;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReasonRequired))]
    private string _orderType = "Аварийная";

    [ObservableProperty]
    private string _priority = "Высокий";

    [ObservableProperty]
    private string _orderTitle = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _failureReason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationCaption))]
    private GeoPoint? _location;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PhotoCaption))]
    private PhotoData? _photo;

    [ObservableProperty]
    private string? _formError;

    public bool ReasonRequired => OrderType == "Аварийная";

    public bool HasDrafts => Drafts.Count > 0;

    public string LocationCaption => Location is null ? "Координаты: определяются…" : $"Координаты: {Location}";

    public string PhotoCaption => Photo is null ? "Фото дефекта не приложено" : $"Фото приложено ({Photo.Content.Length / 1024 + 1} КБ)";

    public override async Task OnShowAsync()
    {
        Location ??= await Shell.Platform.GetLocationAsync();
        if (Location is null)
        {
            OnPropertyChanged(nameof(LocationCaption));
        }
    }

    [RelayCommand]
    private Task TakePhotoAsync() => BusyAsync(async () =>
    {
        Photo = await Shell.Platform.TakePhotoAsync() ?? Photo;
    });

    [RelayCommand]
    private Task ScanEquipmentAsync() => BusyAsync(async () =>
    {
        var code = await Shell.Platform.ScanCodeAsync();
        var match = Equipment.FirstOrDefault(e =>
            string.Equals(e.InventoryNumber, code?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            Shell.ShowToast(code is null ? "Код не распознан." : $"Оборудование «{code}» не найдено.");
            return;
        }

        SelectedEquipment = match;
    });

    [RelayCommand]
    private void SaveDraft()
    {
        var draft = BuildDraft(requireAll: false);
        if (draft is null)
        {
            return;
        }

        Shell.Store.Drafts.Add(draft);
        Shell.Store.SaveDrafts();
        Drafts.Insert(0, draft);
        OnPropertyChanged(nameof(HasDrafts));
        Clear();
        Shell.ShowToast("Черновик сохранён на телефоне.");
    }

    [RelayCommand]
    private Task SubmitAsync() => BusyAsync(async () =>
    {
        var draft = BuildDraft(requireAll: true);
        if (draft is null)
        {
            return;
        }

        if (Photo is not null)
        {
            await Shell.Service.AttachPhotoAsync(null, draft.LocalId, "before", Photo, "Снято при создании заявки");
        }

        Shell.ShowToast(await Shell.Service.SubmitDraftAsync(draft));
        Clear();
        Shell.UpdateConnection();
        await Shell.Service.RefreshAsync();
    });

    [RelayCommand]
    private Task SendDraftAsync(OrderDraft? draft) => BusyAsync(async () =>
    {
        if (draft is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(draft.Title) || string.IsNullOrWhiteSpace(draft.Description) || draft.EquipmentId == Guid.Empty)
        {
            Shell.ShowToast("Черновик не заполнен: откройте его, дополните и отправьте.");
            return;
        }

        Drafts.Remove(draft);
        OnPropertyChanged(nameof(HasDrafts));
        Shell.ShowToast(await Shell.Service.SubmitDraftAsync(draft));
        Shell.UpdateConnection();
    });

    [RelayCommand]
    private void EditDraft(OrderDraft? draft)
    {
        if (draft is null)
        {
            return;
        }

        SelectedEquipment = Equipment.FirstOrDefault(e => e.Id == draft.EquipmentId);
        OrderType = draft.OrderType;
        Priority = draft.Priority;
        OrderTitle = draft.Title;
        Description = draft.Description;
        FailureReason = draft.FailureReason ?? string.Empty;
        Shell.Store.Drafts.RemoveAll(d => d.LocalId == draft.LocalId);
        Shell.Store.SaveDrafts();
        Drafts.Remove(draft);
        OnPropertyChanged(nameof(HasDrafts));
    }

    private OrderDraft? BuildDraft(bool requireAll)
    {
        var errors = new List<string>();
        if (SelectedEquipment is null)
        {
            errors.Add("выберите оборудование");
        }

        if (string.IsNullOrWhiteSpace(OrderTitle))
        {
            errors.Add("кратко назовите неисправность");
        }

        if (requireAll && string.IsNullOrWhiteSpace(Description))
        {
            errors.Add("опишите, что произошло");
        }

        if (requireAll && ReasonRequired && string.IsNullOrWhiteSpace(FailureReason))
        {
            errors.Add("для аварийной заявки укажите причину");
        }

        if (errors.Count > 0)
        {
            FormError = "Нужно: " + string.Join(", ", errors) + ".";
            return null;
        }

        FormError = null;
        return new OrderDraft
        {
            EquipmentId = SelectedEquipment!.Id,
            EquipmentCaption = SelectedEquipment.InventoryNumber,
            OrderType = OrderType,
            Priority = Priority,
            Title = OrderTitle.Trim(),
            Description = Description.Trim(),
            FailureReason = string.IsNullOrWhiteSpace(FailureReason) ? null : FailureReason.Trim(),
            Latitude = Location?.Latitude,
            Longitude = Location?.Longitude,
        };
    }

    private void Clear()
    {
        OrderTitle = string.Empty;
        Description = string.Empty;
        FailureReason = string.Empty;
        Photo = null;
        SelectedEquipment = Equipment.FirstOrDefault(e => e.Id == _equipmentId);
    }
}
