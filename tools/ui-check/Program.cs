using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Logging;
using Avalonia.Threading;
using ToirProf.Desktop;
using ToirProf.Desktop.Models;
using ToirProf.Desktop.Services;
using ToirProf.Desktop.ViewModels;
using ToirProf.Desktop.ViewModels.Screens;
using ToirProf.Desktop.Views;

// ===========================================================================
//  ui-check: открывает каждый экран и диалог desktop-клиента в headless-окне,
//  загружает данные из API (http://localhost:5080) и сохраняет кадр в PNG.
//  Вывод: [UI-OK] / [UI-FAIL] по экрану и список ошибок привязок.
// ===========================================================================

var outDir = args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "ui-shots");
Directory.CreateDirectory(outDir);

var sink = new CollectingSink();
Logger.Sink = sink;

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
        failures = await Checks.RunAsync(outDir, sink);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[UI-FAIL] запуск: {ex}");
        failures++;
    }
    finally
    {
        cts.Cancel();
    }
});
Dispatcher.UIThread.MainLoop(cts.Token);

Console.WriteLine($"[UI-SUMMARY] ошибок: {failures}");
return failures == 0 ? 0 : 1;

internal static class Checks
{
    public static async Task<int> RunAsync(string outDir, CollectingSink sink)
    {
        var failures = 0;
        var temp = Path.Combine(Path.GetTempPath(), "toirprof-ui-check");
        Directory.CreateDirectory(temp);
        UserSettings.FilePath = Path.Combine(temp, "settings.json");
        UserSettings.Current.LockSeconds = 0;

        var log = new FileLogger(temp);
        var api = new ApiClient(new TokenStorage(Path.Combine(temp, "session.json")), new OfflineQueue(Path.Combine(temp, "queue.json")), log);
        var toasts = new ToastService();

        var login = await api.LoginAsync("admin", "Admin123!");
        if (!login.Success)
        {
            Console.WriteLine($"[UI-FAIL] вход admin: {login.Message}");
            return 1;
        }

        async Task Shot(string name, object model, Func<Task>? load, double width = 1360, double height = 860)
        {
            sink.Errors.Clear();
            try
            {
                Window window;
                if (model is Window w)
                {
                    window = w;
                }
                else
                {
                    window = new Window
                    {
                        Width = width,
                        Height = height,
                        Content = new Border
                        {
                            Background = Palette.Make("#1B1F24"),
                            Child = new ContentControl { Content = model },
                        },
                    };
                }

                window.Show();
                if (load is not null)
                {
                    await load();
                }

                await Task.Delay(400);
                Dispatcher.UIThread.RunJobs();
                var frame = window.CaptureRenderedFrame();
                frame?.Save(Path.Combine(outDir, name + ".png"));
                window.Close();

                var errors = sink.Errors.Distinct().ToList();
                if (errors.Count == 0)
                {
                    Console.WriteLine($"[UI-OK] {name}");
                }
                else
                {
                    failures++;
                    Console.WriteLine($"[UI-FAIL] {name}: ошибок привязок {errors.Count}");
                    foreach (var e in errors.Take(8))
                    {
                        Console.WriteLine($"    {e}");
                    }
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[UI-FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Главное окно целиком: шапка, меню, дашборд.
        var shell = new MainWindowViewModel(api, toasts, log, new NavigationService());
        var main = new MainWindow { DataContext = shell };
        await Shot("00-main-dashboard", main, async () =>
        {
            await shell.OpenByNameCommand.ExecuteAsync("Dashboard");
            await Task.Delay(1500);
        });

        var screens = new (string Name, PageViewModel Model)[]
        {
            ("01-login", new LoginViewModel(api, toasts, log)),
            ("02-dashboard", new DashboardViewModel(api, toasts, log)),
            ("03-equipment", new EquipmentViewModel(api, toasts, log)),
            ("04-schedule", new ScheduleViewModel(api, toasts, log)),
            ("05-work-orders", new WorkOrdersViewModel(api, toasts, log)),
            ("06-crews", new CrewsViewModel(api, toasts, log)),
            ("07-materials", new MaterialsViewModel(api, toasts, log)),
            ("08-reports", new ReportsViewModel(api, toasts, log)),
        };

        foreach (var (name, model) in screens)
        {
            await Shot(name, model, () => model.RefreshAsync());
        }

        // Диалоги.
        var equipment = await api.GetAsync<PagedResult<EquipmentShort>>("/api/equipment?page=1&pageSize=5");
        var firstEquipment = equipment.Data?.Items.FirstOrDefault();
        EquipmentCard? card = null;
        if (firstEquipment is not null)
        {
            card = (await api.GetAsync<EquipmentCard>($"/api/equipment/{firstEquipment.Id}")).Data;
        }

        var orders = await api.GetAsync<PagedResult<WorkOrderRow>>("/api/work-orders?page=1&pageSize=5");
        var firstOrder = orders.Data?.Items.FirstOrDefault();

        var dialogs = new List<(string Name, DialogViewModel Model)>
        {
            ("10-dlg-equipment-new", new EquipmentEditViewModel(api, toasts, log, null)),
            ("12-dlg-work-order-new", new WorkOrderEditViewModel(api, toasts, log)),
            ("14-dlg-schedule-add", new ScheduleAddViewModel(api, toasts, log, null)),
            ("15-dlg-settings", new SettingsViewModel()),
            ("16-dlg-error", new MessageDialogViewModel("Нет связи с сервером", "Сервер недоступен.", "Проверьте, что API запущен.", "Понятно", null, true, () => Task.CompletedTask)),
        };
        if (card is not null)
        {
            dialogs.Add(("11-dlg-equipment-edit", new EquipmentEditViewModel(api, toasts, log, card)));
            dialogs.Add(("13-dlg-equipment-card", new EquipmentCardViewModel(card)));
        }

        if (firstOrder is not null)
        {
            dialogs.Add(("17-dlg-complete-order", new CompleteOrderViewModel(api, toasts, log, firstOrder)));
        }

        foreach (var (name, model) in dialogs)
        {
            var window = new DialogWindow(model) { SizeToContent = SizeToContent.Manual, Height = 760 };
            await Shot(name, window, () => model.RefreshAsync());
        }

        // Действия через формы: те же команды, что у кнопок «Сохранить».
        async Task Act(string name, Func<Task<string?>> act)
        {
            try
            {
                var error = await act();
                if (error is null)
                {
                    Console.WriteLine($"[UI-OK] {name}");
                }
                else
                {
                    failures++;
                    Console.WriteLine($"[UI-FAIL] {name}: {error}");
                }
            }
            catch (Exception ex)
            {
                failures++;
                Console.WriteLine($"[UI-FAIL] {name}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        await Act("форма: новое оборудование сохраняется", async () =>
        {
            var form = new EquipmentEditViewModel(api, toasts, log, null);
            await form.RefreshAsync();
            form.InventoryNumber = "UI-" + DateTime.Now.ToString("HHmmss");
            form.Name = "Проверочный станок";
            await form.SaveCommand.ExecuteAsync(null);
            return form.Result ? null : string.Join("; ", form.Errors);
        });

        await Act("форма: даты оборудования проверяются до отправки", async () =>
        {
            var form = new EquipmentEditViewModel(api, toasts, log, null);
            await form.RefreshAsync();
            form.InventoryNumber = "UI-BAD";
            form.Name = "Неверные даты";
            form.OperationDate = form.CommissionDate!.Value.AddDays(-5);
            await form.SaveCommand.ExecuteAsync(null);
            return !form.Result && form.Errors.Any(e => e.Contains("ввода в работу", StringComparison.OrdinalIgnoreCase)) ? null : "ошибка не показана";
        });

        await Act("форма: аварийная заявка без причины не уходит", async () =>
        {
            var form = new WorkOrderEditViewModel(api, toasts, log);
            await form.RefreshAsync();
            form.SelectedEquipment = form.Equipment.FirstOrDefault();
            form.OrderTitle = "Проверка формы";
            form.Description = "Описание";
            await form.SaveCommand.ExecuteAsync(null);
            return !form.Result && form.Errors.Count > 0 ? null : "проверка не сработала";
        });

        await Act("форма: аварийная заявка создаётся", async () =>
        {
            var form = new WorkOrderEditViewModel(api, toasts, log);
            await form.RefreshAsync();
            form.SelectedEquipment = form.Equipment.FirstOrDefault();
            form.OrderTitle = "Проверка формы";
            form.Description = "Описание";
            form.FailureReason = "Износ";
            await form.SaveCommand.ExecuteAsync(null);
            return form.Result ? null : string.Join("; ", form.Errors);
        });

        await Act("график ТО: «Создать заявку» по записи", async () =>
        {
            var schedule = new ScheduleViewModel(api, toasts, log);
            await schedule.RefreshAsync();
            var row = schedule.Rows.FirstOrDefault(r => r.Status == "Запланировано");
            if (row is null)
            {
                return "нет запланированной записи";
            }

            schedule.Selected = row;
            await schedule.CreateOrderCommand.ExecuteAsync(null);
            await schedule.RefreshAsync();
            var after = schedule.Rows.First(r => r.Id == row.Id);
            return after.Status == "Назначено" ? null : $"статус {after.Status}";
        });

        await Act("календарь ТО: просроченные записи красные", async () =>
        {
            var schedule = new ScheduleViewModel(api, toasts, log);
            await schedule.RefreshAsync();
            return schedule.CalendarDays.Count == 42 && schedule.OverdueCount > 0 ? null : $"дней {schedule.CalendarDays.Count}, просрочено {schedule.OverdueCount}";
        });

        // Экран блокировки.
        await Shot("18-lock", new MainWindow { DataContext = shell }, () =>
        {
            shell.LockScreenCommand.Execute(null);
            return Task.CompletedTask;
        });

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

        // «Value is null» — путь через ещё не выбранный объект (Selected.Name
        // при пустом выборе): панель скрыта, это не ошибка разметки.
        if (!text.Contains("Value is null", StringComparison.Ordinal))
        {
            Errors.Add($"{area}: {text} ({source?.GetType().Name})");
        }
    }
}
