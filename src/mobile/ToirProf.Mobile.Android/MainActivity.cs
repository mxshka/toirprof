using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia.Android;

namespace ToirProf.Mobile.Droid;

[Activity(
    Label = "ТОиР-Проф",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    private static int _nextRequest = 100;
    private static readonly Dictionary<int, TaskCompletionSource<(Result Code, Intent? Data)>> ActivityWaiters = [];
    private static readonly Dictionary<int, TaskCompletionSource<bool>> PermissionWaiters = [];

    /// <summary>Текущий экран приложения: нужен для камеры, разрешений и биометрии.</summary>
    public static MainActivity? Current { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Current = this;
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        Current = this;
        base.OnResume();
    }

    /// <summary>Запуск системного экрана (камеры) с ожиданием результата.</summary>
    public Task<(Result Code, Intent? Data)> StartForResultAsync(Intent intent)
    {
        var code = Interlocked.Increment(ref _nextRequest);
        var waiter = new TaskCompletionSource<(Result, Intent?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        ActivityWaiters[code] = waiter;
        StartActivityForResult(intent, code);
        return waiter.Task;
    }

    /// <summary>Запрос разрешений; true — выдано хотя бы одно из запрошенных.</summary>
    public Task<bool> RequestPermissionsAsync(params string[] permissions)
    {
        if (permissions.Any(p => CheckSelfPermission(p) == Permission.Granted))
        {
            return Task.FromResult(true);
        }

        var code = Interlocked.Increment(ref _nextRequest);
        var waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PermissionWaiters[code] = waiter;
        RequestPermissions(permissions, code);
        return waiter.Task;
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (ActivityWaiters.Remove(requestCode, out var waiter))
        {
            waiter.TrySetResult((resultCode, data));
        }
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (PermissionWaiters.Remove(requestCode, out var waiter))
        {
            waiter.TrySetResult(grantResults.Any(r => r == Permission.Granted));
        }
    }
}
