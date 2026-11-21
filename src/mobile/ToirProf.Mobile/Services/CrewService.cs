using System.Globalization;
using System.Text.Json;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.Services;

/// <summary>
/// Действия бригады с офлайн-режимом. Есть связь — операция сразу уходит на
/// сервер. Нет связи — она сохраняется в очередь на телефоне, а заявка в
/// кэше помечается «ждёт отправки». Очередь отправляется сама, когда
/// связь вернётся (SyncAsync вызывает таймер оболочки).
/// </summary>
public sealed class CrewService(MobileApi api, LocalStore store, IPlatformServices platform)
{
    public MobileApi Api { get; } = api;

    public LocalStore Store { get; } = store;

    public IPlatformServices Platform { get; } = platform;

    public bool IsOnline => Api.IsOnline;

    public int PendingCount => Store.Queue.Count;

    /// <summary>Загрузка заявок, справочников и профиля. false — нет связи, показан кэш.</summary>
    public async Task<bool> RefreshAsync()
    {
        var orders = await Api.GetAsync<List<MobileOrder>>("/api/mobile/orders");
        if (!orders.Success || orders.Data is null)
        {
            return false;
        }

        // Локальные изменения, ещё не отправленные на сервер, не затираются.
        var pending = Store.Queue.Where(q => q.OrderId is not null).Select(q => q.OrderId!.Value).ToHashSet();
        foreach (var order in orders.Data.Where(o => pending.Contains(o.Id)))
        {
            var local = Store.Orders.FirstOrDefault(o => o.Id == order.Id);
            if (local is not null)
            {
                order.Status = local.Status;
            }

            order.HasPending = true;
        }

        Store.SaveOrders(orders.Data);

        var me = await Api.GetAsync<MobileMe>("/api/mobile/me");
        if (me.Success && me.Data is not null)
        {
            Store.SaveMe(me.Data);
        }

        var equipment = await Api.GetAsync<List<EquipmentOption>>("/api/equipment/select");
        if (equipment.Success && equipment.Data is not null)
        {
            Store.SaveEquipment(equipment.Data.Where(e => e.Status != "Выведено из эксплуатации").ToList());
        }

        var reference = await Api.GetAsync<List<ReferenceItem>>("/api/mobile/reference");
        if (reference.Success && reference.Data is not null)
        {
            Store.SaveReference(reference.Data);
        }

        return true;
    }

    public Task<string> StartAsync(MobileOrder order) => RunAsync(
        order,
        new PendingOperation
        {
            Kind = "start",
            OrderId = order.Id,
            Body = JsonSerializer.Serialize(new { actualStart = DateTimeOffset.Now }, MobileApi.Json),
            Caption = $"{order.Number}: начало работ",
        },
        "В работе",
        "Работы начаты.");

    public Task<string> CompleteAsync(MobileOrder order, string reason, string? report) => RunAsync(
        order,
        new PendingOperation
        {
            Kind = "complete",
            OrderId = order.Id,
            Body = JsonSerializer.Serialize(
                new { actualEnd = DateTimeOffset.Now, failureReason = reason.Trim(), comment = report?.Trim() },
                MobileApi.Json),
            Caption = $"{order.Number}: завершение",
        },
        "Выполнена",
        "Работы завершены, отчёт отправлен мастеру.");

    /// <summary>Фото к заявке. orderId — заявка на сервере, localId — заявка из очереди.</summary>
    public async Task<string> AttachPhotoAsync(Guid? orderId, Guid? localId, string kind, PhotoData photo, string? comment)
    {
        var point = await Platform.GetLocationAsync();
        var operation = new PendingOperation
        {
            Kind = "photo",
            OrderId = orderId,
            LocalOrderId = localId,
            Body = JsonSerializer.Serialize(new
            {
                kind,
                contentBase64 = Convert.ToBase64String(photo.Content),
                contentType = photo.ContentType,
                comment,
                latitude = point is null ? (decimal?)null : Math.Round((decimal)point.Latitude, 6),
                longitude = point is null ? (decimal?)null : Math.Round((decimal)point.Longitude, 6),
            }, MobileApi.Json),
            Caption = kind == "after" ? "Фото результата" : "Фото дефекта",
        };

        if (orderId is null)
        {
            Store.Enqueue(operation);
            return "Фото сохранено и уйдёт вместе с заявкой.";
        }

        var result = await Api.PostRawAsync($"/api/work-orders/{orderId}/photos", operation.Body);
        if (result.IsOffline)
        {
            Store.Enqueue(operation);
            MarkPending(orderId.Value, null);
            return "Нет связи: фото сохранено и отправится автоматически.";
        }

        if (!result.Success)
        {
            return result.Message;
        }

        var cached = Store.Orders.FirstOrDefault(o => o.Id == orderId);
        if (cached is not null)
        {
            cached.PhotoCount++;
            Store.SaveOrders(Store.Orders);
        }

        return "Фото прикреплено к заявке.";
    }

    /// <summary>Отправка черновика заявки с координатами; без связи — в очередь.</summary>
    public async Task<string> SubmitDraftAsync(OrderDraft draft)
    {
        var body = JsonSerializer.Serialize(new
        {
            equipmentId = draft.EquipmentId,
            title = draft.Title.Trim(),
            description = draft.Description.Trim(),
            priority = draft.Priority,
            orderType = draft.OrderType,
            failureReason = string.IsNullOrWhiteSpace(draft.FailureReason) ? null : draft.FailureReason.Trim(),
            latitude = draft.Latitude is { } lat ? Math.Round((decimal)lat, 6) : (decimal?)null,
            longitude = draft.Longitude is { } lon ? Math.Round((decimal)lon, 6) : (decimal?)null,
        }, MobileApi.Json);

        Store.Drafts.RemoveAll(d => d.LocalId == draft.LocalId);
        Store.SaveDrafts();

        var result = await Api.PostRawAsync("/api/work-orders", body);
        if (result.IsOffline)
        {
            Store.Enqueue(new PendingOperation
            {
                Kind = "create",
                LocalOrderId = draft.LocalId,
                Body = body,
                Caption = $"Новая заявка: {draft.Title}",
            });
            return "Нет связи: заявка в очереди и уйдёт автоматически.";
        }

        if (!result.Success)
        {
            // Сервер отклонил — возвращаем черновик, чтобы его можно было исправить.
            Store.Drafts.Add(draft);
            Store.SaveDrafts();
            return result.Message;
        }

        var number = result.Data.ValueKind == JsonValueKind.Object && result.Data.TryGetProperty("number", out var n)
            ? n.GetString()
            : null;
        if (result.Data.ValueKind == JsonValueKind.Object && result.Data.TryGetProperty("id", out var idProp)
            && idProp.TryGetGuid(out var serverId))
        {
            RebindLocalId(draft.LocalId, serverId);
        }

        await FlushQueueAsync();
        return $"Заявка {number} создана.";
    }

    /// <summary>Отправка очереди. Возвращает число отправленных операций.</summary>
    public async Task<int> FlushQueueAsync()
    {
        var sent = 0;
        foreach (var operation in Store.Queue.ToList())
        {
            if (operation.Kind != "create" && operation.OrderId is null)
            {
                // Фото к заявке, которая ещё не создана на сервере, ждёт её.
                continue;
            }

            var path = operation.Kind switch
            {
                "create" => "/api/work-orders",
                "start" => $"/api/work-orders/{operation.OrderId}/start",
                "complete" => $"/api/work-orders/{operation.OrderId}/complete",
                _ => $"/api/work-orders/{operation.OrderId}/photos",
            };

            var result = await Api.PostRawAsync(path, operation.Body);
            if (result.IsOffline)
            {
                break;
            }

            Store.Queue.Remove(operation);
            if (result.Success)
            {
                sent++;
                if (operation.Kind == "create" && operation.LocalOrderId is { } localId
                    && result.Data.ValueKind == JsonValueKind.Object
                    && result.Data.TryGetProperty("id", out var idProp) && idProp.TryGetGuid(out var serverId))
                {
                    RebindLocalId(localId, serverId);
                }
            }
            else
            {
                Platform.ShowNotification("Операция не принята сервером", $"{operation.Caption}: {result.Message}");
            }

            Store.SaveQueue();
        }

        if (sent > 0)
        {
            foreach (var order in Store.Orders)
            {
                order.HasPending = Store.Queue.Any(q => q.OrderId == order.Id);
            }

            Store.SaveOrders(Store.Orders);
        }

        return sent;
    }

    /// <summary>
    /// «Push» без облачного сервиса: опрос ленты событий сервера и системное
    /// уведомление о новых и изменённых заявках. На площадке нет интернета,
    /// поэтому FCM недоступен — уведомления идут от собственного API.
    /// </summary>
    public async Task<int> PollNotificationsAsync()
    {
        var since = Store.Session.NotificationsSeenAt.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
        var result = await Api.GetAsync<List<NotificationItem>>($"/api/notifications?since={Uri.EscapeDataString(since)}&limit=20");
        if (!result.Success || result.Data is null || result.Data.Count == 0)
        {
            return 0;
        }

        var fresh = result.Data
            .Where(n => n.CreatedAt.ToUniversalTime() > Store.Session.NotificationsSeenAt.ToUniversalTime())
            .OrderBy(n => n.CreatedAt)
            .ToList();
        foreach (var item in fresh.TakeLast(3))
        {
            Platform.ShowNotification(item.Title, item.Text);
        }

        if (fresh.Count > 0)
        {
            Store.Session.NotificationsSeenAt = fresh.Max(n => n.CreatedAt).ToUniversalTime();
            Store.SaveSession();
        }

        return fresh.Count;
    }

    private async Task<string> RunAsync(MobileOrder order, PendingOperation operation, string newStatus, string done)
    {
        var path = operation.Kind == "start"
            ? $"/api/work-orders/{order.Id}/start"
            : $"/api/work-orders/{order.Id}/complete";
        var result = await Api.PostRawAsync(path, operation.Body);

        if (result.IsOffline)
        {
            Store.Enqueue(operation);
            MarkPending(order.Id, newStatus);
            return "Нет связи: действие сохранено и отправится автоматически.";
        }

        if (!result.Success)
        {
            return result.Message;
        }

        order.Status = newStatus;
        Store.SaveOrders(Store.Orders);
        return done;
    }

    private void MarkPending(Guid orderId, string? status)
    {
        var cached = Store.Orders.FirstOrDefault(o => o.Id == orderId);
        if (cached is null)
        {
            return;
        }

        if (status is not null)
        {
            cached.Status = status;
        }

        cached.HasPending = true;
        Store.SaveOrders(Store.Orders);
    }

    private void RebindLocalId(Guid localId, Guid serverId)
    {
        foreach (var queued in Store.Queue.Where(q => q.LocalOrderId == localId && q.Kind != "create"))
        {
            queued.OrderId = serverId;
        }

        Store.SaveQueue();
    }
}
