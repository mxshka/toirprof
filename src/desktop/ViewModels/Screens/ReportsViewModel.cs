using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Отчёты: аналитические срезы за период и выгрузка в PDF и Excel.
///
/// Временная ссылка живёт 15 секунд — это требование задания. Клиент
/// показывает обратный отсчёт, иначе ссылка выглядит битой, хотя
/// просто истекла, пока её открывали в другой программе.
/// </summary>
public sealed partial class ReportsViewModel : PageViewModel
{
    public ReportsViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
        SortField = "downtime";
        SortOrder = "desc";

        // Пустой период показывал в полях даты английские подсказки
        // «day / month / year» — подставляем тот же диапазон, что
        // сервер принимает по умолчанию.
        To = DateOnly.FromDateTime(DateTime.Today);
        From = To.Value.AddDays(-30);
    }

    public ObservableCollection<DowntimeSlice> Downtime { get; } = [];

    public ObservableCollection<CrewEfficiency> Crews { get; } = [];

    public ObservableCollection<MaterialUsage> Materials { get; } = [];

    public ObservableCollection<PlanExecutionSlice> Plan { get; } = [];

    [ObservableProperty]
    private DateOnly? _from;

    [ObservableProperty]
    private DateOnly? _to;

    [ObservableProperty]
    private OrdersSummary _orders = new();

    [ObservableProperty]
    private string _periodCaption = "за последние 30 дней";

    /// <summary>Последняя выданная временная ссылка и время её жизни.</summary>
    [ObservableProperty]
    private ReportLink? _lastLink;

    [ObservableProperty]
    private int _linkSecondsLeft;

    public IReadOnlyList<(string Kind, string Title)> ReportKinds { get; } =
    [
        ("downtime", "Простои по оборудованию"),
        ("crews", "Эффективность бригад"),
        ("materials", "Расход материалов"),
        ("plan", "Выполнение графика ТО"),
    ];

    public override async Task RefreshAsync()
    {
        await RunLoadAsync("Считаем аналитику…", async ct =>
        {
            var path = $"/api/analytics?from={Fmt(From)}&to={Fmt(To)}";
            var result = await Api!.GetAsync<AnalyticsReport>(path, ct);

            if (!result.Success || result.Data is null)
            {
                ErrorText = result.Message;
                OnPropertyChanged(nameof(ErrorText));
                Toasts!.Warn(result.Message);
                return;
            }

            Fill(result.Data);
        });
    }

    private void Fill(AnalyticsReport report)
    {
        Downtime.Clear();
        foreach (var row in report.Downtime)
        {
            Downtime.Add(row);
        }

        Crews.Clear();
        foreach (var row in report.Crews)
        {
            Crews.Add(row);
        }

        Materials.Clear();
        foreach (var row in report.Materials)
        {
            Materials.Add(row);
        }

        Plan.Clear();
        foreach (var row in report.Plan)
        {
            Plan.Add(row);
        }

        Orders = report.Orders;
        OnPropertyChanged(nameof(Orders));
        PeriodCaption = $"{report.PeriodFrom:dd.MM.yyyy} — {report.PeriodTo:dd.MM.yyyy}";
        Log?.Info($"Отчёт: заявок {report.Orders.Total}, простоев в топе {Downtime.Count}");
    }

    /// <summary>
    /// Запрос временной ссылки на PDF. Возвращает и ссылку, и время
    /// жизни — второй раз спрашивать сервер не нужно.
    /// </summary>
    [RelayCommand]
    private async Task CreateLinkAsync(string? kind)
    {
        var reportKind = string.IsNullOrWhiteSpace(kind) ? "downtime" : kind;

        await RunLoadAsync("Готовим PDF…", async ct =>
        {
            var result = await Api!.PostAsync<ReportLink>(
                "/api/reports",
                new
                {
                    kind = reportKind,
                    from = Fmt(From),
                    to = Fmt(To),
                },
                ct);

            if (!result.Success || result.Data is null)
            {
                Toasts!.Error(result.Message);
                return;
            }

            LastLink = result.Data;
            OnPropertyChanged(nameof(LastLink));
            LinkSecondsLeft = result.Data.TtlSeconds;
            Toasts!.Info($"Ссылка готова, действует {result.Data.TtlSeconds} с.");
            Log?.Info($"Выдана временная ссылка на отчёт {reportKind}, токен {result.Data.Token}");
        });
    }

    /// <summary>
    /// Выгрузка отчёта в Excel. Файл строится на клиенте из уже загруженных
    /// данных — второй запрос к серверу не нужен. Сохраняется в папку
    /// «Документы/ToirProf», путь показывается во всплывающем сообщении.
    /// </summary>
    [RelayCommand]
    private void ExportExcel(string? kind)
    {
        var reportKind = string.IsNullOrWhiteSpace(kind) ? "downtime" : kind;

        var (title, header, rows) = reportKind switch
        {
            "crews" => ("Эффективность бригад",
                new[] { "Код", "Бригада", "Назначено", "Выполнено", "В срок", "Средний простой, ч", "Материалы, руб.", "Рейтинг" },
                Crews.Select(c => (IReadOnlyList<object?>)new object?[]
                    { c.Code, c.Name, c.Assigned, c.Completed, c.InTime, c.AverageDowntimeHours, c.TotalMaterialCost, c.Rating }).ToList()),
            "materials" => ("Расход материалов",
                new[] { "Артикул", "Материал", "Ед.", "Количество", "Стоимость, руб.", "Заявок" },
                Materials.Select(m => (IReadOnlyList<object?>)new object?[]
                    { m.Sku, m.Name, m.Unit, m.Quantity, m.Cost, m.OrdersCount }).ToList()),
            "plan" => ("Выполнение графика ТО",
                new[] { "Статус", "Количество", "Доля, %" },
                Plan.Select(p => (IReadOnlyList<object?>)new object?[] { p.Status, p.Count, p.Percent }).ToList()),
            _ => ("Простои по оборудованию",
                new[] { "Инв. номер", "Оборудование", "Цех", "Заявок", "Простой, ч", "Доля, %" },
                Downtime.Select(d => (IReadOnlyList<object?>)new object?[]
                    { d.InventoryNumber, d.Name, d.WorkshopName, d.OrdersCount, d.TotalDowntimeHours, d.SharePercent }).ToList()),
        };

        if (rows.Count == 0)
        {
            Toasts!.Warn("Нет данных за период — нажмите «Показать».");
            return;
        }

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ToirProf");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder,
            $"{reportKind}-{Fmt(From)}_{Fmt(To)}-{DateTime.Now:HHmmss}.xlsx");

        try
        {
            XlsxWriter.Write(file, $"{title} {PeriodCaption}", header, rows);
            Toasts!.Info($"Excel сохранён: {file}");
            Log?.Info($"Отчёт {reportKind} выгружен в Excel: {file}");
        }
        catch (IOException ex)
        {
            Toasts!.Error($"Не удалось сохранить файл: {ex.Message}");
            Log?.Error("Ошибка выгрузки в Excel", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            Toasts!.Error($"Нет доступа к папке: {folder}");
            Log?.Error("Ошибка выгрузки в Excel", ex);
        }
    }

    public static string FormatMoney(decimal value) =>
        value.ToString("N2", CultureInfo.InvariantCulture);

    public static string FormatHours(decimal value) =>
        $"{value.ToString("0.0", CultureInfo.InvariantCulture)} ч";

    private static string Fmt(DateOnly? value) =>
        value is null ? string.Empty : value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
