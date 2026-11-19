using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToirProf.Api.Common;
using ToirProf.Api.Data;
using ToirProf.Api.DTOs;
using ToirProf.Api.Middlewares;
using ToirProf.Api.Services;
using ToirProf.Shared;

namespace ToirProf.Api.Controllers;

/// <summary>
/// Аналитические срезы для дашбордов и диаграмм: KPI предприятия,
/// распределения, динамика, нарушения и предстоящие ТО.
///
/// Все цифры считаются по базе в момент запроса. Период по умолчанию —
/// последние 30 дней; у мастера выборка ограничена его цехом.
/// </summary>
[ApiController]
[Route("api/analytics")]
[Produces("application/json")]
[Authorize]
public sealed class InsightsController(AppDbContext db) : ControllerBase
{
    // Наборы статусов в запросах записаны явными сравнениями, а не Contains
    // по массиву: статус хранится через конвертер в русскую строку, и так
    // SQL гарантированно получается простым «status = '…' OR …».

    // ------------------------------------------------------------------ KPI

    /// <summary>Сводные KPI для главного дашборда с трендом к предыдущему периоду.</summary>
    [HttpGet("dashboard-summary")]
    public async Task<ActionResult<ApiResponse<KpiSummaryDto>>> DashboardSummary(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var length = end.DayNumber - start.DayNumber + 1;
        var prevStart = start.AddDays(-length);
        var prevEnd = start.AddDays(-1);
        var workshop = User.RestrictedWorkshop() ?? workshopId;

        var current = await KpiAsync(start, end, workshop, ct);
        var previous = await KpiAsync(prevStart, prevEnd, workshop, ct);

        var equipment = EquipmentQuery(workshop);
        var equipmentTotal = await equipment.CountAsync(e => e.Status != EquipmentStatus.ВыведеноИзЭксплуатации, ct);
        var inRepair = await equipment.CountAsync(e => e.Status == EquipmentStatus.ВРемонте, ct);
        var idle = await equipment.CountAsync(e => e.Status == EquipmentStatus.ВПростое, ct);

        var orders = OrdersQuery(workshop);
        var active = await orders.CountAsync(w => (w.Status == WorkOrderStatus.Создана || w.Status == WorkOrderStatus.Назначена || w.Status == WorkOrderStatus.ВРаботе || w.Status == WorkOrderStatus.ОжидаетМатериалов), ct);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var overdue = await orders.CountAsync(
            w => (w.Status == WorkOrderStatus.Создана || w.Status == WorkOrderStatus.Назначена || w.Status == WorkOrderStatus.ВРаботе || w.Status == WorkOrderStatus.ОжидаетМатериалов) && w.PlannedEnd != null && w.PlannedEnd < today, ct);
        var todayStart = Utc(today);
        var ordersToday = await orders.CountAsync(w => w.CreatedAt >= todayStart, ct);

        var dto = new KpiSummaryDto(
            start, end,
            equipmentTotal, inRepair, idle,
            active, overdue, ordersToday,
            current.Created, current.Completed, current.Emergency,
            current.DowntimeHours, current.AverageCompletionHours, current.PlanShare,
            new KpiTrendDto(
                Trend(current.Created, previous.Created),
                Trend(current.Completed, previous.Completed),
                Trend(current.DowntimeHours, previous.DowntimeHours),
                Trend(current.AverageCompletionHours, previous.AverageCompletionHours),
                Trend(current.PlanShare, previous.PlanShare)));

        return Ok(ApiResponse<KpiSummaryDto>.Ok(dto));
    }

    // ------------------------------------------------------- распределения

    /// <summary>Распределение оборудования по статусам (для круговой диаграммы).</summary>
    [HttpGet("equipment-status")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SliceDto>>>> EquipmentByStatus(
        [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var rows = await EquipmentQuery(User.RestrictedWorkshop() ?? workshopId)
            .GroupBy(e => e.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        IReadOnlyList<SliceDto> slices = rows
            .Select(r => new SliceDto(EquipmentStatusText(r.Key), r.Count))
            .OrderByDescending(s => s.Count)
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<SliceDto>>.Ok(slices));
    }

    /// <summary>Заявки по цехам за период: всего, выполнено, активных.</summary>
    [HttpGet("orders-by-workshop")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkshopOrdersDto>>>> OrdersByWorkshop(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop()), start, end)
            .Select(w => new { w.Equipment.WorkshopId, Name = w.Equipment.Workshop.Name, w.Status, w.DowntimeHours })
            .ToListAsync(ct);

        // Группировку делаем в памяти: строк за период немного, а EF
        // не переводит в SQL сортировку по полям record после GroupBy.
        IReadOnlyList<WorkshopOrdersDto> result = rows
            .GroupBy(r => new { r.WorkshopId, r.Name })
            .Select(g => new WorkshopOrdersDto(
                g.Key.WorkshopId, g.Key.Name, g.Count(),
                g.Count(r => r.Status is WorkOrderStatus.Выполнена or WorkOrderStatus.Закрыта),
                g.Count(r => r.Status is WorkOrderStatus.Создана or WorkOrderStatus.Назначена
                                 or WorkOrderStatus.ВРаботе or WorkOrderStatus.ОжидаетМатериалов),
                g.Sum(r => r.DowntimeHours ?? 0)))
            .OrderByDescending(r => r.Total)
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<WorkshopOrdersDto>>.Ok(result));
    }

    /// <summary>Заявки по типам (плановая / аварийная / инициативная) за период.</summary>
    [HttpGet("orders-by-type")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SliceDto>>>> OrdersByType(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        IReadOnlyList<SliceDto> rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop() ?? workshopId), start, end)
            .GroupBy(w => w.OrderType)
            .Select(g => new SliceDto(g.Key, g.Count()))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<SliceDto>>.Ok(rows));
    }

    /// <summary>Заявки по приоритетам за период.</summary>
    [HttpGet("orders-by-priority")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SliceDto>>>> OrdersByPriority(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop() ?? workshopId), start, end)
            .GroupBy(w => w.Priority)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        IReadOnlyList<SliceDto> slices = rows.Select(r => new SliceDto(WorkOrderService.StatusText(r.Key), r.Count)).ToList();
        return Ok(ApiResponse<IReadOnlyList<SliceDto>>.Ok(slices));
    }

    /// <summary>Заявки по статусам за период.</summary>
    [HttpGet("orders-by-status")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SliceDto>>>> OrdersByStatus(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop() ?? workshopId), start, end)
            .GroupBy(w => w.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        IReadOnlyList<SliceDto> slices = rows.Select(r => new SliceDto(WorkOrderService.StatusText(r.Key), r.Count)).ToList();
        return Ok(ApiResponse<IReadOnlyList<SliceDto>>.Ok(slices));
    }

    // ------------------------------------------------------------- динамика

    /// <summary>Динамика по дням: число заявок и простой (для линейного графика).</summary>
    [HttpGet("downtime-dynamics")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DynamicsPointDto>>>> DowntimeDynamics(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop() ?? workshopId), start, end)
            .Select(w => new { w.CreatedAt, w.DowntimeHours, w.Priority })
            .ToListAsync(ct);

        var byDay = rows
            .GroupBy(r => DateOnly.FromDateTime(r.CreatedAt.ToLocalTime()))
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = new List<DynamicsPointDto>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            var list = byDay.TryGetValue(day, out var found) ? found : [];
            points.Add(new DynamicsPointDto(
                day,
                list.Count,
                list.Count(r => r.Priority == WorkOrderPriority.Критический),
                Math.Round(list.Sum(r => r.DowntimeHours ?? 0), 2)));
        }

        return Ok(ApiResponse<IReadOnlyList<DynamicsPointDto>>.Ok(points));
    }

    /// <summary>Топ оборудования по простою за период.</summary>
    [HttpGet("top-downtime")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<TopDowntimeDto>>>> TopDowntime(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId,
        [FromQuery] int limit = 5, CancellationToken ct = default)
    {
        var (start, end) = Period(from, to);
        limit = Math.Clamp(limit, 1, 50);
        var rows = await InPeriod(OrdersQuery(User.RestrictedWorkshop() ?? workshopId), start, end)
            .Where(w => w.DowntimeHours != null)
            .Select(w => new { w.EquipmentId, w.Equipment.InventoryNumber, w.Equipment.Name,
                               Workshop = w.Equipment.Workshop.Name, w.DowntimeHours })
            .ToListAsync(ct);

        IReadOnlyList<TopDowntimeDto> result = rows
            .GroupBy(r => new { r.EquipmentId, r.InventoryNumber, r.Name, r.Workshop })
            .Select(g => new TopDowntimeDto(g.Key.EquipmentId, g.Key.InventoryNumber, g.Key.Name, g.Key.Workshop,
                g.Count(), g.Sum(r => r.DowntimeHours ?? 0)))
            .OrderByDescending(r => r.DowntimeHours)
            .Take(limit)
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<TopDowntimeDto>>.Ok(result));
    }

    /// <summary>Топ материалов по расходу и динамика расхода по месяцам.</summary>
    [HttpGet("materials-top")]
    public async Task<ActionResult<ApiResponse<MaterialsAnalyticsDto>>> MaterialsTop(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? workshopId,
        [FromQuery] int limit = 10, CancellationToken ct = default)
    {
        var (start, end) = Period(from, to);
        var workshop = User.RestrictedWorkshop() ?? workshopId;
        var startUtc = Utc(start);
        var endUtc = Utc(end.AddDays(1));

        var lines = db.WorkOrderMaterials.AsNoTracking()
            .Where(m => m.WorkOrder.CreatedAt >= startUtc && m.WorkOrder.CreatedAt < endUtc);
        if (workshop is { } ws)
            lines = lines.Where(m => m.WorkOrder.Equipment.WorkshopId == ws);

        var raw = await lines
            .Select(m => new { m.MaterialId, m.Material.Sku, m.Material.Name, m.Material.Unit,
                               m.Quantity, Cost = m.Quantity * m.UnitPrice, m.WorkOrder.CreatedAt })
            .ToListAsync(ct);

        var top = raw
            .GroupBy(r => new { r.MaterialId, r.Sku, r.Name, r.Unit })
            .Select(g => new MaterialTopDto(g.Key.MaterialId, g.Key.Sku, g.Key.Name, g.Key.Unit,
                g.Sum(r => r.Quantity), g.Sum(r => r.Cost)))
            .OrderByDescending(r => r.Cost)
            .Take(Math.Clamp(limit, 1, 50))
            .ToList();
        var dynamics = raw
            .GroupBy(r => new DateOnly(r.CreatedAt.Year, r.CreatedAt.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => new MaterialMonthDto(g.Key, g.Sum(r => r.Cost)))
            .ToList();

        return Ok(ApiResponse<MaterialsAnalyticsDto>.Ok(new MaterialsAnalyticsDto(top, dynamics)));
    }

    /// <summary>Плановые и фактически выполненные ТО по цехам за период.</summary>
    [HttpGet("plan-vs-fact")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<PlanFactDto>>>> PlanVsFact(
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        var (start, end) = Period(from, to);
        var schedule = db.MaintenanceSchedule.AsNoTracking()
            .Where(s => s.PlannedDate >= start && s.PlannedDate <= end);
        if (User.RestrictedWorkshop() is { } own)
            schedule = schedule.Where(s => s.Equipment.WorkshopId == own);

        var rows = await schedule
            .Select(s => new { s.Equipment.WorkshopId, Name = s.Equipment.Workshop.Name, s.Status })
            .ToListAsync(ct);

        IReadOnlyList<PlanFactDto> result = rows
            .GroupBy(r => new { r.WorkshopId, r.Name })
            .Select(g => new PlanFactDto(g.Key.WorkshopId, g.Key.Name, g.Count(),
                g.Count(r => r.Status == "Выполнено"), g.Count(r => r.Status == "Просрочено")))
            .OrderBy(r => r.WorkshopName)
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<PlanFactDto>>.Ok(result));
    }

    // ------------------------------------------------- нарушения и ТО на неделю

    /// <summary>
    /// Нарушения и просрочки: заявки с истёкшим плановым сроком, просроченные
    /// записи графика ТО и материалы ниже минимального остатка.
    /// </summary>
    [HttpGet("violations")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ViolationDto>>>> Violations(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var workshop = User.RestrictedWorkshop();
        var result = new List<ViolationDto>();

        var lateOrders = await OrdersQuery(workshop)
            .Where(w => (w.Status == WorkOrderStatus.Создана || w.Status == WorkOrderStatus.Назначена || w.Status == WorkOrderStatus.ВРаботе || w.Status == WorkOrderStatus.ОжидаетМатериалов) && w.PlannedEnd != null && w.PlannedEnd < today)
            .OrderBy(w => w.PlannedEnd)
            .Take(20)
            .Select(w => new { w.Id, w.Number, w.Title, w.PlannedEnd, w.Equipment.InventoryNumber })
            .ToListAsync(ct);
        result.AddRange(lateOrders.Select(w => new ViolationDto(
            "order", "Заявка просрочена",
            $"{w.Number} · {w.InventoryNumber} · {w.Title}",
            w.PlannedEnd!.Value, today.DayNumber - w.PlannedEnd!.Value.DayNumber, w.Id)));

        var schedule = db.MaintenanceSchedule.AsNoTracking()
            .Where(s => (s.Status == "Запланировано" || s.Status == "Просрочено") && s.PlannedDate < today);
        if (workshop is { } ws)
            schedule = schedule.Where(s => s.Equipment.WorkshopId == ws);
        var lateTo = await schedule
            .OrderBy(s => s.PlannedDate)
            .Take(20)
            .Select(s => new { s.Id, s.PlannedDate, s.Equipment.InventoryNumber, Type = s.MaintenanceType.Name })
            .ToListAsync(ct);
        result.AddRange(lateTo.Select(s => new ViolationDto(
            "maintenance", "ТО не выполнено в срок",
            $"{s.InventoryNumber} · {s.Type}",
            s.PlannedDate, today.DayNumber - s.PlannedDate.DayNumber, s.Id)));

        var shortages = await db.Materials.AsNoTracking()
            .Where(m => m.IsActive && m.Stock < m.MinStock)
            .Select(m => new { m.Id, m.Name, m.Sku, m.Stock, m.MinStock, m.Unit })
            .ToListAsync(ct);
        result.AddRange(shortages.Select(m => new ViolationDto(
            "material", "Остаток ниже минимума",
            $"{m.Sku} · {m.Name}: {m.Stock} из {m.MinStock} {m.Unit}",
            today, 0, m.Id)));

        IReadOnlyList<ViolationDto> ordered = result.OrderByDescending(v => v.DaysLate).ToList();
        return Ok(ApiResponse<IReadOnlyList<ViolationDto>>.Ok(ordered));
    }

    /// <summary>Предстоящие ТО на ближайшие дни (по умолчанию 7).</summary>
    [HttpGet("upcoming-maintenance")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UpcomingMaintenanceDto>>>> UpcomingMaintenance(
        [FromQuery] int days = 7, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var until = today.AddDays(Math.Clamp(days, 1, 90));
        var query = db.MaintenanceSchedule.AsNoTracking()
            .Where(s => s.Status != "Выполнено" && s.PlannedDate <= until);
        if (User.RestrictedWorkshop() is { } ws)
            query = query.Where(s => s.Equipment.WorkshopId == ws);

        var rows = await query
            .OrderBy(s => s.PlannedDate)
            .Take(30)
            .Select(s => new UpcomingMaintenanceDto(
                s.Id, s.EquipmentId, s.Equipment.InventoryNumber, s.Equipment.Name,
                s.MaintenanceType.Name, s.PlannedDate,
                s.PlannedDate < today ? "Просрочено" : s.Status,
                s.PlannedDate.DayNumber - today.DayNumber))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<UpcomingMaintenanceDto>>.Ok(rows));
    }

    /// <summary>Сводный отчёт по цеху за период — процедура generate_workshop_report из базы.</summary>
    [HttpGet("workshop-report")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkshopReportRowDto>>>> WorkshopReport(
        [FromQuery] Guid workshopId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        User.EnsureWorkshopAccess(workshopId);
        var (start, end) = Period(from, to);
        var rows = await db.Database
            .SqlQuery<WorkshopReportRowDto>(
                $"SELECT section AS \"Section\", indicator AS \"Indicator\", value AS \"Value\" FROM generate_workshop_report({workshopId}, {start}, {end})")
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WorkshopReportRowDto>>.Ok(rows));
    }

    // --------------------------------------------------------------- помощь

    private sealed record Kpi(int Created, int Completed, int Emergency, decimal DowntimeHours,
        decimal AverageCompletionHours, decimal PlanShare);

    private async Task<Kpi> KpiAsync(DateOnly start, DateOnly end, Guid? workshop, CancellationToken ct)
    {
        var period = InPeriod(OrdersQuery(workshop), start, end);
        var created = await period.CountAsync(ct);
        var completed = await period.CountAsync(w => (w.Status == WorkOrderStatus.Выполнена || w.Status == WorkOrderStatus.Закрыта), ct);
        var emergency = await period.CountAsync(w => w.Priority == WorkOrderPriority.Критический, ct);
        var downtime = await period.SumAsync(w => w.DowntimeHours ?? 0, ct);

        var durations = await period
            .Where(w => (w.Status == WorkOrderStatus.Выполнена || w.Status == WorkOrderStatus.Закрыта) && w.ActualStart != null && w.ActualEnd != null)
            .Select(w => new { w.ActualStart, w.ActualEnd })
            .ToListAsync(ct);
        var average = durations.Count == 0
            ? 0m
            : (decimal)durations.Average(d => (d.ActualEnd!.Value - d.ActualStart!.Value).TotalHours);

        var schedule = db.MaintenanceSchedule.AsNoTracking()
            .Where(s => s.PlannedDate >= start && s.PlannedDate <= end);
        if (workshop is { } ws)
            schedule = schedule.Where(s => s.Equipment.WorkshopId == ws);
        var planned = await schedule.CountAsync(ct);
        var done = await schedule.CountAsync(s => s.Status == "Выполнено", ct);
        var share = planned == 0 ? 0m : Math.Round(done * 100m / planned, 1);

        return new Kpi(created, completed, emergency, Math.Round(downtime, 2), Math.Round(average, 2), share);
    }

    private IQueryable<Equipment> EquipmentQuery(Guid? workshop)
    {
        var query = db.Equipment.AsNoTracking();
        return workshop is { } ws ? query.Where(e => e.WorkshopId == ws) : query;
    }

    private IQueryable<WorkOrder> OrdersQuery(Guid? workshop)
    {
        var query = db.WorkOrders.AsNoTracking();
        return workshop is { } ws ? query.Where(w => w.Equipment.WorkshopId == ws) : query;
    }

    private static IQueryable<WorkOrder> InPeriod(IQueryable<WorkOrder> query, DateOnly start, DateOnly end)
    {
        var startUtc = Utc(start);
        var endUtc = Utc(end.AddDays(1));
        return query.Where(w => w.CreatedAt >= startUtc && w.CreatedAt < endUtc);
    }

    private static (DateOnly Start, DateOnly End) Period(DateOnly? from, DateOnly? to)
    {
        var end = to ?? DateOnly.FromDateTime(DateTime.Today);
        var start = from ?? end.AddDays(-29);
        if (end < start)
            throw new ApiException(400, "Дата окончания периода раньше даты начала.", ErrorCodes.ValidationError);
        return (start, end);
    }

    private static DateTime Utc(DateOnly date)
        => DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Local).ToUniversalTime();

    /// <summary>Изменение к предыдущему периоду в процентах; null, если сравнивать не с чем.</summary>
    private static decimal? Trend(decimal current, decimal previous)
        => previous == 0 ? null : Math.Round((current - previous) * 100m / previous, 1);

    private static string EquipmentStatusText(EquipmentStatus status) => status switch
    {
        EquipmentStatus.ВРемонте => EquipmentStatuses.InRepair,
        EquipmentStatus.ВПростое => EquipmentStatuses.Idle,
        EquipmentStatus.ВыведеноИзЭксплуатации => EquipmentStatuses.Retired,
        _ => EquipmentStatuses.Running,
    };
}
