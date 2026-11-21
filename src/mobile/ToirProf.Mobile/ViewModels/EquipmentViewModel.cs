using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Сканер: QR-код на табличке станка содержит инвентарный номер. По нему
/// открывается карточка: паспорт, активные заявки, типовые неисправности
/// и ближайшее ТО с инструкцией. Номер можно ввести и вручную.
/// </summary>
public sealed partial class EquipmentViewModel : PageViewModel
{
    private readonly string? _initialCode;

    public EquipmentViewModel(ShellViewModel shell, string? code)
        : base(shell)
    {
        _initialCode = code;
        Code = code ?? string.Empty;
    }

    public override string Title => Card?.InventoryNumber ?? "Оборудование";

    public override string Tab => "scan";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCard), nameof(Title))]
    private MobileEquipment? _card;

    [ObservableProperty]
    private string _code = string.Empty;

    [ObservableProperty]
    private string? _hint = "Наведите камеру на QR-код таблички оборудования или введите инвентарный номер.";

    public bool HasCard => Card is not null;

    public override Task OnShowAsync() =>
        _initialCode is not null && Card is null ? LoadAsync(_initialCode) : Task.CompletedTask;

    [RelayCommand]
    private Task ScanAsync() => BusyAsync(async () =>
    {
        var code = await Shell.Platform.ScanCodeAsync();
        if (string.IsNullOrWhiteSpace(code))
        {
            Shell.ShowToast("Код не распознан — попробуйте ещё раз или введите номер.");
            return;
        }

        Code = code.Trim();
        await LoadCoreAsync(Code);
    });

    [RelayCommand]
    private Task FindAsync() => LoadAsync(Code);

    [RelayCommand]
    private void CreateOrder()
    {
        if (Card is not null)
        {
            Shell.Navigate(new NewOrderViewModel(Shell, Card.Id));
        }
    }

    [RelayCommand]
    private void OpenOrder(MobileOrder? order)
    {
        if (order is not null)
        {
            Shell.Navigate(new OrderDetailViewModel(Shell, order));
        }
    }

    private Task LoadAsync(string code) => BusyAsync(() => LoadCoreAsync(code));

    private async Task LoadCoreAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            Hint = "Введите инвентарный номер, например ТС-11.";
            return;
        }

        var result = await Shell.Api.GetAsync<MobileEquipment>($"/api/mobile/equipment/{Uri.EscapeDataString(code.Trim())}");
        if (result.Success && result.Data is not null)
        {
            Card = result.Data;
            Hint = null;
            return;
        }

        if (result.IsOffline)
        {
            // Без связи — карточка из кэша: справочник оборудования и заявки бригады.
            var cached = Shell.Store.Equipment.FirstOrDefault(e =>
                string.Equals(e.InventoryNumber, code.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cached is not null)
            {
                Card = new MobileEquipment
                {
                    Id = cached.Id,
                    InventoryNumber = cached.InventoryNumber,
                    Name = cached.Name,
                    Status = cached.Status,
                    WorkshopName = "нет связи — данные из кэша",
                    ActiveOrders = Shell.Store.Orders.Where(o => o.EquipmentId == cached.Id).ToList(),
                };
                Hint = "Нет связи: показаны сохранённые данные.";
                Shell.UpdateConnection();
                return;
            }
        }

        Card = null;
        Hint = result.IsOffline ? "Нет связи, а в кэше такого номера нет." : result.Message;
    }
}
