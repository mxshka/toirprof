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
/// Модальная форма новой заявки: плановая, аварийная или инициативная.
/// Перед отправкой форма проверяется той же функцией
/// <see cref="Calculators.ValidateWorkOrder"/>, что покрыта модульными тестами.
/// </summary>
public sealed partial class WorkOrderEditViewModel : DialogViewModel
{
    public WorkOrderEditViewModel(ApiClient api, ToastService toasts, FileLogger log, string orderType = "Аварийная")
        : base(api, toasts, log)
    {
        OrderType = orderType;
        PlannedStart = DateOnly.FromDateTime(DateTime.Today);
        PlannedEnd = PlannedStart.Value.AddDays(1);
        if (orderType == "Аварийная")
        {
            Priority = "Критический";
        }
    }

    public override string Title => "Новая заявка";

    public override double DialogWidth => 620;

    public ObservableCollection<EquipmentShort> Equipment { get; } = [];

    public ObservableCollection<CrewShort> Crews { get; } = [];

    public IReadOnlyList<string> TypeOptions { get; } = ["Плановая", "Аварийная", "Инициативная"];

    public IReadOnlyList<string> PriorityOptions { get; } = ["Низкий", "Средний", "Высокий", "Критический"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmergency))]
    private string _orderType = "Аварийная";

    [ObservableProperty]
    private EquipmentShort? _selectedEquipment;

    [ObservableProperty]
    private string _orderTitle = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _priority = "Средний";

    [ObservableProperty]
    private DateOnly? _plannedStart;

    [ObservableProperty]
    private DateOnly? _plannedEnd;

    [ObservableProperty]
    private CrewShort? _crew;

    [ObservableProperty]
    private string? _failureReason;

    /// <summary>Для аварийной заявки причина неисправности обязательна.</summary>
    public bool IsEmergency => OrderType == "Аварийная";

    partial void OnOrderTypeChanged(string value)
    {
        if (value == "Аварийная" && Priority is "Низкий" or "Средний")
        {
            Priority = "Высокий";
        }
    }

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
        else
        {
            SetErrors("Список оборудования не загрузился: " + equipment.Message);
        }

        var crews = await Api.GetAsync<List<CrewShort>>("/api/crews/select");
        if (crews.Success && crews.Data is not null)
        {
            Crews.Clear();
            foreach (var c in crews.Data)
            {
                Crews.Add(c);
            }
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var draft = new WorkOrderDraft(
            SelectedEquipment?.Id ?? Guid.Empty,
            Description,
            Priority,
            "Создана",
            PlannedStart,
            PlannedEnd,
            FailureReason);
        var check = Calculators.ValidateWorkOrder(draft);
        var errors = check.Errors.ToList();
        if (string.IsNullOrWhiteSpace(OrderTitle))
        {
            errors.Add("Укажите тему заявки.");
        }

        if (IsEmergency && string.IsNullOrWhiteSpace(FailureReason) && !errors.Any(e => e.Contains("аварийной", StringComparison.Ordinal)))
        {
            errors.Add("Для аварийной заявки укажите причину неисправности.");
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
            var result = await Api!.PostAsync<WorkOrderRow>("/api/work-orders", new
            {
                equipmentId = SelectedEquipment!.Id,
                title = OrderTitle.Trim(),
                description = Description.Trim(),
                priority = Priority,
                orderType = OrderType,
                plannedStart = PlannedStart,
                plannedEnd = PlannedEnd,
                crewId = Crew?.Id,
                failureReason = string.IsNullOrWhiteSpace(FailureReason) ? null : FailureReason.Trim(),
            });

            if (!result.Success || result.Data is null)
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

            CreatedNumber = result.Data.Number;
            Toasts!.Success($"Создана заявка {result.Data.Number} ({OrderType.ToLowerInvariant()}).");
            CloseWith(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    public string? CreatedNumber { get; private set; }
}
