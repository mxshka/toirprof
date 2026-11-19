namespace ToirProf.Api.DTOs;

// ===========================================================================
//  Аналитика для дашбордов: KPI, распределения, динамика, нарушения
// ===========================================================================

/// <summary>Изменение показателя к предыдущему периоду такой же длины, %.</summary>
public sealed record KpiTrendDto(
    decimal? Created,
    decimal? Completed,
    decimal? DowntimeHours,
    decimal? AverageCompletionHours,
    decimal? MaintenancePlanShare);

/// <summary>Сводные KPI предприятия (GET /api/analytics/dashboard-summary).</summary>
public sealed record KpiSummaryDto(
    DateOnly PeriodFrom,
    DateOnly PeriodTo,
    int EquipmentTotal,
    int EquipmentInRepair,
    int EquipmentIdle,
    int ActiveOrders,
    int OverdueOrders,
    int OrdersToday,
    int CreatedOrders,
    int CompletedOrders,
    int EmergencyOrders,
    decimal DowntimeHours,
    decimal AverageCompletionHours,
    decimal MaintenancePlanShare,
    KpiTrendDto Trend);

/// <summary>Доля для круговой или столбчатой диаграммы.</summary>
public sealed record SliceDto(string Label, int Count);

public sealed record WorkshopOrdersDto(
    Guid WorkshopId,
    string WorkshopName,
    int Total,
    int Completed,
    int Active,
    decimal DowntimeHours);

public sealed record DynamicsPointDto(
    DateOnly Date,
    int Orders,
    int Emergency,
    decimal DowntimeHours);

public sealed record TopDowntimeDto(
    Guid EquipmentId,
    string InventoryNumber,
    string Name,
    string WorkshopName,
    int Orders,
    decimal DowntimeHours);

public sealed record MaterialTopDto(
    Guid MaterialId,
    string Sku,
    string Name,
    string Unit,
    decimal Quantity,
    decimal Cost);

public sealed record MaterialMonthDto(DateOnly Month, decimal Cost);

public sealed record MaterialsAnalyticsDto(
    IReadOnlyList<MaterialTopDto> Top,
    IReadOnlyList<MaterialMonthDto> Dynamics);

public sealed record PlanFactDto(
    Guid WorkshopId,
    string WorkshopName,
    int Planned,
    int Done,
    int Overdue);

/// <summary>
/// Нарушение: просроченная заявка, невыполненное ТО или дефицит материала.
/// Kind — «order», «maintenance» или «material»; TargetId — объект для перехода.
/// </summary>
public sealed record ViolationDto(
    string Kind,
    string Title,
    string Detail,
    DateOnly Date,
    int DaysLate,
    Guid TargetId);

public sealed record UpcomingMaintenanceDto(
    Guid Id,
    Guid EquipmentId,
    string InventoryNumber,
    string EquipmentName,
    string TypeName,
    DateOnly PlannedDate,
    string Status,
    int DaysLeft);

/// <summary>Строка процедуры generate_workshop_report: раздел, показатель, значение.</summary>
public sealed class WorkshopReportRowDto
{
    public string Section { get; set; } = string.Empty;

    public string Indicator { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

// ===========================================================================
//  Глобальный поиск, уведомления, справочник цехов, пользователи
// ===========================================================================

/// <summary>Результат глобального поиска. Kind — «order», «equipment» или «user».</summary>
public sealed record SearchHitDto(string Kind, Guid Id, string Title, string Subtitle);

/// <summary>Событие для ленты уведомлений клиента.</summary>
public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string Title,
    string Text,
    DateTime CreatedAt,
    Guid? TargetId);

public sealed record WorkshopDto(Guid Id, string Code, string Name, int EquipmentCount);

public sealed class SaveWorkshopDto
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Укажите код цеха")]
    [System.ComponentModel.DataAnnotations.StringLength(50, MinimumLength = 1)]
    public string Code { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Укажите название цеха")]
    [System.ComponentModel.DataAnnotations.StringLength(150, MinimumLength = 1)]
    public string Name { get; set; } = string.Empty;
}

public sealed record RoleDto(Guid Id, string Code, string Name);

public sealed class SaveUserDto
{
    [System.ComponentModel.DataAnnotations.StringLength(50, MinimumLength = 3)]
    [System.ComponentModel.DataAnnotations.RegularExpression("^[a-zA-Z0-9_.-]+$",
        ErrorMessage = "Логин: латиница, цифры и символы _ . -")]
    public string? Username { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8,
        ErrorMessage = "Пароль — минимум 8 символов")]
    public string? Password { get; set; }

    [System.ComponentModel.DataAnnotations.StringLength(200, MinimumLength = 3)]
    public string? FullName { get; set; }

    [System.ComponentModel.DataAnnotations.EmailAddress(ErrorMessage = "Неверный формат email")]
    public string? Email { get; set; }

    [System.ComponentModel.DataAnnotations.RegularExpression(@"^\+?[0-9]{10,15}$",
        ErrorMessage = "Телефон: 10–15 цифр, можно с + в начале")]
    public string? Phone { get; set; }

    /// <summary>Код роли: ADMIN, MANAGER или MASTER.</summary>
    public string? Role { get; set; }

    public Guid? WorkshopId { get; set; }

    /// <summary>true — снять цех (для руководителя и администратора).</summary>
    public bool ClearWorkshop { get; set; }

    public bool? IsActive { get; set; }
}
