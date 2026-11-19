using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Модальная форма добавления и редактирования оборудования.
/// Даты проверяются до отправки: ввод в работу не раньше ввода в
/// эксплуатацию и не позже сегодняшнего дня, списание строго позже ввода.
/// </summary>
public sealed partial class EquipmentEditViewModel : DialogViewModel
{
    private readonly Guid? _id;
    private readonly Guid? _workshopId;

    public EquipmentEditViewModel(ApiClient api, ToastService toasts, FileLogger log, EquipmentCard? source)
        : base(api, toasts, log)
    {
        _id = source?.Id;
        _workshopId = source?.WorkshopId;

        if (source is null)
        {
            CommissionDate = DateOnly.FromDateTime(DateTime.Today);
            OperationDate = CommissionDate;
            return;
        }

        InventoryNumber = source.InventoryNumber;
        Name = source.Name;
        Manufacturer = source.Manufacturer;
        Model = source.Model;
        SerialNumber = source.SerialNumber;
        CommissionDate = source.CommissionDate;
        OperationDate = source.OperationDate;
        DecommissionDate = source.DecommissionDate;
        Status = source.Status;
        Criticality = source.Criticality;
        Location = source.Location;
        Notes = source.Notes;
    }

    public override string Title => IsNew ? "Новое оборудование" : $"Оборудование {InventoryNumber}";

    public override double DialogWidth => 660;

    public bool IsNew => _id is null;

    public bool IsExisting => _id is not null;

    public ObservableCollection<WorkshopItem> Workshops { get; } = [];

    public IReadOnlyList<string> StatusOptions { get; } =
        ["В работе", "В ремонте", "В простое", "Выведено из эксплуатации"];

    [ObservableProperty]
    private string _inventoryNumber = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private WorkshopItem? _workshop;

    [ObservableProperty]
    private string? _manufacturer;

    [ObservableProperty]
    private string? _model;

    [ObservableProperty]
    private string? _serialNumber;

    [ObservableProperty]
    private DateOnly? _commissionDate;

    [ObservableProperty]
    private DateOnly? _operationDate;

    [ObservableProperty]
    private DateOnly? _decommissionDate;

    [ObservableProperty]
    private string _status = "В работе";

    [ObservableProperty]
    private decimal? _criticality = 3;

    [ObservableProperty]
    private string? _location;

    [ObservableProperty]
    private string? _notes;

    public override async Task RefreshAsync()
    {
        var result = await Api!.GetAsync<List<WorkshopItem>>("/api/workshops");
        if (!result.Success || result.Data is null)
        {
            SetErrors("Справочник цехов не загрузился: " + result.Message);
            return;
        }

        Workshops.Clear();
        foreach (var w in result.Data)
        {
            Workshops.Add(w);
        }

        Workshop = Workshops.FirstOrDefault(w => w.Id == _workshopId) ?? Workshops.FirstOrDefault();
    }

    /// <summary>Проверка формы; пустой список — можно сохранять.</summary>
    public static List<string> Validate(
        string? inventoryNumber,
        string? name,
        Guid? workshopId,
        DateOnly? commission,
        DateOnly? operation,
        DateOnly? decommission,
        DateOnly today)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(inventoryNumber))
        {
            errors.Add("Укажите инвентарный номер.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Укажите наименование.");
        }

        if (workshopId is null)
        {
            errors.Add("Выберите цех.");
        }

        if (commission is null)
        {
            errors.Add("Укажите дату ввода в эксплуатацию.");
        }
        else
        {
            if (commission > today)
            {
                errors.Add("Дата ввода в эксплуатацию не может быть в будущем.");
            }

            if (operation is { } op && (op < commission || op > today))
            {
                errors.Add("Дата ввода в работу — не раньше ввода в эксплуатацию и не позже сегодняшнего дня.");
            }

            if (decommission is { } dec && dec <= commission)
            {
                errors.Add("Дата списания должна быть позже даты ввода в эксплуатацию.");
            }
        }

        return errors;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var errors = Validate(InventoryNumber, Name, Workshop?.Id, CommissionDate, OperationDate, DecommissionDate,
            DateOnly.FromDateTime(DateTime.Today));
        var criticality = (int)(Criticality ?? 3);
        if (criticality is < 1 or > 5)
        {
            errors.Add("Критичность — от 1 до 5.");
        }

        if (errors.Count > 0)
        {
            SetErrors(errors);
            return;
        }

        SetErrors();
        IsBusy = true;
        try
        {
            ApiResult<EquipmentCard> result;
            if (IsNew)
            {
                result = await Api!.PostAsync<EquipmentCard>("/api/equipment", new
                {
                    inventoryNumber = InventoryNumber.Trim(),
                    name = Name.Trim(),
                    workshopId = Workshop!.Id,
                    manufacturer = Manufacturer,
                    model = Model,
                    serialNumber = SerialNumber,
                    commissionDate = CommissionDate,
                    operationDate = OperationDate,
                    decommissionDate = DecommissionDate,
                    status = Status,
                    criticality,
                    location = Location,
                    notes = Notes,
                });
            }
            else
            {
                result = await Api!.PutAsync<EquipmentCard>($"/api/equipment/{_id}", new
                {
                    name = Name.Trim(),
                    workshopId = Workshop!.Id,
                    manufacturer = Manufacturer,
                    model = Model,
                    serialNumber = SerialNumber,
                    commissionDate = CommissionDate,
                    operationDate = OperationDate,
                    decommissionDate = DecommissionDate,
                    status = Status,
                    criticality,
                    location = Location,
                    notes = Notes,
                });
            }

            if (!result.Success)
            {
                if (result.StatusCode is 400 or 409)
                {
                    SetErrors(result.Message);
                }
                else
                {
                    ShowError(result);
                }

                return;
            }

            Toasts!.Success(IsNew ? $"Оборудование {InventoryNumber} добавлено." : "Изменения сохранены.");
            CloseWith(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
