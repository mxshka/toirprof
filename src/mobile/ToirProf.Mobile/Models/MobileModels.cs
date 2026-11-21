using Avalonia.Media;

namespace ToirProf.Mobile.Models;

public sealed class MobileCrew
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Specialty { get; set; }
}

public sealed class MobileMe
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<MobileCrew> Crews { get; set; } = [];

    public string CrewsCaption => Crews.Count == 0 ? "без бригады" : string.Join(", ", Crews.Select(c => c.Name));
}

public sealed class LoginResult
{
    public string Token { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
}

public sealed class MobileOrder
{
    public Guid Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string OrderType { get; set; } = string.Empty;
    public Guid EquipmentId { get; set; }
    public string EquipmentName { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string WorkshopName { get; set; } = string.Empty;
    public string? Location { get; set; }
    public string? CrewName { get; set; }
    public DateOnly? PlannedStart { get; set; }
    public DateOnly? PlannedEnd { get; set; }
    public DateTimeOffset? ActualStart { get; set; }
    public DateTimeOffset? ActualEnd { get; set; }
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public int PhotoCount { get; set; }

    /// <summary>Есть действие, которое ещё не ушло на сервер (офлайн).</summary>
    public bool HasPending { get; set; }

    public string Deadline => PlannedEnd is { } end ? $"срок {end:dd.MM}" : "без срока";

    public bool IsOverdue => PlannedEnd is { } end && end < DateOnly.FromDateTime(DateTime.Today)
                             && Status is not ("Выполнена" or "Закрыта");

    public string Place => string.IsNullOrWhiteSpace(Location) ? WorkshopName : $"{WorkshopName} · {Location}";

    public IBrush PriorityFill => Priority switch
    {
        "Критический" => Brushes.Firebrick,
        "Высокий" => new SolidColorBrush(Color.Parse("#C79A2E")),
        "Средний" => new SolidColorBrush(Color.Parse("#3D7DD6")),
        _ => new SolidColorBrush(Color.Parse("#6B7785")),
    };

    public bool CanStart => Status is "Назначена" or "Создана";

    public bool CanComplete => Status is "В работе" or "Ожидает материалов";
}

public sealed class MobileFault
{
    public string Reason { get; set; } = string.Empty;
    public int Count { get; set; }
    public DateOnly LastSeen { get; set; }
}

public sealed class MobileMaintenance
{
    public string TypeName { get; set; } = string.Empty;
    public DateOnly PlannedDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? Instruction { get; set; }
}

public sealed class MobileEquipment
{
    public Guid Id { get; set; }
    public string InventoryNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string WorkshopName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Criticality { get; set; }
    public string? Location { get; set; }
    public string? Notes { get; set; }
    public DateOnly CommissionDate { get; set; }
    public List<MobileOrder> ActiveOrders { get; set; } = [];
    public List<MobileFault> TypicalFaults { get; set; } = [];
    public List<MobileMaintenance> Maintenance { get; set; } = [];

    public string Passport =>
        $"{Manufacturer} {Model}".Trim() is { Length: > 0 } maker
            ? $"{maker}, зав. № {SerialNumber ?? "—"}, в эксплуатации с {CommissionDate:dd.MM.yyyy}"
            : $"в эксплуатации с {CommissionDate:dd.MM.yyyy}";
}

public sealed class EquipmentOption
{
    public Guid Id { get; set; }
    public string InventoryNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public override string ToString() => $"{InventoryNumber} — {Name}";
}

public sealed class ReferenceItem
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int IntervalDays { get; set; }
    public string? Instruction { get; set; }

    public string Period => $"раз в {IntervalDays} дн.";
}

public sealed class PhotoInfo
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public string Caption => (Kind == "after" ? "После ремонта" : "Дефект до ремонта") +
                             $" · {CreatedAt.ToLocalTime():dd.MM HH:mm} · {AuthorName}";
}

public sealed class NotificationItem
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public Guid? TargetId { get; set; }
}

/// <summary>Черновик новой заявки: хранится на телефоне, пока нет связи.</summary>
public sealed class OrderDraft
{
    public Guid LocalId { get; set; } = Guid.NewGuid();
    public Guid EquipmentId { get; set; }
    public string EquipmentCaption { get; set; } = string.Empty;
    public string OrderType { get; set; } = "Аварийная";
    public string Priority { get; set; } = "Высокий";
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? FailureReason { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public string Caption => $"{EquipmentCaption}: {Title}";

    public string When => $"{OrderType.ToLowerInvariant()} · сохранён {CreatedAt:dd.MM HH:mm}";
}

/// <summary>Операция, ожидающая отправки на сервер.</summary>
public sealed class PendingOperation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>create, start, complete, photo.</summary>
    public string Kind { get; set; } = string.Empty;

    public Guid? OrderId { get; set; }

    /// <summary>Для фото к заявке, которая сама ещё в очереди: локальный id черновика.</summary>
    public Guid? LocalOrderId { get; set; }

    public string Body { get; set; } = "{}";

    public string Caption { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

/// <summary>Сохранённый сеанс: адрес сервера, токен и вход по отпечатку.</summary>
public sealed class SessionState
{
    public string ServerUrl { get; set; } = string.Empty;
    public string? Token { get; set; }
    public DateTime TokenExpiresAt { get; set; }
    public string? Login { get; set; }
    public bool BiometricEnabled { get; set; }
    public DateTime NotificationsSeenAt { get; set; } = DateTime.UtcNow;
}
