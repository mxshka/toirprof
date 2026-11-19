using ToirProf.Desktop.Models;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Карточка оборудования во всплывающем окне: вкладки «Основная
/// информация», «История заявок» и «История ТО». Данные приходят одним
/// запросом GET /api/equipment/{id}.
/// </summary>
public sealed class EquipmentCardViewModel : DialogViewModel
{
    public EquipmentCardViewModel(EquipmentCard card)
    {
        Card = card;
    }

    public EquipmentCard Card { get; }

    public override string Title => $"{Card.InventoryNumber} — {Card.Name}";

    public override double DialogWidth => 860;

    public string CommissionCaption => Card.CommissionDate.ToString("dd.MM.yyyy");

    public string OperationCaption => Card.OperationDate?.ToString("dd.MM.yyyy") ?? "—";

    public string DecommissionCaption => Card.DecommissionDate?.ToString("dd.MM.yyyy") ?? "—";

    public string OrdersCaption => $"История заявок ({Card.Orders.Count})";

    public string MaintenanceCaption => $"История ТО ({Card.Maintenance.Count})";
}
