using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Вход: логин или email и пароль, либо отпечаток пальца, если он был
/// включён при прошлом входе и токен ещё действует. Без связи можно
/// открыть сохранённые на телефоне заявки.
/// </summary>
public sealed partial class LoginViewModel : PageViewModel
{
    private bool _autoPrompted;

    public LoginViewModel(ShellViewModel shell)
        : base(shell)
    {
        var session = shell.Store.Session;
        ServerUrl = string.IsNullOrWhiteSpace(session.ServerUrl) ? shell.Platform.DefaultServerUrl : session.ServerUrl;
        Login = session.Login ?? "master";
        UseBiometric = shell.Platform.BiometricAvailable;
    }

    public override string Title => "Вход";

    public override bool ShowTabs => false;

    [ObservableProperty]
    private string _serverUrl = string.Empty;

    [ObservableProperty]
    private string _login = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _useBiometric;

    [ObservableProperty]
    private string? _error;

    public bool BiometricHardware => Shell.Platform.BiometricAvailable;

    /// <summary>Вход по отпечатку: включён и токен ещё не истёк.</summary>
    public bool CanUseBiometric =>
        Shell.Platform.BiometricAvailable
        && Shell.Store.Session.BiometricEnabled
        && !string.IsNullOrEmpty(Shell.Store.Session.Token)
        && Shell.Store.Session.TokenExpiresAt > DateTime.UtcNow;

    /// <summary>Сохранённые заявки можно открыть без сети.</summary>
    public bool CanWorkOffline => !string.IsNullOrEmpty(Shell.Store.Session.Token) && Shell.Store.Orders.Count > 0;

    public override async Task OnShowAsync()
    {
        if (CanUseBiometric && !_autoPrompted)
        {
            _autoPrompted = true;
            await BiometricAsync();
        }
    }

    [RelayCommand]
    private Task SignInAsync() => BusyAsync(async () =>
    {
        Error = null;
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(Password))
        {
            Error = "Введите логин (или email) и пароль.";
            return;
        }

        if (!Uri.TryCreate(ServerUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            Error = "Адрес сервера — вида http://192.168.1.10:5080";
            return;
        }

        Shell.Api.SetServer(ServerUrl);
        var result = await Shell.Api.LoginAsync(Login.Trim(), Password);
        if (!result.Success || result.Data is null)
        {
            Error = result.StatusCode switch
            {
                0 => "Сервер недоступен. Проверьте адрес и что телефон в той же сети.",
                401 => "Неверный логин или пароль.",
                _ => result.Message,
            };
            return;
        }

        var session = Shell.Store.Session;
        session.ServerUrl = Shell.Api.BaseUrl;
        session.Token = result.Data.Token;
        session.TokenExpiresAt = DateTime.UtcNow.AddSeconds(result.Data.ExpiresIn > 0 ? result.Data.ExpiresIn : 8 * 3600);
        session.Login = Login.Trim();
        session.BiometricEnabled = UseBiometric && Shell.Platform.BiometricAvailable;
        Shell.Store.SaveSession();
        Password = string.Empty;

        await Shell.Service.RefreshAsync();
        Shell.OnLoggedIn();
        Shell.ShowToast(session.BiometricEnabled ? "Вход выполнен. В следующий раз — по отпечатку." : "Вход выполнен.");
    });

    [RelayCommand]
    private Task BiometricAsync() => BusyAsync(async () =>
    {
        Error = null;
        var ok = await Shell.Platform.AuthenticateBiometricAsync("Вход в ТОиР-Проф", "Приложите палец к сканеру");
        if (!ok)
        {
            Error = "Отпечаток не подтверждён. Войдите по паролю.";
            return;
        }

        Shell.Api.SetServer(Shell.Store.Session.ServerUrl);
        Shell.Api.Token = Shell.Store.Session.Token;
        await Shell.Service.RefreshAsync();
        Shell.OnLoggedIn();
        Shell.ShowToast("Вход по отпечатку выполнен.");
    });

    [RelayCommand]
    private void WorkOffline()
    {
        Shell.Api.SetServer(Shell.Store.Session.ServerUrl);
        Shell.Api.Token = Shell.Store.Session.Token;
        Shell.OnLoggedIn();
        Shell.ShowToast("Офлайн: показаны сохранённые заявки.");
    }
}
