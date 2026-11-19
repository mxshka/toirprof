using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ToirProf.Api.Common;
using ToirProf.Api.DTOs;
using ToirProf.Api.Middlewares;
using ToirProf.Api.Services;
using ToirProf.Shared;

namespace ToirProf.Api.Controllers;

/// <summary>График планового технического обслуживания.</summary>
[ApiController]
[Route("api/maintenance-schedule")]
[Produces("application/json")]
[Authorize]
public sealed class MaintenanceScheduleController(
    IMaintenanceScheduleService service,
    IWorkOrderService workOrders) : ControllerBase
{
    /// <summary>
    /// Записи графика за период.
    /// </summary>
    /// <remarks>
    /// Период по умолчанию — последние 30 дней. Перед чтением записи с
    /// прошедшей плановой датой и статусом «Запланировано»
    /// автоматически переводятся в «Просрочено»: иначе просроченная
    /// работа месяцами числилась бы в плане, а не в долге, и доля
    /// выполнения плана выглядела бы лучше, чем есть.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MaintenanceScheduleDto>>>> List(
        [FromQuery] Guid? equipmentId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? status,
        CancellationToken ct = default)
        => Ok(ApiResponse<IReadOnlyList<MaintenanceScheduleDto>>.Ok(
            await service.GetListAsync(equipmentId, new DateRange(from, to), status, ct)));

    /// <summary>Карточка записи графика.</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> GetById(Guid id, CancellationToken ct)
        => Ok(ApiResponse<MaintenanceScheduleDto>.Ok(await service.GetByIdAsync(id, ct)));

    /// <summary>Поставить оборудование на плановое обслуживание.</summary>
    [HttpPost]
    [RequireRole(RoleCodes.Admin, RoleCodes.Manager)]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> Create(
        [FromBody] CreateMaintenanceScheduleDto dto,
        CancellationToken ct)
        => Created("/api/maintenance-schedule",
            ApiResponse<MaintenanceScheduleDto>.Ok(await service.CreateAsync(dto, ct), "Запись графика создана."));

    /// <summary>
    /// Отметить плановое обслуживание выполненным.
    /// </summary>
    /// <remarks>
    /// Следующая плановая дата считает функция базы
    /// <c>fn_next_maintenance_date(date, integer)</c>. Правило «через
    /// месяц» живёт только в одном месте, поэтому его нельзя
    /// продублировать в коде: как только формулы разойдутся, график
    /// перестанет совпадать с отчётом.
    /// </remarks>
    [HttpPost("{id:guid}/complete")]
    [RequireRole(RoleCodes.Admin, RoleCodes.Manager, RoleCodes.Master)]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> Complete(
        Guid id,
        [FromBody] CompleteMaintenanceScheduleDto dto,
        CancellationToken ct)
        => Ok(ApiResponse<MaintenanceScheduleDto>.Ok(
            await service.CompleteAsync(id, dto, ct), "Обслуживание отмечено выполненным."));

    /// <summary>Вернуть запись в состояние «Запланировано».</summary>
    [HttpPost("{id:guid}/plan")]
    [RequireRole(RoleCodes.Admin, RoleCodes.Manager)]
    public async Task<ActionResult<ApiResponse<MaintenanceScheduleDto>>> MarkPlanned(Guid id, CancellationToken ct)
    {
        await service.MarkPlannedAsync(id, ct);
        return Ok(ApiResponse<MaintenanceScheduleDto>.Ok(
            await service.GetByIdAsync(id, ct), "Запись возвращена в план."));
    }

    /// <summary>Удалить запись графика, если она ещё не выполнена и не использована.</summary>
    /// <summary>
    /// Создать плановую заявку из записи графика ТО. В заявку переносятся
    /// оборудование и вид ТО, запись графика связывается с заявкой и
    /// получает статус «Назначено».
    /// </summary>
    [HttpPost("{id:guid}/create-order")]
    [HttpPost("/api/schedule/{id:guid}/create-order")]
    [RequireRole(RoleCodes.Admin, RoleCodes.Manager, RoleCodes.Master)]
    public async Task<ActionResult<ApiResponse<WorkOrderDto>>> CreateOrder(Guid id, CancellationToken ct)
    {
        var entry = await service.GetByIdAsync(id, ct);
        if (entry.Status == "Выполнено")
            throw ApiException.Conflict("Запись графика уже выполнена — заявка по ней не нужна.");
        if (entry.Status == "Назначено")
            throw ApiException.Conflict("По этой записи графика заявка уже создана.");

        var today = DateOnly.FromDateTime(DateTime.Now);
        var start = entry.PlannedDate < today ? today : entry.PlannedDate;

        var order = await workOrders.CreateAsync(
            new CreateWorkOrderDto
            {
                EquipmentId = entry.EquipmentId,
                MaintenanceTypeId = entry.MaintenanceTypeId,
                ScheduleId = entry.Id,
                OrderType = WorkOrderTypes.Planned,
                Title = $"{entry.MaintenanceTypeName}: {entry.InventoryNumber}",
                Description = $"Плановое ТО по графику на {entry.PlannedDate:dd.MM.yyyy}.",
                Priority = WorkOrderPriorities.Medium,
                PlannedStart = start,
                PlannedEnd = start.AddDays(1),
            },
            User.GetUserId(),
            ct,
            User.RestrictedWorkshop());

        await service.MarkAssignedAsync(id, ct);

        return Created($"/api/work-orders/{order.Id}",
            ApiResponse<WorkOrderDto>.Ok(order, $"Создана плановая заявка {order.Number}."));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        await service.DeleteAsync(id, ct);
        return Ok(ApiResponse.Ok("Запись графика удалена."));
    }
}