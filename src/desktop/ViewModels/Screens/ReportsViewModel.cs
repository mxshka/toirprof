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

        foreach (var t in UserSettings.Current.ReportTemplates)
        {
            Templates.Add(t);
        }
    }

    // ------------------------------------------------------------------
    //  Параметры отчёта, предпросмотр-диаграмма и шаблоны
    // ------------------------------------------------------------------

    public IReadOnlyList<ReportKindOption> KindOptions { get; } =
    [
        new("downtime", "Простои по оборудованию"),
        new("crews", "Эффективность бригад"),
        new("materials", "Расход материалов"),
        new("plan", "Выполнение графика ТО"),
    ];

    public ObservableCollection<WorkshopItem> Workshops { get; } = [];

    public ObservableCollection<ReportTemplate> Templates { get; } = [];

    public ObservableCollection<BarItem> Bars { get; } = [];

    [ObservableProperty]
    private ReportKindOption? _selectedKind;

    [ObservableProperty]
    private WorkshopItem? _selectedWorkshop;

    [ObservableProperty]
    private ReportTemplate? _selectedTemplate;

    [ObservableProperty]
    private string _templateName = string.Empty;

    [ObservableProperty]
    private string _chartTitle = "Предпросмотр";

    partial void OnSelectedKindChanged(ReportKindOption? value) => BuildChart();

    /// <summary>Предпросмотр отчёта: горизонтальная диаграмма по выбранному виду.</summary>
    private void BuildChart()
    {
        var kind = SelectedKind?.Kind ?? "downtime";
        ChartTitle = (SelectedKind ?? KindOptions[0]).Title;
        IReadOnlyList<BarItem> bars = kind switch
        {
            "crews" => BarItem.Build(
                Crews.OrderByDescending(c => c.Rating).Take(8)
                    .Select(c => (c.Name, c.Rating, $"{c.Rating:0.#} %")), 320, Palette.Ok),
            "materials" => BarItem.Build(
                Materials.OrderByDescending(m => m.Cost).Take(8)
                    .Select(m => (m.Name, m.Cost, $"{m.Cost:N0} ₽")), 320, Palette.Wait),
            "plan" => Plan
                .Select(p => new BarItem(p.Status, $"{p.Count} ({p.Percent:0.#} %)",
                    Math.Max(2, (double)p.Percent * 3.2), Palette.ForStatus(p.Status)))
                .ToList(),
            _ => BarItem.Build(
                Downtime.OrderByDescending(d => d.TotalDowntimeHours).Take(8)
                    .Select(d => ($"{d.InventoryNumber} {d.Name}", d.TotalDowntimeHours, $"{d.TotalDowntimeHours:0.#} ч")), 320, Palette.Bad),
        };

        Bars.Clear();
        foreach (var bar in bars)
        {
            Bars.Add(bar);
        }
    }

    [RelayCommand]
    private void SaveTemplate()
    {
        var name = TemplateName.Trim();
        if (name.Length == 0)
        {
            Toasts!.Warn("Введите название шаблона.");
            return;
        }

        var days = From is { } f && To is { } t ? Math.Max(1, t.DayNumber - f.DayNumber) : 30;
        var template = new ReportTemplate
        {
            Name = name,
            Kind = SelectedKind?.Kind ?? "downtime",
            PeriodDays = days,
            WorkshopId = SelectedWorkshop?.Id,
        };

        var settings = UserSettings.Current;
        settings.ReportTemplates.RemoveAll(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        settings.ReportTemplates.Add(template);
        settings.Save();

        Templates.Clear();
        foreach (var x in settings.ReportTemplates)
        {
            Templates.Add(x);
        }

        SelectedTemplate = Templates.FirstOrDefault(x => x.Name == name);
        Toasts!.Success($"Шаблон «{name}» сохранён.");
    }

    [RelayCommand]
    private async Task ApplyTemplateAsync()
    {
        if (SelectedTemplate is null)
        {
            Toasts!.Warn("Выберите шаблон.");
            return;
        }

        var t = SelectedTemplate;
        To = DateOnly.FromDateTime(DateTime.Today);
        From = To.Value.AddDays(-t.PeriodDays);
        SelectedKind = KindOptions.FirstOrDefault(k => k.Kind == t.Kind) ?? KindOptions[0];
        SelectedWorkshop = Workshops.FirstOrDefault(w => w.Id == t.WorkshopId);
        TemplateName = t.Name;
        await RefreshAsync();
        Toasts!.Info($"Применён шаблон «{t.Name}».");
    }

    [RelayCommand]
    private void DeleteTemplate()
    {
        if (SelectedTemplate is null)
        {
            return;
        }

        var name = SelectedTemplate.Name;
        UserSettings.Current.ReportTemplates.RemoveAll(x => x.Name == name);
        UserSettings.Current.Save();
        Templates.Remove(SelectedTemplate);
        SelectedTemplate = null;
        Toasts!.Info($"Шаблон «{name}» удалён.");
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
            if (Workshops.Count == 0)
            {
                var workshops = await Api!.GetAsync<List<WorkshopItem>>("/api/workshops", ct);
                if (workshops.Success && workshops.Data is not null)
                {
                    foreach (var w in workshops.Data)
                    {
                        Workshops.Add(w);
                    }
                }
            }

            var path = $"/api/analytics?from={Fmt(From)}&to={Fmt(To)}" +
                (SelectedWorkshop is null ? string.Empty : $"&workshopId={SelectedWorkshop.Id}");
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
        if (SelectedWorkshop is not null)
        {
            PeriodCaption += $" · {SelectedWorkshop.Name}";
        }

        BuildChart();
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
    /// Открыть PDF по последней временной ссылке в программе по умолчанию.
    /// Ссылка живёт 15 секунд, поэтому истёкшую не открываем, а просим новую.
    /// </summary>
    [RelayCommand]
    private void OpenPdf()
    {
        if (LastLink is null)
        {
            return;
        }

        if (LastLink.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            Toasts!.Warn("Ссылка истекла — сформируйте отчёт заново.");
            return;
        }

        var url = new Uri(Api!.BaseAddress, LastLink.Url).ToString();
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            Log?.Info($"Открыт отчёт по ссылке {url}");
        }
        catch (Exception ex)
        {
            Toasts!.Error("Не удалось открыть браузер: " + ex.Message);
            Log?.Error("Ошибка открытия ссылки на отчёт", ex);
        }
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
            XlsxWriter.Write(file, title, header, rows);
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

/// <summary>Вид отчёта для выпадающего списка.</summary>
public sealed record ReportKindOption(string Kind, string Title)
{
    public override string ToString() => Title;
}
