using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using ToirProf.Desktop.Models;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Desktop.Services;
using ToirProf.Desktop.ViewModels.Screens;

namespace ToirProf.Desktop.ViewModels;

/// <summary>
/// Оболочка desktop-клиента: боковое меню, верхняя панель, хлебные
/// крошки и всплывающие сообщения живут здесь, а наполняет их активный
/// экран. Один объект на всё окно — проще держать состояние в одном
/// месте, чем раздавать его по восьми представлениям.
///
/// Сам экран входа тоже создаётся здесь, но его поля живут в
/// <see cref="LoginViewModel" />: оболочка не должна знать, что такое
/// пароль. Единственное, что она делает, — ловит событие «вход удался»
/// и открывает дашборд.
/// </summary>
public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient _api;
    private readonly ToastService _toasts;
    private readonly FileLogger _log;
    private readonly NavigationService _nav;

    private PageViewModel? _page;

    public MainWindowViewModel(ApiClient api, ToastService toasts, FileLogger log, NavigationService nav)
    {
        _api = api;
        _toasts = toasts;
        _log = log;
        _nav = nav;

        VisibleMenu = new ReadOnlyObservableCollection<MenuItem>(
            new ObservableCollection<MenuItem>(NavigationService.All));

        ShowLogin();
        if (Page is LoginViewModel login)
        {
            _ = login.TryRestoreSessionAsync();
        }

        UiFontSize = UserSettings.Current.FontSize;

        // Таймер автоблокировки и опроса уведомлений.
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnClock();
        _clock.Start();
    }

    // ==================================================================
    //  Автоблокировка, уведомления, горячие клавиши, настройки
    // ==================================================================

    private readonly DispatcherTimer _clock;
    private DateTime _nextPoll = DateTime.UtcNow;

    [ObservableProperty]
    private double _uiFontSize = 13;

    [ObservableProperty]
    private bool _isLocked;

    [ObservableProperty]
    private string _unlockPassword = string.Empty;

    [ObservableProperty]
    private string? _unlockError;

    public ObservableCollection<NotificationItem> Notifications { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NotificationsCaption))]
    private int _unreadCount;

    [ObservableProperty]
    private bool _isNotificationsOpen;

    public string NotificationsCaption => UnreadCount > 0 ? $"Уведомления · {UnreadCount}" : "Уведомления";

    public string LockCaption => $"Экран заблокирован после {UserSettings.Current.LockSeconds} с бездействия";

    private void OnClock()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        var lockSeconds = UserSettings.Current.LockSeconds;
        if (!IsLocked && lockSeconds > 0 && ActivityTracker.OpenDialogs == 0
            && ActivityTracker.IdleSeconds >= lockSeconds)
        {
            LockScreen();
        }

        if (DateTime.UtcNow >= _nextPoll)
        {
            _nextPoll = DateTime.UtcNow.AddSeconds(30);
            _ = PollNotificationsAsync();
        }
    }

    [RelayCommand]
    private void LockScreen()
    {
        if (!IsLoggedIn)
        {
            return;
        }

        UnlockPassword = string.Empty;
        UnlockError = null;
        IsNotificationsOpen = false;
        IsLocked = true;
        OnPropertyChanged(nameof(LockCaption));
        _log.Info("Экран заблокирован по бездействию");
    }

    /// <summary>Разблокировка паролем текущего пользователя (повторный вход).</summary>
    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (string.IsNullOrEmpty(UnlockPassword))
        {
            UnlockError = "Введите пароль.";
            return;
        }

        var result = await _api.LoginAsync(_api.Username ?? string.Empty, UnlockPassword);
        UnlockPassword = string.Empty;
        if (!result.Success)
        {
            UnlockError = result.StatusCode == 401 ? "Неверный пароль." : result.Message;
            return;
        }

        UnlockError = null;
        IsLocked = false;
        ActivityTracker.Touch();
    }

    [RelayCommand]
    private void LogoutFromLock()
    {
        IsLocked = false;
        Logout();
    }

    public void Touch() => ActivityTracker.Touch();

    private async Task PollNotificationsAsync()
    {
        var result = await _api.GetAsync<List<NotificationItem>>("/api/notifications?limit=30");
        if (!result.Success || result.Data is null)
        {
            return;
        }

        var seen = UserSettings.Current.NotificationsSeenAt;
        var before = UnreadCount;
        Notifications.Clear();
        foreach (var n in result.Data)
        {
            Notifications.Add(n);
        }

        UnreadCount = result.Data.Count(n => n.CreatedAt.ToUniversalTime() > seen.ToUniversalTime());
        if (UnreadCount > before && before >= 0 && !IsNotificationsOpen)
        {
            var newest = result.Data.FirstOrDefault();
            if (newest is not null && UnreadCount - before <= 3)
            {
                _toasts.Info($"{newest.Title}: {newest.Text}");
            }
        }

        OnPropertyChanged(nameof(StatusText));
    }

    [RelayCommand]
    private void ToggleNotifications()
    {
        IsNotificationsOpen = !IsNotificationsOpen;
        if (IsNotificationsOpen && Notifications.Count > 0)
        {
            UserSettings.Current.NotificationsSeenAt = Notifications.Max(n => n.CreatedAt).ToUniversalTime();
            UserSettings.Current.Save();
            UnreadCount = 0;
        }
    }

    /// <summary>Ctrl+N — новая аварийная заявка с любого экрана.</summary>
    [RelayCommand]
    private async Task NewOrderAsync()
    {
        if (!IsLoggedIn || IsLocked)
        {
            return;
        }

        if (Page is not WorkOrdersViewModel)
        {
            await OpenAsync(ScreenKind.WorkOrders);
        }

        if (Page is WorkOrdersViewModel orders)
        {
            await orders.CreateAsync("Аварийная");
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        if (await Dialogs.ShowAsync(new SettingsViewModel()))
        {
            UiFontSize = UserSettings.Current.FontSize;
            _api.RememberSession = UserSettings.Current.RememberMe;
            _toasts.Success("Настройки сохранены.");
        }
    }

    [RelayCommand]
    private Task ShowHotkeysAsync() => Dialogs.ShowAsync(new MessageDialogViewModel(
        "Горячие клавиши",
        "Ctrl+1 … Ctrl+7 — экраны по порядку меню\nCtrl+N — новая аварийная заявка\nCtrl+L — заблокировать экран\n" +
        "F5 или Ctrl+R — обновить экран\nCtrl+, — настройки\nF1 — эта подсказка\nEsc — закрыть окно",
        null,
        "Понятно",
        null,
        false));

    /// <summary>
    /// Сервисы, доступные экранам. Оболочка владеет ими, но экрану нужны
    /// те же самые экземпляры — иначе журнал писался бы в один файл,
    /// а токен хранился бы в другом.
    /// </summary>
    public ApiClient Api => _api;

    public FileLogger Log => _log;

    public ReadOnlyObservableCollection<MenuItem> VisibleMenu { get; }

    public ReadOnlyObservableCollection<string> Breadcrumbs => _nav.Breadcrumbs;

    public ReadOnlyObservableCollection<Toast> Toasts => _toasts.Items;

    /// <summary>Активный экран. При смене экрана старый освобождает подписки.</summary>
    public PageViewModel? Page
    {
        get => _page;
        private set
        {
            var previous = _page;
            if (SetProperty(ref _page, value))
            {
                OnPropertyChanged(nameof(IsLoggedIn));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(WindowTitle));
                OnPropertyChanged(nameof(CurrentScreenTitle));
                OnPropertyChanged(nameof(CanGoBack));
                previous?.Dispose();
            }
        }
    }

    public bool IsLoggedIn => _nav.Current != ScreenKind.Login;

    /// <summary>
    /// Правая часть верхней панели. Собирается из того, что клиент знает
    /// наверняка: кто вошёл, есть ли связь, сколько операций ждёт отправки.
    /// </summary>
    public string StatusText
    {
        get
        {
            if (!IsLoggedIn)
            {
                return "Не авторизован";
            }

            var link = _api.IsOffline ? "Нет связи" : "Связь есть";
            var queue = _api.PendingQueueCount > 0 ? $", в очереди {_api.PendingQueueCount}" : string.Empty;
            return $"{_api.Username} · {_api.Role} · {link}{queue}";
        }
    }

    public string WindowTitle => IsLoggedIn
        ? $"ТОиР-Проф — {_api.Username} ({_api.Role})"
        : "ТОиР-Проф — вход";

    public bool CanGoBack => _nav.Current != ScreenKind.Login && _nav.Current != ScreenKind.Dashboard;

    public string CurrentScreenTitle => _nav.CurrentTitle;

    // ------------------------------------------------------------ состояние

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _busyCaption = string.Empty;

    // ------------------------------------------------------------ навигация

    [RelayCommand]
    private Task OpenDashboardAsync() => OpenAsync(ScreenKind.Dashboard);

    [RelayCommand]
    private Task OpenEquipmentAsync() => OpenAsync(ScreenKind.Equipment);

    [RelayCommand]
    private Task OpenScheduleAsync() => OpenAsync(ScreenKind.Schedule);

    [RelayCommand]
    private Task OpenWorkOrdersAsync() => OpenAsync(ScreenKind.WorkOrders);

    [RelayCommand]
    private Task OpenCrewsAsync() => OpenAsync(ScreenKind.Crews);

    [RelayCommand]
    private Task OpenMaterialsAsync() => OpenAsync(ScreenKind.Materials);

    [RelayCommand]
    private Task OpenReportsAsync() => OpenAsync(ScreenKind.Reports);

    /// <summary>
    /// Открыть экран по имени пункта меню. Вызывается из разметки, где
    /// параметр команды приходит строкой, а не перечислением.
    /// </summary>
    [RelayCommand]
    private Task OpenByNameAsync(string? screen)
    {
        if (!Enum.TryParse<ScreenKind>(screen, out var kind))
        {
            return Task.CompletedTask;
        }

        return OpenAsync(kind);
    }

    /// <summary>
    /// Переход на экран. Права проверяет NavigationService: если роль не
    /// подходит, показываем тост и остаёмся на текущем экране — так
    /// эксперту проще понять, что произошло.
    /// </summary>
    private async Task OpenAsync(ScreenKind screen)
    {
        if (screen == ScreenKind.Login)
        {
            ShowLogin();
            return;
        }

        if (!_nav.GoTo(screen, _api.Role))
        {
            _toasts.Warn($"У роли {_api.Role} нет доступа к экрану «{NavigationService.TitleOf(screen)}».");
            return;
        }

        var page = BuildPage(screen);
        AttachAuthenticated(page);
        Page = page;

        IsBusy = true;
        BusyCaption = $"Загружаем «{NavigationService.TitleOf(screen)}»…";
        try
        {
            await page.RefreshAsync();
            _log.Info($"Открыт экран {screen}");
        }
        finally
        {
            IsBusy = false;
            BusyCaption = string.Empty;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    [RelayCommand]
    private void Logout()
    {
        IsLocked = false;
        IsNotificationsOpen = false;
        Notifications.Clear();
        UnreadCount = 0;
        _api.Logout();
        _toasts.Info("Сеанс завершён");
        ShowLogin();
    }

    /// <summary>
    /// Экран входа. Создаётся заново при каждом выходе из сеанса, чтобы
    /// поле «пароль» и счётчик неудачных попыток начинали с нуля.
    /// </summary>
    private void ShowLogin()
    {
        _nav.Reset();
        var login = new LoginViewModel(_api, _toasts, _log);
        login.Authenticated += OnAuthenticated;
        Page = login;
        OnPropertyChanged(nameof(CanGoBack));
    }

    /// <summary>
    /// Создаёт представление экрана. Один раз в одном месте — чтобы не
    /// разъехалось: экран добавлен в меню, но забыт здесь.
    /// </summary>
    private PageViewModel BuildPage(ScreenKind screen) => screen switch
    {
        ScreenKind.Dashboard => new DashboardViewModel(_api, _toasts, _log),
        ScreenKind.Equipment => new EquipmentViewModel(_api, _toasts, _log),
        ScreenKind.Schedule => new ScheduleViewModel(_api, _toasts, _log),
        ScreenKind.WorkOrders => new WorkOrdersViewModel(_api, _toasts, _log),
        ScreenKind.Crews => new CrewsViewModel(_api, _toasts, _log),
        ScreenKind.Materials => new MaterialsViewModel(_api, _toasts, _log),
        ScreenKind.Reports => new ReportsViewModel(_api, _toasts, _log),
        _ => new LoginViewModel(_api, _toasts, _log),
    };

    private void AttachAuthenticated(PageViewModel page)
    {
        if (page is LoginViewModel login)
        {
            login.Authenticated += OnAuthenticated;
        }
    }

    private async void OnAuthenticated()
    {
        // Событие приходит из команды входа, поэтому обработчик обязан
        // быть async void: подписчик не ждёт результата. Исключения
        // внутри перехватываются оболочкой и превращаются в тост.
        try
        {
            await OpenAsync(ScreenKind.Dashboard);
        }
        catch (Exception ex)
        {
            _log.Error("Не удалось открыть дашборд после входа", ex);
            _toasts.Error(ex.Message);
        }
    }

    // ------------------------------------------------------------- прочее

    [RelayCommand]
    private void Refresh()
    {
        if (Page is not null)
        {
            _ = RefreshPageAsync();
        }
    }

    private async Task RefreshPageAsync()
    {
        IsBusy = true;
        BusyCaption = "Обновляем данные…";
        try
        {
            await Page!.RefreshAsync();
        }
        finally
        {
            IsBusy = false;
            BusyCaption = string.Empty;
            OnPropertyChanged(nameof(StatusText));
        }
    }

    public void Dispose()
    {
        _clock.Stop();
        _toasts.Dispose();
        _api.Dispose();
        GC.SuppressFinalize(this);
    }
}
