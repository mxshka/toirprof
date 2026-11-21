using System.Text.Json;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.Services;

/// <summary>
/// Хранилище на телефоне: сеанс, кэш заявок и справочников, черновики и
/// очередь операций. Каждый набор — свой JSON-файл в папке приложения,
/// поэтому без сети видно всё, что было загружено в последний раз.
/// </summary>
public sealed class LocalStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public string Directory { get; } = directory;

    public SessionState Session { get; private set; } = new();

    public List<MobileOrder> Orders { get; private set; } = [];

    public List<EquipmentOption> Equipment { get; private set; } = [];

    public List<ReferenceItem> Reference { get; private set; } = [];

    public List<OrderDraft> Drafts { get; private set; } = [];

    public List<PendingOperation> Queue { get; private set; } = [];

    public MobileMe? Me { get; set; }

    public DateTime? OrdersLoadedAt { get; set; }

    public void Load()
    {
        System.IO.Directory.CreateDirectory(Directory);
        Session = Read<SessionState>("session.json") ?? new SessionState();
        Orders = Read<List<MobileOrder>>("orders.json") ?? [];
        Equipment = Read<List<EquipmentOption>>("equipment.json") ?? [];
        Reference = Read<List<ReferenceItem>>("reference.json") ?? [];
        Drafts = Read<List<OrderDraft>>("drafts.json") ?? [];
        Queue = Read<List<PendingOperation>>("queue.json") ?? [];
        Me = Read<MobileMe>("me.json");
    }

    public void SaveSession() => Write("session.json", Session);

    public void SaveOrders(List<MobileOrder> orders)
    {
        Orders = orders;
        OrdersLoadedAt = DateTime.Now;
        Write("orders.json", orders);
    }

    public void SaveEquipment(List<EquipmentOption> rows)
    {
        Equipment = rows;
        Write("equipment.json", rows);
    }

    public void SaveReference(List<ReferenceItem> rows)
    {
        Reference = rows;
        Write("reference.json", rows);
    }

    public void SaveMe(MobileMe me)
    {
        Me = me;
        Write("me.json", me);
    }

    public void SaveDrafts() => Write("drafts.json", Drafts);

    public void SaveQueue() => Write("queue.json", Queue);

    public void Enqueue(PendingOperation operation)
    {
        Queue.Add(operation);
        SaveQueue();
    }

    /// <summary>Выход: токен и личные данные удаляются, адрес сервера остаётся.</summary>
    public void ClearSession()
    {
        Session.Token = null;
        Session.BiometricEnabled = false;
        SaveSession();
        Me = null;
        Delete("me.json");
    }

    private T? Read<T>(string name)
    {
        try
        {
            var path = Path.Combine(Directory, name);
            return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) : default;
        }
        catch (Exception)
        {
            return default;
        }
    }

    private void Write<T>(string name, T value)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            File.WriteAllText(Path.Combine(Directory, name), JsonSerializer.Serialize(value, Json));
        }
        catch (Exception)
        {
            // Нет места или прав — данные останутся в памяти до конца сеанса.
        }
    }

    private void Delete(string name)
    {
        try
        {
            File.Delete(Path.Combine(Directory, name));
        }
        catch (Exception)
        {
        }
    }
}
