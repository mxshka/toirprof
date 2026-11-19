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

/// <summary>Новая запись графика ТО: оборудование, вид обслуживания и дата.</summary>
public sealed partial class ScheduleAddViewModel : DialogViewModel
{
    public ScheduleAddViewModel(ApiClient api, ToastService toasts, FileLogger log, DateOnly? date)
        : base(api, toasts, log)
    {
        PlannedDate = date ?? DateOnly.FromDateTime(DateTime.Today).AddDays(1);
    }

    public override string Title => "Новая запись графика ТО";

    public ObservableCollection<EquipmentShort> Equipment { get; } = [];

    public ObservableCollection<MaintenanceTypeRow> Types { get; } = [];

    [ObservableProperty]
    private EquipmentShort? _selectedEquipment;

    [ObservableProperty]
    private MaintenanceTypeRow? _selectedType;

    [ObservableProperty]
    private DateOnly? _plannedDate;

    [ObservableProperty]
    private string? _notes;

    public override async Task RefreshAsync()
    {
        var equipment = await Api!.GetAsync<List<EquipmentShort>>("/api/equipment/select");
        if (equipment.Success && equipment.Data is not null)
        {
            Equipment.Clear();
            foreach (var e in equipment.Data.Where(e => e.Status != "Выведено из эксплуатации"))
            {
                Equipment.Add(e);
            }
        }

        var types = await Api.GetAsync<List<MaintenanceTypeRow>>("/api/maintenance-types");
        if (types.Success && types.Data is not null)
        {
            Types.Clear();
            foreach (var t in types.Data)
            {
                Types.Add(t);
            }
        }

        if (!equipment.Success || !types.Success)
        {
            SetErrors("Справочники не загрузились: " + (equipment.Success ? types.Message : equipment.Message));
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var errors = new List<string>();
        if (SelectedEquipment is null)
        {
            errors.Add("Выберите оборудование.");
        }

        if (SelectedType is null)
        {
            errors.Add("Выберите вид обслуживания.");
        }

        if (PlannedDate is null)
        {
            errors.Add("Укажите плановую дату.");
        }

        if (errors.Count > 0)
        {
            SetErrors(errors);
            return;
        }

        var result = await Api!.PostAsync<ScheduleRow>("/api/maintenance-schedule", new
        {
            equipmentId = SelectedEquipment!.Id,
            maintenanceTypeId = SelectedType!.Id,
            plannedDate = PlannedDate,
            notes = Notes,
        });

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

        Toasts!.Success("Запись добавлена в график ТО.");
        CloseWith(true);
    }
}
