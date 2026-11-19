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

// ===========================================================================
//  Справочник цехов
// ===========================================================================

/// <summary>Цеха предприятия: список для фильтров и ведение справочника администратором.</summary>
[ApiController]
[Route("api/workshops")]
[Produces("application/json")]
[Authorize]
public sealed class WorkshopsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<WorkshopDto>>>> List(CancellationToken ct)
    {
        IReadOnlyList<WorkshopDto> rows = await db.Workshops.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new WorkshopDto(w.Id, w.Code, w.Name, w.Equipment.Count))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<WorkshopDto>>.Ok(rows));
    }

    [HttpPost]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse<WorkshopDto>>> Create([FromBody] SaveWorkshopDto dto, CancellationToken ct)
    {
        var workshop = new Workshop { Id = Guid.NewGuid(), Code = dto.Code.Trim(), Name = dto.Name.Trim() };
        db.Workshops.Add(workshop);
        await db.SaveChangesAsync(ct);
        return Created("/api/workshops",
            ApiResponse<WorkshopDto>.Ok(new WorkshopDto(workshop.Id, workshop.Code, workshop.Name, 0), "Цех добавлен."));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse<WorkshopDto>>> Update(Guid id, [FromBody] SaveWorkshopDto dto, CancellationToken ct)
    {
        var workshop = await db.Workshops.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw ApiException.NotFound("Цех не найден.");
        workshop.Code = dto.Code.Trim();
        workshop.Name = dto.Name.Trim();
        await db.SaveChangesAsync(ct);
        var count = await db.Equipment.CountAsync(e => e.WorkshopId == id, ct);
        return Ok(ApiResponse<WorkshopDto>.Ok(new WorkshopDto(id, workshop.Code, workshop.Name, count), "Цех сохранён."));
    }

    [HttpDelete("{id:guid}")]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse>> Delete(Guid id, CancellationToken ct)
    {
        var workshop = await db.Workshops.FirstOrDefaultAsync(w => w.Id == id, ct)
            ?? throw ApiException.NotFound("Цех не найден.");
        if (await db.Equipment.AnyAsync(e => e.WorkshopId == id, ct))
            throw ApiException.Conflict("В цехе есть оборудование — удалить цех нельзя.");
        db.Workshops.Remove(workshop);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse.Ok("Цех удалён."));
    }
}

// ===========================================================================
//  Пользователи и роли
// ===========================================================================

/// <summary>
/// Управление пользователями: список, создание, смена роли и цеха,
/// блокировка. Доступно только администратору.
/// </summary>
[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public sealed class UsersController(AppDbContext db, ITokenService tokens) : ControllerBase
{
    [HttpGet]
    [RequireRole(RoleCodes.Admin, RoleCodes.Manager)]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UserDto>>>> List(
        [FromQuery] string? search, [FromQuery] string? role, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.FullName, pattern)
                                     || EF.Functions.ILike(u.Username, pattern)
                                     || EF.Functions.ILike(u.Email, pattern));
        }
        if (!string.IsNullOrWhiteSpace(role))
            query = query.Where(u => u.Role.Code == role);

        var rows = await query
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id, u.Username, u.FullName, Role = u.Role.Code, u.IsActive, u.CreatedAt,
                u.Email, u.Phone, u.WorkshopId, Workshop = u.Workshop != null ? u.Workshop.Name : null,
                Crews = u.CrewMemberships.Select(m => m.Crew.Name).ToList(),
            })
            .ToListAsync(ct);

        IReadOnlyList<UserDto> result = rows
            .Select(u => new UserDto(u.Id, u.Username, u.FullName, u.Role, u.IsActive, u.Crews, u.CreatedAt,
                u.Email, u.Phone, u.WorkshopId, u.Workshop))
            .ToList();
        return Ok(ApiResponse<IReadOnlyList<UserDto>>.Ok(result));
    }

    [HttpGet("roles")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<RoleDto>>>> Roles(CancellationToken ct)
    {
        IReadOnlyList<RoleDto> roles = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleDto(r.Id, r.Code, r.Name))
            .ToListAsync(ct);
        return Ok(ApiResponse<IReadOnlyList<RoleDto>>.Ok(roles));
    }

    [HttpPost]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Create([FromBody] SaveUserDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Username))
            throw ApiException.BadRequest("Укажите логин.");
        if (string.IsNullOrWhiteSpace(dto.Password))
            throw ApiException.BadRequest("Укажите пароль.");
        if (string.IsNullOrWhiteSpace(dto.Email))
            throw ApiException.BadRequest("Укажите email.");
        if (string.IsNullOrWhiteSpace(dto.FullName))
            throw ApiException.BadRequest("Укажите ФИО.");

        var role = await ResolveRoleAsync(dto.Role ?? RoleCodes.Master, ct);
        var user = new User
        {
            Id = Guid.NewGuid(),
            Username = dto.Username.Trim(),
            PasswordHash = tokens.HashPassword(dto.Password),
            FullName = dto.FullName.Trim(),
            Email = dto.Email.Trim().ToLowerInvariant(),
            Phone = string.IsNullOrWhiteSpace(dto.Phone) ? null : dto.Phone.Trim(),
            RoleId = role.Id,
            WorkshopId = dto.WorkshopId,
            IsActive = dto.IsActive ?? true,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return Created($"/api/users/{user.Id}", ApiResponse<UserDto>.Ok(await LoadAsync(user.Id, ct), "Пользователь создан."));
    }

    [HttpPut("{id:guid}")]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse<UserDto>>> Update(Guid id, [FromBody] SaveUserDto dto, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ApiException.NotFound("Пользователь не найден.");

        if (dto.FullName is { } fullName) user.FullName = fullName.Trim();
        if (dto.Email is { } email) user.Email = email.Trim().ToLowerInvariant();
        if (dto.Phone is { } phone) user.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        if (dto.Role is { } roleCode) user.RoleId = (await ResolveRoleAsync(roleCode, ct)).Id;
        if (dto.WorkshopId is { } workshopId) user.WorkshopId = workshopId;
        if (dto.ClearWorkshop) user.WorkshopId = null;
        if (!string.IsNullOrWhiteSpace(dto.Password)) user.PasswordHash = tokens.HashPassword(dto.Password);
        if (dto.IsActive is { } active)
        {
            if (!active && id == User.GetUserId())
                throw ApiException.BadRequest("Нельзя заблокировать собственную учётную запись.");
            user.IsActive = active;
        }

        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<UserDto>.Ok(await LoadAsync(id, ct), "Пользователь сохранён."));
    }

    /// <summary>Заблокировать или разблокировать учётную запись.</summary>
    [HttpPatch("{id:guid}/active")]
    [RequireRole(RoleCodes.Admin)]
    public async Task<ActionResult<ApiResponse<UserDto>>> SetActive(Guid id, [FromBody] SaveUserDto dto, CancellationToken ct)
        => await Update(id, new SaveUserDto { IsActive = dto.IsActive ?? false }, ct);

    private async Task<Role> ResolveRoleAsync(string code, CancellationToken ct)
        => await db.Roles.FirstOrDefaultAsync(r => r.Code == code.ToUpper(), ct)
           ?? throw ApiException.BadRequest($"Неизвестная роль «{code}». Допустимо: ADMIN, MANAGER, MASTER.");

    private async Task<UserDto> LoadAsync(Guid id, CancellationToken ct)
    {
        var u = await db.Users.AsNoTracking()
            .Include(x => x.Role).Include(x => x.Workshop)
            .Include(x => x.CrewMemberships).ThenInclude(m => m.Crew)
            .FirstAsync(x => x.Id == id, ct);
        return new UserDto(u.Id, u.Username, u.FullName, u.Role.Code, u.IsActive,
            u.CrewMemberships.Select(m => m.Crew.Name).ToList(), u.CreatedAt,
            u.Email, u.Phone, u.WorkshopId, u.Workshop?.Name);
    }
}

// ===========================================================================
//  Глобальный поиск
// ===========================================================================

/// <summary>
/// Поиск из верхней панели: заявки по номеру и названию, оборудование по
/// инвентарному номеру и названию, сотрудники по ФИО.
/// </summary>
[ApiController]
[Route("api/search")]
[Produces("application/json")]
[Authorize]
public sealed class SearchController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<SearchHitDto>>>> Search(
        [FromQuery] string q, [FromQuery] int limit = 8, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
            return Ok(ApiResponse<IReadOnlyList<SearchHitDto>>.Ok([]));

        var pattern = $"%{q.Trim()}%";
        limit = Math.Clamp(limit, 1, 20);
        var workshop = User.RestrictedWorkshop();

        var orders = db.WorkOrders.AsNoTracking()
            .Where(w => EF.Functions.ILike(w.Number, pattern) || EF.Functions.ILike(w.Title, pattern));
        if (workshop is { } ws1)
            orders = orders.Where(w => w.Equipment.WorkshopId == ws1);
        var orderRows = await orders.OrderByDescending(w => w.CreatedAt).Take(limit)
            .Select(w => new { w.Id, w.Number, w.Title, w.Status, Inv = w.Equipment.InventoryNumber })
            .ToListAsync(ct);

        var equipment = db.Equipment.AsNoTracking()
            .Where(e => EF.Functions.ILike(e.InventoryNumber, pattern) || EF.Functions.ILike(e.Name, pattern));
        if (workshop is { } ws2)
            equipment = equipment.Where(e => e.WorkshopId == ws2);
        var equipmentRows = await equipment.OrderBy(e => e.InventoryNumber).Take(limit)
            .Select(e => new SearchHitDto("equipment", e.Id, e.InventoryNumber + " · " + e.Name, e.Workshop.Name))
            .ToListAsync(ct);

        var users = await db.Users.AsNoTracking()
            .Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Username, pattern))
            .OrderBy(u => u.FullName).Take(limit)
            .Select(u => new SearchHitDto("user", u.Id, u.FullName, u.Role.Name + (u.Workshop != null ? " · " + u.Workshop.Name : "")))
            .ToListAsync(ct);

        var hits = orderRows
            .Select(w => new SearchHitDto("order", w.Id, $"{w.Number} · {w.Title}",
                $"{w.Inv} · {WorkOrderService.StatusText(w.Status)}"))
            .Concat(equipmentRows)
            .Concat(users)
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<SearchHitDto>>.Ok(hits));
    }
}

// ===========================================================================
//  Уведомления
// ===========================================================================

/// <summary>
/// Лента уведомлений: последние изменения заявок и оборудования из журнала
/// операций. Непрочитанные клиент считает сам — по дате последнего просмотра,
/// которую передаёт параметром since.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Produces("application/json")]
[Authorize]
public sealed class NotificationsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<NotificationDto>>>> List(
        [FromQuery] DateTime? since, [FromQuery] int limit = 30, CancellationToken ct = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = db.OperationLog.AsNoTracking()
            .Where(l => l.TableName == "work_orders" || l.TableName == "equipment");
        if (since is { } from)
        {
            var fromUtc = from.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(from, DateTimeKind.Utc)
                : from.ToUniversalTime();
            query = query.Where(l => l.CreatedAt > fromUtc);
        }

        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .Take(limit)
            .Select(l => new { l.Id, l.TableName, l.RecordId, l.Operation, l.OldValues, l.NewValues, l.CreatedAt,
                               Who = l.User != null ? l.User.FullName : null })
            .ToListAsync(ct);

        IReadOnlyList<NotificationDto> items = rows
            .Select(l =>
            {
                var number = ReadField(l.NewValues ?? l.OldValues, l.TableName == "work_orders" ? "number" : "inventory_number");
                var what = l.TableName == "work_orders"
                    ? WorkOrderService.DescribeAction(l.Operation, l.OldValues, l.NewValues)
                    : DescribeEquipment(l.Operation, l.OldValues, l.NewValues);
                var title = l.TableName == "work_orders" ? $"Заявка {number}" : $"Оборудование {number}";
                var text = l.Who is null ? what : $"{what} · {l.Who}";
                return new NotificationDto(l.Id, l.TableName == "work_orders" ? "order" : "equipment",
                    title, text, l.CreatedAt, l.RecordId);
            })
            .ToList();

        return Ok(ApiResponse<IReadOnlyList<NotificationDto>>.Ok(items));
    }

    private static string DescribeEquipment(string operation, string? oldValues, string? newValues)
    {
        var before = ReadField(oldValues, "status");
        var after = ReadField(newValues, "status");
        return operation switch
        {
            "INSERT" => "Добавлено в реестр",
            "UPDATE" when before != after && after is not null => $"Статус: {before} → {after}",
            "UPDATE" => "Карточка изменена",
            "DELETE" => "Удалено из реестра",
            _ => "Изменение",
        };
    }

    private static string? ReadField(string? json, string field)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty(field, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
