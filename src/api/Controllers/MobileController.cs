// Маршруты мобильного клиента бригады (Android):
//   GET  /api/mobile/me                    профиль и бригады текущего пользователя
//   GET  /api/mobile/orders                заявки своих бригад (активные и выполненные за 7 дней)
//   GET  /api/mobile/equipment/{code}      карточка по инвентарному номеру из QR-кода:
//                                          активные заявки, типовые неисправности, ТО
//   GET  /api/mobile/reference             справочник видов ТО с инструкциями
//   GET  /api/work-orders/{id}/photos      фото к заявке (без содержимого)
//   POST /api/work-orders/{id}/photos      прикрепить фото «до» или «после» (base64)
//   GET  /api/work-order-photos/{photoId}  содержимое фото (image/jpeg или image/png)

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ToirProf.Api.Common;
using ToirProf.Api.Data;
using ToirProf.Api.Middlewares;
using ToirProf.Api.Services;
using ToirProf.Shared;

namespace ToirProf.Api.Controllers;

public sealed record MobileCrewDto(Guid Id, string Code, string Name, string? Specialty);

public sealed record MobileMeDto(Guid Id, string FullName, string Role, IReadOnlyList<MobileCrewDto> Crews);

public sealed record MobileOrderDto(
    Guid Id,
    string Number,
    string Title,
    string Description,
    string Priority,
    string Status,
    string OrderType,
    Guid EquipmentId,
    string EquipmentName,
    string InventoryNumber,
    string WorkshopName,
    string? Location,
    string? CrewName,
    DateOnly? PlannedStart,
    DateOnly? PlannedEnd,
    DateTimeOffset? ActualStart,
    DateTimeOffset? ActualEnd,
    string? FailureReason,
    DateTime CreatedAt,
    int PhotoCount);

public sealed record MobileFaultDto(string Reason, int Count, DateOnly LastSeen);

public sealed record MobileMaintenanceDto(string TypeName, DateOnly PlannedDate, string Status, string? Instruction);

public sealed record MobileEquipmentDto(
    Guid Id,
    string InventoryNumber,
    string Name,
    string? Manufacturer,
    string? Model,
    string? SerialNumber,
    string WorkshopName,
    string Status,
    int Criticality,
    string? Location,
    string? Notes,
    DateOnly CommissionDate,
    IReadOnlyList<MobileOrderDto> ActiveOrders,
    IReadOnlyList<MobileFaultDto> TypicalFaults,
    IReadOnlyList<MobileMaintenanceDto> Maintenance);

public sealed record MobileReferenceDto(string Code, string Name, int IntervalDays, string? Instruction);

public sealed record WorkOrderPhotoDto(
    Guid Id,
    Guid WorkOrderId,
    string Kind,
    string? Comment,
    decimal? Latitude,
    decimal? Longitude,
    string AuthorName,
    DateTime CreatedAt,
    string Url);

public sealed class UploadPhotoDto
{
    /// <summary>before — дефект до ремонта, after — результат работ.</summary>
    [Required(ErrorMessage = "Укажите вид фото: before или after")]
    [RegularExpression("^(before|after)$", ErrorMessage = "Вид фото: before или after")]
    public string Kind { get; set; } = "before";

    [Required(ErrorMessage = "Нет содержимого фото")]
    public string ContentBase64 { get; set; } = string.Empty;

    [RegularExpression("^image/(jpeg|png)$", ErrorMessage = "Поддерживаются image/jpeg и image/png")]
    public string ContentType { get; set; } = "image/jpeg";

    [StringLength(500)]
    public string? Comment { get; set; }

    [Range(-90.0, 90.0)]
    public decimal? Latitude { get; set; }

    [Range(-180.0, 180.0)]
    public decimal? Longitude { get; set; }
}

/// <summary>
/// Данные для мобильного клиента бригады: заявки своих бригад, карточка
/// оборудования по QR-коду, справочник ТО и фотофиксация.
/// </summary>
[ApiController]
[Produces("application/json")]
[Authorize]
public sealed class MobileController(AppDbContext db) : ControllerBase
{
    private const int MaxPhotoBytes = 5 * 1024 * 1024;

    private static readonly WorkOrderStatus[] ActiveStatuses =
    [
        WorkOrderStatus.Создана,
        WorkOrderStatus.Назначена,
        WorkOrderStatus.ВРаботе,
        WorkOrderStatus.ОжидаетМатериалов,
    ];

    [HttpGet("api/mobile/me")]
    public async Task<ActionResult<ApiResponse<MobileMeDto>>> Me(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var user = await db.Users.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == userId, ct)
            ?? throw ApiException.NotFound("Пользователь не найден.");

        IReadOnlyList<MobileCrewDto> crews = await db.CrewMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Crew.Name)
            .Select(m => new MobileCrewDto(m.Crew.Id, m.Crew.Code, m.Crew.Name, m.Crew.Specialty))
            .ToListAsync(ct);

        return Ok(ApiResponse<MobileMeDto>.Ok(new MobileMeDto(user.Id, user.FullName, user.Role.Name, crews)));
    }

    /// <summary>
    /// Заявки бригад, в которых состоит пользователь: активные и выполненные
    /// за последние 7 дней. Сначала критические и аварийные, затем по сроку.
    /// Если пользователь не состоит в бригаде, показываются активные заявки
    /// его цеха (мастер) или всего предприятия (руководитель, администратор).
    /// </summary>
    [HttpGet("api/mobile/orders")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MobileOrderDto>>>> Orders(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var crewIds = await db.CrewMembers.AsNoTracking()
            .Where(m => m.UserId == userId)
            .Select(m => m.CrewId)
            .ToListAsync(ct);

        var recent = DateTimeOffset.UtcNow.AddDays(-7);
        var query = db.WorkOrders.AsNoTracking().Where(o =>
            o.Status == WorkOrderStatus.Создана
            || o.Status == WorkOrderStatus.Назначена
            || o.Status == WorkOrderStatus.ВРаботе
            || o.Status == WorkOrderStatus.ОжидаетМатериалов
            || (o.Status == WorkOrderStatus.Выполнена && o.CompletedAt >= recent));

        if (crewIds.Count > 0)
        {
            query = query.Where(o => o.CrewId != null && crewIds.Contains(o.CrewId.Value));
        }
        else if (User.RestrictedWorkshop() is { } workshop)
        {
            query = query.Where(o => o.Equipment.WorkshopId == workshop);
        }

        var rows = await Project(query).Take(100).ToListAsync(ct);
        IReadOnlyList<MobileOrderDto> ordered = rows
            .OrderBy(o => o.Status == "Выполнена" ? 1 : 0)
            .ThenBy(o => PriorityRank(o.Priority))
            .ThenBy(o => o.PlannedStart ?? DateOnly.MaxValue)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<MobileOrderDto>>.Ok(ordered));
    }

    /// <summary>
    /// Карточка оборудования по инвентарному номеру — его кодирует QR-код
    /// на табличке станка. Вместе с карточкой: активные заявки, типовые
    /// неисправности (причины из завершённых заявок) и ближайшее ТО с
    /// инструкцией из справочника видов ТО.
    /// </summary>
    [HttpGet("api/mobile/equipment/{code}")]
    public async Task<ActionResult<ApiResponse<MobileEquipmentDto>>> Equipment(string code, CancellationToken ct)
    {
        var key = code.Trim();
        var equipment = await db.Equipment.AsNoTracking()
            .Include(e => e.Workshop)
            .FirstOrDefaultAsync(e => e.InventoryNumber == key || e.Id.ToString() == key, ct)
            ?? throw ApiException.NotFound($"Оборудование «{key}» не найдено.");

        User.EnsureWorkshopAccess(equipment.WorkshopId);

        var active = await Project(db.WorkOrders.AsNoTracking()
                .Where(o => o.EquipmentId == equipment.Id
                            && (o.Status == WorkOrderStatus.Создана
                                || o.Status == WorkOrderStatus.Назначена
                                || o.Status == WorkOrderStatus.ВРаботе
                                || o.Status == WorkOrderStatus.ОжидаетМатериалов)))
            .ToListAsync(ct);

        var reasons = await db.WorkOrders.AsNoTracking()
            .Where(o => o.EquipmentId == equipment.Id && o.FailureReason != null)
            .Select(o => new { o.FailureReason, o.CreatedAt })
            .ToListAsync(ct);
        IReadOnlyList<MobileFaultDto> faults = reasons
            .GroupBy(r => r.FailureReason!.Trim())
            .Select(g => new MobileFaultDto(g.Key, g.Count(), DateOnly.FromDateTime(g.Max(r => r.CreatedAt))))
            .OrderByDescending(f => f.Count)
            .ThenByDescending(f => f.LastSeen)
            .Take(5)
            .ToList();

        var today = DateOnly.FromDateTime(DateTime.Today);
        var schedule = await db.MaintenanceSchedule.AsNoTracking()
            .Where(s => s.EquipmentId == equipment.Id && s.PlannedDate >= today.AddDays(-30))
            .OrderBy(s => s.PlannedDate)
            .Take(5)
            .Select(s => new { s.MaintenanceType.Name, s.PlannedDate, s.Status, s.MaintenanceType.Description })
            .ToListAsync(ct);
        IReadOnlyList<MobileMaintenanceDto> maintenance = schedule
            .Select(s => new MobileMaintenanceDto(s.Name, s.PlannedDate, s.Status, s.Description))
            .ToList();

        var dto = new MobileEquipmentDto(
            equipment.Id,
            equipment.InventoryNumber,
            equipment.Name,
            equipment.Manufacturer,
            equipment.Model,
            equipment.SerialNumber,
            equipment.Workshop.Name,
            EquipmentStatusText(equipment.Status),
            equipment.Criticality,
            equipment.Location,
            equipment.Notes,
            equipment.CommissionDate,
            active,
            faults,
            maintenance);

        return Ok(ApiResponse<MobileEquipmentDto>.Ok(dto));
    }

    /// <summary>Справочник видов ТО: периодичность и инструкция по выполнению.</summary>
    [HttpGet("api/mobile/reference")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<MobileReferenceDto>>>> Reference(CancellationToken ct)
    {
        IReadOnlyList<MobileReferenceDto> rows = await db.MaintenanceTypes.AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.IntervalDays)
            .Select(t => new MobileReferenceDto(t.Code, t.Name, t.IntervalDays, t.Description))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<MobileReferenceDto>>.Ok(rows));
    }

    [HttpGet("api/work-orders/{id:guid}/photos")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkOrderPhotoDto>>>> Photos(Guid id, CancellationToken ct)
    {
        await EnsureOrderAccessAsync(id, ct);
        IReadOnlyList<WorkOrderPhotoDto> rows = await db.WorkOrderPhotos.AsNoTracking()
            .Where(p => p.WorkOrderId == id)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new WorkOrderPhotoDto(
                p.Id, p.WorkOrderId, p.Kind, p.Comment, p.Latitude, p.Longitude,
                p.Author.FullName, p.CreatedAt, "/api/work-order-photos/" + p.Id))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WorkOrderPhotoDto>>.Ok(rows));
    }

    [HttpPost("api/work-orders/{id:guid}/photos")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<ApiResponse<WorkOrderPhotoDto>>> Upload(
        Guid id,
        [FromBody] UploadPhotoDto dto,
        CancellationToken ct)
    {
        await EnsureOrderAccessAsync(id, ct);

        byte[] content;
        try
        {
            content = Convert.FromBase64String(dto.ContentBase64);
        }
        catch (FormatException)
        {
            throw ApiException.BadRequest("Фото передано не в base64.");
        }

        if (content.Length == 0 || content.Length > MaxPhotoBytes)
        {
            throw ApiException.BadRequest("Размер фото — от 1 байта до 5 МБ.");
        }

        var photo = new WorkOrderPhoto
        {
            Id = Guid.NewGuid(),
            WorkOrderId = id,
            Kind = dto.Kind,
            ContentType = dto.ContentType,
            Content = content,
            Comment = string.IsNullOrWhiteSpace(dto.Comment) ? null : dto.Comment.Trim(),
            Latitude = dto.Latitude.HasValue && dto.Longitude.HasValue ? dto.Latitude : null,
            Longitude = dto.Latitude.HasValue && dto.Longitude.HasValue ? dto.Longitude : null,
            CreatedBy = User.GetUserId(),
        };
        db.WorkOrderPhotos.Add(photo);
        await db.SaveChangesAsync(ct);

        var author = await db.Users.AsNoTracking().Where(u => u.Id == photo.CreatedBy).Select(u => u.FullName).FirstAsync(ct);
        var result = new WorkOrderPhotoDto(
            photo.Id, id, photo.Kind, photo.Comment, photo.Latitude, photo.Longitude,
            author, DateTime.UtcNow, "/api/work-order-photos/" + photo.Id);

        return Created(result.Url, ApiResponse<WorkOrderPhotoDto>.Ok(result, "Фото прикреплено к заявке."));
    }

    [HttpGet("api/work-order-photos/{photoId:guid}")]
    [Produces("image/jpeg", "image/png")]
    public async Task<IActionResult> PhotoContent(Guid photoId, CancellationToken ct)
    {
        var photo = await db.WorkOrderPhotos.AsNoTracking()
            .Where(p => p.Id == photoId)
            .Select(p => new { p.WorkOrderId, p.ContentType, p.Content })
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Фото не найдено.");

        await EnsureOrderAccessAsync(photo.WorkOrderId, ct);
        return File(photo.Content, photo.ContentType);
    }

    private async Task EnsureOrderAccessAsync(Guid orderId, CancellationToken ct)
    {
        var workshop = await db.WorkOrders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => (Guid?)o.Equipment.WorkshopId)
            .FirstOrDefaultAsync(ct)
            ?? throw ApiException.NotFound("Заявка не найдена.");
        User.EnsureWorkshopAccess(workshop);
    }

    private IQueryable<MobileOrderDto> Project(IQueryable<WorkOrder> query) => query
        .Select(o => new MobileOrderDto(
            o.Id,
            o.Number,
            o.Title,
            o.Description ?? string.Empty,
            WorkOrderService.StatusText(o.Priority),
            WorkOrderService.StatusText(o.Status),
            o.OrderType,
            o.EquipmentId,
            o.Equipment.Name,
            o.Equipment.InventoryNumber,
            o.Equipment.Workshop.Name,
            o.Equipment.Location,
            o.Crew != null ? o.Crew.Name : null,
            o.PlannedStart,
            o.PlannedEnd,
            o.ActualStart,
            o.ActualEnd,
            o.FailureReason,
            o.CreatedAt,
            db.WorkOrderPhotos.Count(p => p.WorkOrderId == o.Id)));

    private static int PriorityRank(string priority) => priority switch
    {
        "Критический" => 0,
        "Высокий" => 1,
        "Средний" => 2,
        _ => 3,
    };

    private static string EquipmentStatusText(EquipmentStatus status) => status switch
    {
        EquipmentStatus.ВРаботе => "В работе",
        EquipmentStatus.ВРемонте => "В ремонте",
        EquipmentStatus.ВПростое => "В простое",
        _ => "Выведено из эксплуатации",
    };
}
