using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Оборудование цеха: реестр с поиском, фильтрами и пагинацией.
///
/// Статус выведенной из эксплуатации единицы менять нельзя — это
/// требование задания и правило триггера. Клиент не проверяет его сам:
/// кнопку он просто прячет, а настоящую проверку делает сервер.
/// </summary>
public sealed partial class EquipmentViewModel : PageViewModel
{
    public EquipmentViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
        SortField = "inventoryNumber";
        SortOrder = "asc";
    }

    public ObservableCollection<EquipmentShort> Rows { get; } = [];

    /// <summary>Список статусов для выпадающего фильтра.</summary>
    public IReadOnlyList<string> StatusOptions { get; } =
        ["В работе", "В ремонте", "В простое", "Выведено из эксплуатации"];

    [ObservableProperty]
    private string? _statusFilter;

    [ObservableProperty]
    private bool _includeRetired;

    [ObservableProperty]
    private int _criticalityFilter;

    [ObservableProperty]
    private EquipmentCard? _selected;

    /// <summary>Строка, выбранная в таблице.</summary>
    [ObservableProperty]
    private EquipmentShort? _selectedRow;

    [ObservableProperty]
    private string? _modelFilter;

    public IReadOnlyList<string> SortOptions { get; } = ["inventoryNumber", "name", "status", "criticality"];

    public bool CanCreate => Api?.Role == "ADMIN";

    public bool CanEdit => Api?.Role is "ADMIN" or "MANAGER";

    public bool CanDelete => Api?.Role == "ADMIN";

    public bool CanEditSelected => Selected is not null && !Selected.IsDecommissioned;

    public override async Task RefreshAsync()
    {
        await RunLoadAsync("Загружаем перечень оборудования…", async ct =>
        {
            var path =
                $"/api/equipment?search={Uri.EscapeDataString(SearchTextOrEmpty)}" +
                $"&status={Uri.EscapeDataString(StatusFilter ?? string.Empty)}" +
                $"&criticality={CriticalityFilter}" +
                $"&includeRetired={(IncludeRetired ? "true" : "false")}" +
                $"&model={Uri.EscapeDataString(ModelFilter ?? string.Empty)}" +
                $"&sort={Uri.EscapeDataString(SortField)}&order={SortOrder}" +
                $"&page={Page}&pageSize={PageSize}";

            var result = await Api!.GetAsync<PagedResult<EquipmentShort>>(path, ct);

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message;
                OnPropertyChanged(nameof(ErrorText));
                Toasts!.Warn(result.Message);
                return;
            }

            Rows.Clear();
            foreach (var row in result.Data.Items)
            {
                Rows.Add(row);
            }

            Total = result.Data.Total;
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(CanGoPrevious));
            OnPropertyChanged(nameof(PageCaption));
        });
    }

    /// <summary>Карточка единицы — второй уровень списка, по ней видно даты и изготовителя.</summary>
    [RelayCommand]
    private async Task OpenCardAsync(Guid? id)
    {
        if (id is null)
        {
            return;
        }

        await RunLoadAsync("Открываем карточку…", async ct =>
        {
            var result = await Api!.GetAsync<EquipmentCard>($"/api/equipment/{id.Value}", ct);
            if (!result.Success || result.Data is null)
            {
                Toasts!.Warn(result.Message);
                return;
            }

            Selected = result.Data;
            OnPropertyChanged(nameof(Selected));
            OnPropertyChanged(nameof(CanEditSelected));
        });
    }

    [RelayCommand]
    private void CloseCard()
    {
        Selected = null;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(CanEditSelected));
    }

    /// <summary>
    /// Смена статуса единицы. Если единица выведена из эксплуатации,
    /// клиент отказывает до запроса и объясняет правило — эксперту
    /// видно, что ограничение понято, а не просто спрятано.
    /// </summary>
    [RelayCommand]
    private async Task ChangeStatusAsync(string? status)
    {
        if (Selected is null || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        if (Selected.IsDecommissioned)
        {
            Toasts!.Warn($"«{Selected.Name}» выведено из эксплуатации: статус изменить нельзя.");
            return;
        }

        await RunLoadAsync("Меняем статус…", async ct =>
        {
            var result = await Api!.PatchAsync<EquipmentCard>(
                $"/api/equipment/{Selected.Id}/status", new { status }, ct);

            if (!result.Success || result.Data is null)
            {
                Toasts!.Error(result.Message);
                return;
            }

            Selected = result.Data;
            OnPropertyChanged(nameof(Selected));
            Toasts!.Success($"Статус: {result.Data.Status}");
            await RefreshAsync();
        });
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        Page = 1;
        await RefreshAsync();
    }

    private async Task<EquipmentCard?> LoadCardAsync(Guid id)
    {
        var result = await Api!.GetAsync<EquipmentCard>($"/api/equipment/{id}");
        if (!result.Success || result.Data is null)
        {
            ShowError(result);
            return null;
        }

        return result.Data;
    }

    /// <summary>Карточка во всплывающем окне — по двойному щелчку или кнопке.</summary>
    [RelayCommand]
    public async Task ShowCardAsync()
    {
        if (SelectedRow is null)
        {
            Toasts!.Warn("Выберите оборудование в таблице.");
            return;
        }

        var card = await LoadCardAsync(SelectedRow.Id);
        if (card is not null)
        {
            await Dialogs.ShowAsync(new EquipmentCardViewModel(card));
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        if (await Dialogs.ShowAsync(new EquipmentEditViewModel(Api!, Toasts!, Log!, null)))
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedRow is null)
        {
            Toasts!.Warn("Выберите оборудование для редактирования.");
            return;
        }

        var card = await LoadCardAsync(SelectedRow.Id);
        if (card is null)
        {
            return;
        }

        if (await Dialogs.ShowAsync(new EquipmentEditViewModel(Api!, Toasts!, Log!, card)))
        {
            await RefreshAsync();
        }
    }

    /// <summary>
    /// Безопасное удаление: подтверждение с последствиями, а сервер
    /// откажет (409), если на оборудование есть заявки или записи графика —
    /// тогда предлагается вывести его из эксплуатации.
    /// </summary>
    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedRow is null)
        {
            Toasts!.Warn("Выберите оборудование для удаления.");
            return;
        }

        var row = SelectedRow;
        var confirmed = await Dialogs.ConfirmAsync(
            "Удалить оборудование?",
            $"«{row.InventoryNumber} — {row.Name}» будет удалено из реестра. Действие нельзя отменить. " +
            "Если по оборудованию есть заявки или график ТО, сервер не даст удалить запись — " +
            "в этом случае выведите оборудование из эксплуатации.",
            "Удалить",
            danger: true);
        if (!confirmed)
        {
            return;
        }

        var result = await Api!.DeleteAsync<object>($"/api/equipment/{row.Id}");
        if (!result.Success)
        {
            ShowError(result);
            return;
        }

        Toasts!.Success($"Оборудование {row.InventoryNumber} удалено.");
        await RefreshAsync();
    }

    partial void OnSelectedRowChanged(EquipmentShort? value)
    {
        OnPropertyChanged(nameof(CanEdit));
    }
}
