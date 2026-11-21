using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using ToirProf.Mobile;
using ToirProf.Mobile.Models;
using ToirProf.Mobile.Services;
using ToirProf.Mobile.ViewModels;
using ToirProf.Mobile.Views;

// ===========================================================================
//  mobile-check: клиент бригады в headless-окне 390×844 (как телефон).
//  Вывод: [MOB-OK] / [MOB-FAIL] по шагу и ошибки привязок; кадры — в PNG.
// ===========================================================================

var outDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "mobile-shots");
Directory.CreateDirectory(outDir);

var sink = new CollectingSink();
Logger.Sink = sink;

var data = Path.Combine(Path.GetTempPath(), "toirprof-mobile-check");
if (Directory.Exists(data))
{
    Directory.Delete(data, recursive: true);
}

var platform = new PreviewPlatform(data);
App.Platform = platform;

AppBuilder.Configure<App>()
    .UseSkia()
    .WithInterFont()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

var failures = 0;
var cts = new CancellationTokenSource();
Dispatcher.UIThread.Post(async () =>
{
    try
    {
        failures = await MobileChecks.RunAsync(outDir, sink, platform);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[MOB-FAIL] запуск: {ex}");
        failures++;
    }
    finally
    {
        cts.Cancel();
    }
});
Dispatcher.UIThread.MainLoop(cts.Token);

Console.WriteLine($"[MOB-SUMMARY] ошибок: {failures}");
return failures == 0 ? 0 : 1;

internal static class MobileChecks
{
    public static async Task<int> RunAsync(string outDir, CollectingSink sink, PreviewPlatform platform)
    {
        var failures = 0;
        var shell = new ShellViewModel(platform);
        var window = new Window
        {
            Width = 390,
            Height = 844,
            Content = new MainView { DataContext = shell },
        };
        window.Show();

        async Task<bool> WaitFor(Func<bool> condition, int seconds = 10)
        {
            for (var i = 0; i < seconds * 10; i++)
            {
                Dispatcher.UIThread.RunJobs();
                if (condition())
                {
                    return true;
                }

                await Task.Delay(100);
            }

            return condition();
        }

        void Check(string name, bool ok, string? details = null)
        {
            if (ok)
            {
                Console.WriteLine($"[MOB-OK] {name}" + (details is null ? string.Empty : $" — {details}"));
            }
            else
            {
                failures++;
                Console.WriteLine($"[MOB-FAIL] {name}" + (details is null ? string.Empty : $" — {details}"));
            }
        }

        async Task Shot(string name)
        {
            await Task.Delay(500);
            Dispatcher.UIThread.RunJobs();
            window.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name + ".png"));
            var errors = sink.Errors.Distinct().ToList();
            sink.Errors.Clear();
            Check($"экран {name}", errors.Count == 0, errors.Count == 0 ? null : $"ошибок привязок {errors.Count}");
            foreach (var e in errors.Take(8))
            {
                Console.WriteLine($"    {e}");
            }
        }

        // 1. Вход.
        await Shot("m01-login");
        var login = (LoginViewModel)shell.Current!;
        login.Login = "master";
        login.Password = "Master123!";
        await login.SignInCommand.ExecuteAsync(null);
        Check("вход мастера", shell.IsLoggedIn, login.Error);
        if (!shell.IsLoggedIn)
        {
            return failures;
        }

        // 2. Список заявок бригады (кэш наполняется синхронизацией).
        await WaitFor(() => shell.Store.Orders.Count > 0 && shell.Store.Me is not null);
        (shell.Current as OrdersViewModel)?.Reload();
        Check("заявки бригады загружены", shell.Store.Orders.Count > 0, $"{shell.Store.Orders.Count} шт.");
        await Shot("m02-orders");

        // 3. Карточка заявки: фото «до», старт, завершение с отчётом.
        var order = shell.Store.Orders.FirstOrDefault(o => o.CanStart) ?? shell.Store.Orders.First();
        var detail = new OrderDetailViewModel(shell, order);
        shell.Navigate(detail);
        await WaitFor(() => !detail.IsBusy, 3);
        var before = detail.Photos.Count;
        await detail.PhotoBeforeCommand.ExecuteAsync(null);
        await detail.OnShowAsync();
        Check("фото «до» отправлено", detail.Photos.Count == before + 1, $"{order.Number}: фото {detail.Photos.Count}");
        if (detail.CanStart)
        {
            await detail.StartCommand.ExecuteAsync(null);
            Check("заявка взята в работу одним нажатием", detail.Order.Status == "В работе" || detail.CanComplete, detail.Order.Status);
        }

        await Shot("m03-order-detail");
        if (detail.CanComplete)
        {
            detail.BeginCompleteCommand.Execute(null);
            detail.Reason = "Износ подшипника";
            detail.Report = "Подшипник заменён, проверен ход";
            await Shot("m04-order-complete-form");
            await detail.CompleteCommand.ExecuteAsync(null);
            Check("заявка завершена с отчётом", detail.Order.Status == "Выполнена", detail.Order.Status + (detail.FormError ?? string.Empty));
        }

        // 4. QR-код: распознанный инвентарный номер открывает паспорт.
        platform.NextScan = order.InventoryNumber;
        var scan = new EquipmentViewModel(shell, null);
        shell.Navigate(scan, clearStack: true);
        await scan.ScanCommand.ExecuteAsync(null);
        await WaitFor(() => scan.Card is not null, 5);
        Check("QR → паспорт оборудования", scan.Card?.InventoryNumber == order.InventoryNumber,
            scan.Card is null ? scan.Hint : $"{scan.Card.InventoryNumber}: заявок {scan.Card.ActiveOrders.Count}, ТО {scan.Card.Maintenance.Count}");
        await Shot("m05-equipment");

        // 5. Новая заявка с координатами и фото.
        var created = new NewOrderViewModel(shell, scan.Card?.Id);
        shell.Navigate(created, clearStack: true);
        await WaitFor(() => created.Location is not null && created.SelectedEquipment is not null, 5);
        Check("геолокация подставлена автоматически", created.Location is not null, created.LocationCaption);
        created.OrderType = "Аварийная";
        created.OrderTitle = "Посторонний шум (проверка мобильного)";
        created.Description = "Шум при пуске, вибрация на корпусе";
        created.FailureReason = "Предположительно ослаблено крепление";
        await created.TakePhotoCommand.ExecuteAsync(null);
        await Shot("m06-new-order");
        await created.SubmitCommand.ExecuteAsync(null);
        Check("заявка создана с телефона", created.FormError is null && shell.Store.Queue.Count == 0,
            created.FormError ?? $"очередь {shell.Store.Queue.Count}");

        // 6. Нет связи: заявка уходит в очередь, при появлении сети отправляется.
        var server = shell.Api.BaseUrl;
        shell.Api.SetServer("http://127.0.0.1:9");
        created.OrderTitle = "Течь масла (офлайн-черновик)";
        created.Description = "Капает из-под крышки редуктора";
        created.FailureReason = "Износ уплотнения";
        await created.SubmitCommand.ExecuteAsync(null);
        shell.UpdateConnection();
        Check("без сети заявка встала в очередь", shell.Store.Queue.Count >= 1, shell.Connection);
        await Shot("m07-offline-queue");
        shell.Api.SetServer(server);
        var sent = await shell.Service.FlushQueueAsync();
        Check("очередь отправлена при появлении сети", sent >= 1 && shell.Store.Queue.Count == 0, $"отправлено {sent}");

        // 7. Справка и профиль.
        shell.Navigate(new MoreViewModel(shell), clearStack: true);
        await Task.Delay(300);
        await Shot("m08-more");

        window.Close();
        return failures;
    }
}

/// <summary>Собирает предупреждения и ошибки Avalonia: привязки, ресурсы, разметку.</summary>
internal sealed class CollectingSink : ILogSink
{
    public List<string> Errors { get; } = [];

    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning && (area == LogArea.Binding || area == LogArea.Property || area == LogArea.Control);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        => Errors.Add($"{area}: {messageTemplate} ({source?.GetType().Name})");

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var text = messageTemplate;
        foreach (var value in propertyValues)
        {
            var start = text.IndexOf('{');
            var end = start < 0 ? -1 : text.IndexOf('}', start);
            if (start < 0 || end < 0)
            {
                break;
            }

            text = text[..start] + value + text[(end + 1)..];
        }

        if (!text.Contains("Value is null", StringComparison.Ordinal))
        {
            Errors.Add($"{area}: {text} ({source?.GetType().Name})");
        }
    }
}
