using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Дашборд мастера: шесть показателей за период и четыре среза.
///
/// Первый экран, который мастер видит после входа, поэтому он должен
/// собираться одним запросом и не требовать прокрутки: всё, что ниже
/// первого экрана, на демонстрации никто не увидит.
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    public DashboardViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
        // Период подставляем сразу: пустое поле выбора даты показывает
        // английские подсказки «day / month / year», и на снимке экрана
        // это выглядит как недоделанный фильтр. Те же границы, что
        // сервер берёт по умолчанию, — последние 30 дней.
        To = DateOnly.FromDateTime(DateTime.Today);
        From = To.Value.AddDays(-30);

        // Автообновление раз в 60 секунд: дашборд висит на мониторе
        // мастера весь день, и новые заявки должны появляться сами.
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
    }

    private readonly DispatcherTimer _timer;

    /// <summary>Четыре ключевых показателя: парк, активные заявки, заявки на сегодня, среднее время выполнения.</summary>
    public ObservableCollection<MetricCard> Kpi { get; } = [];

    /// <summary>Десять последних активных заявок с цветом приоритета.</summary>
    public ObservableCollection<WorkOrderRow> LatestOrders { get; } = [];

    /// <summary>ТО на ближайшие 7 дней и просроченное.</summary>
    public ObservableCollection<UpcomingMaintenance> Upcoming { get; } = [];

    [ObservableProperty]
    private string _updatedCaption = string.Empty;

    protected override void OnDispose() => _timer.Stop();

    private async Task LoadKpiAsync(CancellationToken ct)
    {
        var kpi = await Api!.GetAsync<KpiSummary>($"/api/analytics/dashboard-summary?from={Fmt(From)}&to={Fmt(To)}", ct);
        if (kpi.Success && kpi.Data is not null)
        {
            var d = kpi.Data;
            Kpi.Clear();
            Kpi.Add(new MetricCard("Оборудование", d.EquipmentTotal.ToString(CultureInfo.InvariantCulture),
                $"в ремонте {d.EquipmentInRepair}, в простое {d.EquipmentIdle}"));
            Kpi.Add(new MetricCard("Активные заявки", d.ActiveOrders.ToString(CultureInfo.InvariantCulture),
                d.OverdueOrders > 0 ? $"просрочено {d.OverdueOrders}" : "просрочек нет"));
            Kpi.Add(new MetricCard("Заявки на сегодня", d.OrdersToday.ToString(CultureInfo.InvariantCulture),
                "плановое начало — сегодня"));
            Kpi.Add(new MetricCard("Среднее время выполнения", $"{d.AverageCompletionHours:0.0} ч",
                Trend(d.Trend.AverageCompletionHours)));
        }

        var orders = await Api.GetAsync<PagedResult<WorkOrderRow>>(
            "/api/work-orders?sort=createdAt&order=desc&page=1&pageSize=60", ct);
        if (orders.Success && orders.Data is not null)
        {
            LatestOrders.Clear();
            foreach (var row in orders.Data.Items
                         .Where(o => o.Status is "Создана" or "Назначена" or "В работе" or "Ожидает материалов")
                         .Take(10))
            {
                LatestOrders.Add(row);
            }
        }

        var upcoming = await Api.GetAsync<List<UpcomingMaintenance>>("/api/analytics/upcoming-maintenance?days=7", ct);
        if (upcoming.Success && upcoming.Data is not null)
        {
            Upcoming.Clear();
            foreach (var row in upcoming.Data.Take(12))
            {
                Upcoming.Add(row);
            }
        }

        UpdatedCaption = $"обновлено в {DateTime.Now:HH:mm:ss}, автообновление каждые 60 с";
    }

    private static string Trend(decimal? change) => change switch
    {
        null => "нет данных за прошлый период",
        > 0 => $"▲ {change:0.#}% к прошлому периоду",
        < 0 => $"▼ {-change:0.#}% к прошлому периоду",
        _ => "без изменений",
    };

    /// <summary>Шесть карточек — по одной на показатель из задания.</summary>
    public ObservableCollection<MetricCard> Cards { get; } = [];

    public ObservableCollection<WorkshopSlice> ByWorkshop { get; } = [];

    public ObservableCollection<StatusSlice> ByStatus { get; } = [];

    public ObservableCollection<MonthSlice> ByMonth { get; } = [];

    [ObservableProperty]
    private DateOnly? _from;

    [ObservableProperty]
    private DateOnly? _to;

    [ObservableProperty]
    private string _periodCaption = "за последние 30 дней";

    public override async Task RefreshAsync()
    {
        await RunLoadAsync("Считаем показатели цеха…", async ct =>
        {
            var path = $"/api/dashboard/summary?from={Fmt(From)}&to={Fmt(To)}";
            var result = await Api!.GetAsync<DashboardSummary>(path, ct);

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message;
                OnPropertyChanged(nameof(ErrorText));
                Toasts!.Warn(result.Message);
                return;
            }

            Fill(result.Data);
            await LoadKpiAsync(ct);
        });
    }

    private void Fill(DashboardSummary data)
    {
        Cards.Clear();
        Cards.Add(new MetricCard("Заявок за период", data.TotalOrders.ToString(CultureInfo.InvariantCulture), "все"));
        Cards.Add(new MetricCard("В работе", data.InProgress.ToString(CultureInfo.InvariantCulture), "требуют внимания"));
        Cards.Add(new MetricCard("Завершено", data.Completed.ToString(CultureInfo.InvariantCulture), "подтверждено"));
        Cards.Add(new MetricCard("Средний простой", $"{data.AverageDowntimeHours:0.0} ч", "на заявку"));
        Cards.Add(new MetricCard("Доля аварийных", $"{data.EmergencyShare:0.0}%", "от всех заявок"));
        Cards.Add(new MetricCard("План ТО выполнен", $"{data.MaintenancePlanShare:0.0}%", "по графику"));

        ByWorkshop.Clear();
        foreach (var row in data.ByWorkshop)
        {
            ByWorkshop.Add(row);
        }

        ByStatus.Clear();
        foreach (var row in data.ByStatus)
        {
            ByStatus.Add(row);
        }

        ByMonth.Clear();
        foreach (var row in data.ByMonth)
        {
            ByMonth.Add(row);
        }

        PeriodCaption = $"{data.PeriodFrom:dd.MM.yyyy} — {data.PeriodTo:dd.MM.yyyy}";
        Log?.Info($"Дашборд: заявок {data.TotalOrders}, аварийных {data.EmergencyShare:0.0}%, простой {data.AverageDowntimeHours:0.0} ч");
    }

    /// <summary>
    /// Дата в формате API. Пустая дата — пустая строка, а не «01.01.0001»:
    /// сервер подставит значение по умолчанию.
    /// </summary>
    private static string Fmt(DateOnly? value) =>
        value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Карточка показателя: подпись, значение и пояснение под ним.</summary>
public sealed record MetricCard(string Title, string Value, string Hint);
