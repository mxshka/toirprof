using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>Строка списка израсходованных материалов в форме завершения.</summary>
public sealed class UsedMaterial(MaterialRow material, decimal quantity)
{
    public MaterialRow Material { get; } = material;

    public decimal Quantity { get; } = quantity;

    public string Caption => $"{Material.Sku} · {Material.Name}";

    public string QuantityCaption => $"{Quantity:0.##} {Material.Unit}";

    public decimal Cost => Quantity * Material.Price;
}

/// <summary>
/// Завершение заявки: причина неисправности и список израсходованных
/// материалов. Сервер списывает материалы в той же транзакции, что и
/// завершение, — при нехватке остатка не проходит ничего.
/// </summary>
public sealed partial class CompleteOrderViewModel : DialogViewModel
{
    private readonly WorkOrderRow _order;

    public CompleteOrderViewModel(ApiClient api, ToastService toasts, FileLogger log, WorkOrderRow order)
        : base(api, toasts, log)
    {
        _order = order;
    }

    public override string Title => $"Завершение заявки {_order.Number}";

    public override double DialogWidth => 640;

    public string Subtitle => $"{_order.InventoryNumber} — {_order.EquipmentName}";

    public ObservableCollection<MaterialRow> Catalog { get; } = [];

    public ObservableCollection<UsedMaterial> Used { get; } = [];

    [ObservableProperty]
    private string _failureReason = string.Empty;

    [ObservableProperty]
    private MaterialRow? _material;

    [ObservableProperty]
    private decimal? _quantity = 1;

    [ObservableProperty]
    private string _totalCaption = "0,00 ₽";

    public override async Task RefreshAsync()
    {
        var result = await Api!.GetAsync<List<MaterialRow>>("/api/materials/select");
        if (!result.Success || result.Data is null)
        {
            SetErrors("Справочник материалов не загрузился: " + result.Message);
            return;
        }

        Catalog.Clear();
        foreach (var m in result.Data)
        {
            Catalog.Add(m);
        }
    }

    [RelayCommand]
    private void AddMaterial()
    {
        if (Material is null)
        {
            SetErrors("Выберите материал.");
            return;
        }

        var quantity = Quantity ?? 0m;
        if (quantity <= 0m)
        {
            SetErrors("Количество должно быть больше нуля.");
            return;
        }

        var already = Used.Where(u => u.Material.Id == Material.Id).Sum(u => u.Quantity);
        if (already + quantity > Material.Stock)
        {
            SetErrors($"На складе {Material.Stock:0.##} {Material.Unit} «{Material.Name}» — столько списать нельзя.");
            return;
        }

        SetErrors();
        Used.Add(new UsedMaterial(Material, quantity));
        Recalculate();
    }

    [RelayCommand]
    private void RemoveMaterial(UsedMaterial? line)
    {
        if (line is not null)
        {
            Used.Remove(line);
            Recalculate();
        }
    }

    private void Recalculate()
    {
        var total = Calculators.CalculateTotalMaterialCost(
            Used.Select(u => new MaterialLine(u.Material.Id, u.Quantity, u.Material.Price)));
        TotalCaption = $"{total:N2} ₽";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(FailureReason))
        {
            SetErrors("Укажите причину неисправности — без неё заявку завершить нельзя.");
            return;
        }

        SetErrors();
        IsBusy = true;
        try
        {
            var materials = Used
                .GroupBy(u => u.Material.Id)
                .Select(g => new { materialId = g.Key, quantity = g.Sum(u => u.Quantity) })
                .ToList();

            var result = await Api!.PostAsync<object>($"/api/work-orders/{_order.Id}/complete", new
            {
                actualEnd = DateTimeOffset.Now,
                failureReason = FailureReason.Trim(),
                materials,
            });

            if (!result.Success)
            {
                if (result.StatusCode is 400 or 409)
                {
                    SetErrors(result.Message);
                }
                else
                {
                    ShowError(result);
                }

                return;
            }

            Toasts!.Success(Used.Count == 0
                ? "Работа выполнена. Заявку можно закрыть."
                : $"Работа выполнена, списано позиций: {materials.Count} на {TotalCaption}.");
            CloseWith(true);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
