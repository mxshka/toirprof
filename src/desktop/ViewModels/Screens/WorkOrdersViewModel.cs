using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Заявки на ремонт и ТО — самый рабочий экран мастера.
///
/// Здесь виден весь жизненный цикл: создать, назначить бригаду, начать,
/// завершить, списать материал. Клиент не решает, можно ли завершить
/// заявку без причины неисправности или назначить занятую бригаду, —
/// это делает сервер, а клиент показывает его ответ.
/// </summary>
public sealed partial class WorkOrdersViewModel : PageViewModel
{
    public WorkOrdersViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
        // Плановые даты новой заявки заполнены заранее: пустые поля
        // выбора даты показывают английские подсказки, а заявку всё
        // равно заводят «на завтра-послезавтра», не в прошлое.
    }

    public ObservableCollection<WorkOrderRow> Rows { get; } = [];

    public ObservableCollection<CrewShort> Crews { get; } = [];

    public ObservableCollection<EquipmentShort> Equipment { get; } = [];

    public IReadOnlyList<string> StatusOptions { get; } = ["Создана", "Назначена", "В работе", "Ожидает материалов", "Выполнена", "Закрыта", "Отменена"];

    public IReadOnlyList<string> PriorityOptions { get; } = ["Низкий", "Средний", "Высокий", "Критический"];

    [ObservableProperty]
    private string? _statusFilter;

    [ObservableProperty]
    private string? _priorityFilter;

    [ObservableProperty]
    private Guid? _equipmentFilter;

    [ObservableProperty]
    private Guid? _crewFilter;

    // -------------------------------------------------------- новая заявка

    public ObservableCollection<WorkshopItem> Workshops { get; } = [];

    public IReadOnlyList<string> TypeOptions { get; } = ["Плановая", "Аварийная", "Инициативная"];

    [ObservableProperty]
    private WorkshopItem? _workshopFilter;

    [ObservableProperty]
    private string? _typeFilter;

    [ObservableProperty]
    private DateOnly? _fromFilter;

    [ObservableProperty]
    private DateOnly? _toFilter;

    /// <summary>Полная карточка выбранной заявки: хронология статусов и материалы.</summary>
    [ObservableProperty]
    private WorkOrderDetail? _detail;

    public ObservableCollection<WorkOrderHistoryItem> Timeline { get; } = [];

    // ------------------------------------------------------ текущая заявка

    [ObservableProperty]
    private WorkOrderRow? _selected;

    [ObservableProperty]
    private CrewShort? _assignCrew;

    [ObservableProperty]
    private string _failureReason = string.Empty;

    [ObservableProperty]
    private Guid? _writeOffMaterial;

    [ObservableProperty]
    private decimal _writeOffQuantity;

    public override async Task RefreshAsync()
    {
        await RunLoadAsync("Загружаем заявки…", async ct =>
        {
            var path =
                $"/api/work-orders?query={Uri.EscapeDataString(SearchTextOrEmpty)}" +
                $"&status={Uri.EscapeDataString(StatusFilter ?? string.Empty)}" +
                $"&priority={Uri.EscapeDataString(PriorityFilter ?? string.Empty)}" +
                $"&equipmentId={Fmt(EquipmentFilter)}&crewId={Fmt(CrewFilter)}" +
                $"&workshopId={Fmt(WorkshopFilter?.Id)}&orderType={Uri.EscapeDataString(TypeFilter ?? string.Empty)}" +
                $"&from={Fmt(FromFilter)}&to={Fmt(ToFilter)}" +
                $"&sort={Uri.EscapeDataString(SortField)}&order={SortOrder}" +
                $"&page={Page}&pageSize={PageSize}";

            var result = await Api!.GetAsync<PagedResult<WorkOrderRow>>(path, ct);

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message;
                OnPropertyChanged(nameof(ErrorText));
                Toasts!.Warn(result.Message);
                return;
            }

            Rows.Clear();
            foreach (var row in result.Data.Items)
            {
                Rows.Add(row);
            }

            Total = result.Data.Total;
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(PageCaption));

            await LoadLookupsAsync(ct);
        });
    }

    /// <summary>
    /// Справочники для выпадающих списков. Грузятся один раз при входе
    /// на экран: мастер выбирает бригаду и материал много раз подряд,
    /// и каждый раз дёргать сервер незачем.
    /// </summary>
    private async Task LoadLookupsAsync(CancellationToken ct)
    {
        var crews = await Api!.GetAsync<List<CrewShort>>("/api/crews/select", ct);
        if (crews.Success && crews.Data is not null)
        {
            Crews.Clear();
            foreach (var c in crews.Data)
            {
                Crews.Add(c);
            }
        }

        if (Workshops.Count == 0)
        {
            var workshops = await Api.GetAsync<List<WorkshopItem>>("/api/workshops", ct);
            if (workshops.Success && workshops.Data is not null)
            {
                foreach (var w in workshops.Data)
                {
                    Workshops.Add(w);
                }
            }
        }

        var equipment = await Api.GetAsync<List<EquipmentShort>>("/api/equipment/select", ct);
        if (equipment.Success && equipment.Data is not null)
        {
            Equipment.Clear();
            foreach (var e in equipment.Data)
            {
                Equipment.Add(e);
            }
        }
    }

    [RelayCommand]
    private void Select(WorkOrderRow? row)
    {
        Selected = row;
        if (row is not null)
        {
            AssignCrew = null;
            FailureReason = string.Empty;
            WriteOffQuantity = 0m;
        }
    }

    /// <summary>Новая заявка в модальной форме (Ctrl+N).</summary>
    [RelayCommand]
    public async Task CreateAsync(string? orderType)
    {
        var form = new WorkOrderEditViewModel(Api!, Toasts!, Log!, string.IsNullOrEmpty(orderType) ? "Аварийная" : orderType);
        if (await Dialogs.ShowAsync(form))
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        StatusFilter = null;
        PriorityFilter = null;
        CrewFilter = null;
        WorkshopFilter = null;
        TypeFilter = null;
        FromFilter = null;
        ToFilter = null;
        SearchText = null;
        Page = 1;
        await RefreshAsync();
    }

    partial void OnSelectedChanged(WorkOrderRow? value)
    {
        _ = LoadDetailAsync(value);
    }

    private async Task LoadDetailAsync(WorkOrderRow? row)
    {
        Timeline.Clear();
        Detail = null;
        if (row is null)
        {
            return;
        }

        var result = await Api!.GetAsync<WorkOrderDetail>($"/api/work-orders/{row.Id}");
        if (!result.Success || result.Data is null || Selected?.Id != row.Id)
        {
            return;
        }

        Detail = result.Data;
        foreach (var item in result.Data.History.OrderBy(h => h.ChangedAt))
        {
            Timeline.Add(item);
        }
    }

    [RelayCommand]
    private async Task AssignCrewAsync()
    {
        if (Selected is null || AssignCrew is null)
        {
            Toasts!.Warn("Выберите заявку и бригаду.");
            return;
        }

        await RunLoadAsync("Назначаем бригаду…", async ct =>
        {
            var result = await Api!.PostAsync<object>(
                $"/api/work-orders/{Selected.Id}/crew",
                new { crewId = AssignCrew.Id },
                ct);

            if (!result.Success)
            {
                ShowError(result);
                return;
            }

            Toasts!.Success($"Бригада {AssignCrew.Name} назначена.");
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await RunLoadAsync("Начинаем работу…", async ct =>
        {
            var result = await Api!.PostAsync<object>(
                $"/api/work-orders/{Selected.Id}/start",
                new { actualStart = DateTimeOffset.UtcNow },
                ct);

            if (!result.Success)
            {
                ShowError(result);
                return;
            }

            Toasts!.Success("Заявка переведена в работу.");
            await RefreshAsync();
        });
    }

    /// <summary>
    /// Завершение требует причину неисправности: без неё сервер
    /// отвечает 400, и поле подсвечивается, чтобы мастер не искал
    /// причину ошибки в другом месте.
    /// </summary>
    /// <summary>Завершение: причина неисправности и список материалов в модальной форме.</summary>
    [RelayCommand]
    private async Task CompleteAsync()
    {
        if (Selected is null)
        {
            return;
        }

        if (Selected.Status is not ("В работе" or "Ожидает материалов"))
        {
            Toasts!.Warn("Завершить можно заявку в статусе «В работе».");
            return;
        }

        if (await Dialogs.ShowAsync(new CompleteOrderViewModel(Api!, Toasts!, Log!, Selected)))
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task CloseOrderAsync()
    {
        if (Selected is null)
        {
            return;
        }

        if (Selected.Status != "Выполнена")
        {
            Toasts!.Warn("Закрыть можно только выполненную заявку.");
            return;
        }

        await RunLoadAsync("Закрываем заявку…", async ct =>
        {
            var result = await Api!.PostAsync<object>(
                $"/api/work-orders/{Selected.Id}/close",
                new { },
                ct);

            if (!result.Success)
            {
                ShowError(result);
                return;
            }

            Toasts!.Success("Заявка закрыта, простой зафиксирован.");
            await RefreshAsync();
        });
    }

    /// <summary>Списание материала. Клиент шлёт количество, стоимость считает база.</summary>
    [RelayCommand]
    private async Task WriteOffAsync()
    {
        if (Selected is null || WriteOffMaterial is null)
        {
            Toasts!.Warn("Выберите заявку и материал.");
            return;
        }

        if (WriteOffQuantity <= 0m)
        {
            Toasts!.Warn("Количество должно быть больше нуля.");
            return;
        }

        await RunLoadAsync("Списываем материал…", async ct =>
        {
            var result = await Api!.PostAsync<object>(
                $"/api/work-orders/{Selected.Id}/materials",
                new { materialId = WriteOffMaterial.Value, quantity = WriteOffQuantity },
                ct);

            if (!result.Success)
            {
                ShowError(result);
                return;
            }

            Toasts!.Success("Материал списан, остаток на складе уменьшен.");
            WriteOffQuantity = 0m;
            await RefreshAsync();
        });
    }

    private static string Fmt(Guid? value) => value?.ToString() ?? string.Empty;

    private static string Fmt(DateOnly? value) =>
        value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
