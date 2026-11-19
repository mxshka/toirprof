using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// График планового ТО. Ключевое действие экрана — отметить выполнение:
/// сервер считает следующую плановую дату функцией базы, клиент только
/// отправляет дату выполнения и фактическую стоимость.
/// </summary>
public sealed partial class ScheduleViewModel : PageViewModel
{
    public ScheduleViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
        SortField = "plannedDate";
        SortOrder = "asc";

        // Период графика задан сразу, иначе пустые поля даты рисуют
        // английские подсказки вместо чисел. Берём целый год: график ТО
        // планируют на год вперёд, а не на прошедший месяц — с периодом
        // «последние 30 дней» экран оставался пустым.
        To = new DateOnly(DateTime.Today.Year, 12, 31);
        From = new DateOnly(DateTime.Today.Year, 1, 1);
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
    }

    // ------------------------------------------------------------------
    //  Календарь ТО: сетка месяца 6×7, навигация по месяцам. Просроченные
    //  записи — красные, на ближайшие три дня — жёлтые, выполненные — зелёные.
    // ------------------------------------------------------------------

    private DateOnly _month;

    public ObservableCollection<CalendarDay> CalendarDays { get; } = [];

    public IReadOnlyList<string> WeekDays { get; } = ["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"];

    public string MonthCaption
    {
        get
        {
            var text = _month.ToString("MMMM yyyy", new CultureInfo("ru-RU"));
            return char.ToUpper(text[0], CultureInfo.CurrentCulture) + text[1..];
        }
    }

    [ObservableProperty]
    private int _overdueCount;

    [ObservableProperty]
    private int _soonCount;

    public bool CanPlan => Api?.Role is "ADMIN" or "MANAGER";

    [RelayCommand]
    private async Task PreviousMonthAsync()
    {
        _month = _month.AddMonths(-1);
        await LoadCalendarAsync();
    }

    [RelayCommand]
    private async Task NextMonthAsync()
    {
        _month = _month.AddMonths(1);
        await LoadCalendarAsync();
    }

    [RelayCommand]
    private async Task CurrentMonthAsync()
    {
        _month = new DateOnly(DateTime.Today.Year, DateTime.Today.Month, 1);
        await LoadCalendarAsync();
    }

    private async Task LoadCalendarAsync()
    {
        OnPropertyChanged(nameof(MonthCaption));
        var first = _month;
        var gridStart = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
        var gridEnd = gridStart.AddDays(41);

        var result = await Api!.GetAsync<List<ScheduleRow>>(
            $"/api/maintenance-schedule?from={Fmt(gridStart)}&to={Fmt(gridEnd)}");
        var rows = result.Data ?? new List<ScheduleRow>();
        if (!result.Success)
        {
            Toasts!.Warn("Календарь: " + result.Message);
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        CalendarDays.Clear();
        for (var day = gridStart; day <= gridEnd; day = day.AddDays(1))
        {
            var current = day;
            var entries = rows
                .Where(r => r.PlannedDate == current)
                .OrderBy(r => r.InventoryNumber)
                .Select(r => new CalendarEntry(r, CalendarDay.Classify(r, today)))
                .ToList();
            CalendarDays.Add(new CalendarDay(current, current.Month == first.Month, current == today, entries));
        }

        OverdueCount = rows.Count(r => CalendarDay.Classify(r, today) == "bad");
        SoonCount = rows.Count(r => CalendarDay.Classify(r, today) == "soon");
    }

    /// <summary>Двойной щелчок по дню календаря — новая запись на эту дату.</summary>
    [RelayCommand]
    private async Task AddEntryAsync(CalendarDay? day)
    {
        if (!CanPlan)
        {
            Toasts!.Warn("Добавлять записи графика может руководитель или администратор.");
            return;
        }

        if (await Dialogs.ShowAsync(new ScheduleAddViewModel(Api!, Toasts!, Log!, day?.Date)))
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// «Создать заявку» по записи графика: сервер создаёт плановую заявку,
    /// а запись получает статус «Назначено».
    /// </summary>
    [RelayCommand]
    private async Task CreateOrderAsync()
    {
        if (Selected is null)
        {
            Toasts!.Warn("Выберите запись графика — в таблице или в календаре.");
            return;
        }

        if (Selected.Status is "Выполнено" or "Назначено")
        {
            Toasts!.Warn($"Запись в статусе «{Selected.Status}»: новая заявка не нужна.");
            return;
        }

        var row = Selected;
        var result = await Api!.PostAsync<WorkOrderRow>($"/api/schedule/{row.Id}/create-order", new { });
        if (!result.Success || result.Data is null)
        {
            ShowError(result);
            return;
        }

        Toasts!.Success($"Создана плановая заявка {result.Data.Number} на {row.InventoryNumber}.");
        await RefreshAsync();
    }

    public ObservableCollection<ScheduleRow> Rows { get; } = [];

    public IReadOnlyList<string> StatusOptions { get; } = ["Запланировано", "Назначено", "Выполнено", "Просрочено"];

    [ObservableProperty]
    private string? _statusFilter;

    [ObservableProperty]
    private DateOnly? _from;

    [ObservableProperty]
    private DateOnly? _to;

    /// <summary>
    /// Выбранная запись графика. Список и карточка выполнения —
    /// две разные вещи: мастер может отмечать выполнение, не
    /// переключаясь на другую запись.
    /// </summary>
    [ObservableProperty]
    private ScheduleRow? _selected;

    [ObservableProperty]
    private DateOnly? _completedDate;

    [ObservableProperty]
    private decimal? _actualCost;

    public override async Task RefreshAsync()
    {
        await RunLoadAsync("Загружаем график ТО…", async ct =>
        {
            var path =
                $"/api/maintenance-schedule?from={Fmt(From)}&to={Fmt(To)}" +
                $"&status={Uri.EscapeDataString(StatusFilter ?? string.Empty)}";

            var result = await Api!.GetAsync<List<ScheduleRow>>(path, ct);

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message;
                OnPropertyChanged(nameof(ErrorText));
                Toasts!.Warn(result.Message);
                return;
            }

            Rows.Clear();
            foreach (var row in result.Data)
            {
                Rows.Add(row);
            }

            Total = result.Data.Count;
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageCaption));

            await LoadCalendarAsync();
        });
    }

    [RelayCommand]
    private void Select(ScheduleRow? row)
    {
        Selected = row;
        if (row is not null)
        {
            // Дата выполнения по умолчанию — плановая: мастер чаще всего
            // отмечает ТО в срок, и лишний клик по календарю не нужен.
            CompletedDate = row.PlannedDate;
            ActualCost = row.ActualCost;
        }
    }

    /// <summary>
    /// Отметить выполнение. Сервер проверит, что дата не раньше ввода
    /// оборудования в эксплуатацию, и сам посчитает следующую дату.
    /// </summary>
    [RelayCommand]
    private async Task CompleteAsync()
    {
        if (Selected is null)
        {
            Toasts!.Warn("Сначала выберите запись графика.");
            return;
        }

        if (CompletedDate is null)
        {
            Toasts!.Warn("Укажите дату выполнения.");
            return;
        }

        await RunLoadAsync("Отмечаем выполнение…", async ct =>
        {
            var result = await Api!.PostAsync<ScheduleRow>(
                $"/api/maintenance-schedule/{Selected.Id}/complete",
                new
                {
                    completedDate = CompletedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    actualCost = ActualCost,
                },
                ct);

            if (!result.Success || result.Data is null)
            {
                Toasts!.Error(result.Message);
                return;
            }

            Toasts!.Success(
                $"ТО выполнено. Следующая дата: {result.Data.NextPlannedDate?.ToString("dd.MM.yyyy") ?? "не назначена"}");
            await RefreshAsync();
        });
    }

    /// <summary>Вернуть запись в план — снимает отметку о выполнении.</summary>
    [RelayCommand]
    private async Task ReturnToPlanAsync()
    {
        if (Selected is null)
        {
            return;
        }

        await RunLoadAsync("Возвращаем запись в план…", async ct =>
        {
            var result = await Api!.PostAsync<ScheduleRow>(
                $"/api/maintenance-schedule/{Selected.Id}/plan",
                new { },
                ct);

            if (!result.Success)
            {
                Toasts!.Error(result.Message);
                return;
            }

            Toasts!.Success("Запись возвращена в план.");
            await RefreshAsync();
        });
    }

    private static string Fmt(DateOnly? value) =>
        value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Запись графика внутри ячейки календаря.</summary>
public sealed class CalendarEntry(ScheduleRow row, string kind)
{
    public ScheduleRow Row { get; } = row;

    public string Text => $"{Row.InventoryNumber} · {Row.MaintenanceTypeName}";

    public string Tooltip => $"{Row.EquipmentName}\n{Row.MaintenanceTypeName}, {Row.PlannedDate:dd.MM.yyyy}\nСтатус: {Row.Status}";

    public IBrush Fill => kind switch
    {
        "bad" => Palette.Bad,
        "soon" => Palette.Wait,
        "done" => Palette.Ok,
        "assigned" => Palette.Accent,
        _ => Palette.SurfaceAlt,
    };
}

/// <summary>Ячейка календаря ТО.</summary>
public sealed class CalendarDay(DateOnly date, bool isCurrentMonth, bool isToday, IReadOnlyList<CalendarEntry> entries)
{
    public DateOnly Date { get; } = date;

    public string DayNumber => Date.Day.ToString(CultureInfo.InvariantCulture);

    public IReadOnlyList<CalendarEntry> Entries { get; } = entries;

    public double Opacity => isCurrentMonth ? 1.0 : 0.4;

    public IBrush BorderFill => isToday ? Palette.Accent : Palette.SurfaceAlt;

    /// <summary>Фон дня: есть просрочка — красный, ТО в ближайшие 3 дня — жёлтый.</summary>
    public IBrush Background => Entries.Any(e => e.Fill == Palette.Bad)
        ? Palette.BadSoft
        : Entries.Any(e => e.Fill == Palette.Wait) ? Palette.WaitSoft : Palette.Surface;

    /// <summary>
    /// Класс записи: «bad» — просрочена (дата прошла, ТО не выполнено),
    /// «soon» — плановая дата в ближайшие три дня, «done» — выполнено,
    /// «assigned» — по записи уже создана заявка.
    /// </summary>
    public static string Classify(ScheduleRow row, DateOnly today)
    {
        if (row.Status == "Выполнено")
        {
            return "done";
        }

        if (row.Status == "Просрочено" || row.PlannedDate < today)
        {
            return "bad";
        }

        if (row.PlannedDate <= today.AddDays(3))
        {
            return "soon";
        }

        return row.Status == "Назначено" ? "assigned" : "plan";
    }
}
