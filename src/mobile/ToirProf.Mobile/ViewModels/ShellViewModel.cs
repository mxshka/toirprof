using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Services;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Оболочка приложения: текущий экран, стек «назад», нижние вкладки,
/// плашка связи и всплывающие сообщения. Раз в 20 секунд отправляет
/// очередь офлайн-операций, проверяет уведомления и обновляет заявки.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly Stack<PageViewModel> _back = new();
    private readonly DispatcherTimer _timer;
    private int _ticks;

    public ShellViewModel(IPlatformServices platform)
    {
        Platform = platform;
        Store = new LocalStore(platform.DataDirectory);
        Store.Load();
        Api = new MobileApi();
        Api.SetServer(string.IsNullOrWhiteSpace(Store.Session.ServerUrl) ? platform.DefaultServerUrl : Store.Session.ServerUrl);
        Service = new CrewService(Api, Store, platform);

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += async (_, _) => await TickAsync();

        Navigate(new LoginViewModel(this), clearStack: true);
    }

    public IPlatformServices Platform { get; }

    public LocalStore Store { get; }

    public MobileApi Api { get; }

    public CrewService Service { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ShowTabs), nameof(IsOrdersTab), nameof(IsScanTab), nameof(IsNewTab), nameof(IsMoreTab))]
    private PageViewModel? _current;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private string? _toast;

    [ObservableProperty]
    private bool _hasToast;

    [ObservableProperty]
    private string _connection = "В сети";

    [ObservableProperty]
    private bool _isOffline;

    public string Title => Current?.Title ?? "ТОиР-Проф";

    public bool ShowTabs => Current?.ShowTabs ?? false;

    public bool IsOrdersTab => Current?.Tab == "orders";

    public bool IsScanTab => Current?.Tab == "scan";

    public bool IsNewTab => Current?.Tab == "new";

    public bool IsMoreTab => Current?.Tab == "more";

    public bool IsLoggedIn { get; private set; }

    public void Navigate(PageViewModel page, bool clearStack = false)
    {
        if (clearStack)
        {
            _back.Clear();
        }
        else if (Current is not null)
        {
            _back.Push(Current);
        }

        Current = page;
        CanGoBack = _back.Count > 0;
        _ = page.OnShowAsync();
    }

    [RelayCommand]
    public void Back()
    {
        if (_back.Count == 0)
        {
            return;
        }

        Current = _back.Pop();
        CanGoBack = _back.Count > 0;
        _ = Current.OnShowAsync();
    }

    [RelayCommand]
    public void ShowOrders() => Navigate(new OrdersViewModel(this), clearStack: true);

    [RelayCommand]
    public void ShowScanner() => Navigate(new EquipmentViewModel(this, null), clearStack: true);

    [RelayCommand]
    public void ShowNewOrder() => Navigate(new NewOrderViewModel(this, null), clearStack: true);

    [RelayCommand]
    public void ShowMore() => Navigate(new MoreViewModel(this), clearStack: true);

    public void OnLoggedIn()
    {
        IsLoggedIn = true;
        _timer.Start();
        ShowOrders();
        _ = TickAsync();
    }

    public void Logout()
    {
        IsLoggedIn = false;
        _timer.Stop();
        Store.ClearSession();
        Api.Token = null;
        Navigate(new LoginViewModel(this), clearStack: true);
    }

    public void ShowToast(string text)
    {
        Toast = text;
        HasToast = true;
        var shown = text;
        DispatcherTimer.RunOnce(() =>
        {
            if (Toast == shown)
            {
                HasToast = false;
            }
        }, TimeSpan.FromSeconds(3.5));
    }

    public void UpdateConnection()
    {
        IsOffline = !Api.IsOnline;
        var queue = Store.Queue.Count;
        Connection = (Api.IsOnline ? "В сети" : "Нет связи") + (queue > 0 ? $" · в очереди {queue}" : string.Empty);
    }

    /// <summary>Фоновая синхронизация по таймеру и по кнопке «Синхронизировать».</summary>
    public async Task SyncAsync(bool refresh)
    {
        if (!IsLoggedIn)
        {
            return;
        }

        var sent = await Service.FlushQueueAsync();
        await Service.PollNotificationsAsync();
        var refreshed = false;
        if (refresh || sent > 0)
        {
            refreshed = await Service.RefreshAsync();
        }

        UpdateConnection();
        if (sent > 0)
        {
            ShowToast($"Отправлено из очереди: {sent}");
        }

        if (refreshed && Current is OrdersViewModel orders)
        {
            orders.Reload();
        }
    }

    private async Task TickAsync()
    {
        _ticks++;
        try
        {
            await SyncAsync(refresh: _ticks % 3 == 1);
        }
        catch (Exception)
        {
            UpdateConnection();
        }
    }
}
