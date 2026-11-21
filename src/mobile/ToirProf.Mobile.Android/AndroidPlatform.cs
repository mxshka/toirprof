using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Biometrics;
using Android.Locations;
using Android.Media;
using Android.OS;
using Android.Provider;
using AndroidX.Core.Content;
using ToirProf.Mobile.Services;
using ZXing;
using ZXing.Common;
using AppContext = Android.App.Application;
using JavaFile = Java.IO.File;

namespace ToirProf.Mobile.Droid;

/// <summary>
/// Возможности телефона на Android: отпечаток, геолокация, камера, распознавание
/// QR-кода и системные уведомления. Всё работает без интернета и без сервисов Google.
/// </summary>
public sealed class AndroidPlatform : IPlatformServices
{
    private const string ChannelId = "toirprof-orders";
    private const int MaxPhotoSide = 1280;
    private int _notificationId = 1;

    private static Context Ctx => AppContext.Context;

    private static MainActivity Activity =>
        MainActivity.Current ?? throw new InvalidOperationException("Экран приложения ещё не открыт.");

    public string DataDirectory => Ctx.FilesDir!.AbsolutePath;

    // Эмулятор Android видит компьютер разработчика по адресу 10.0.2.2.
    public string DefaultServerUrl => "http://10.0.2.2:5080";

    public bool BiometricAvailable
    {
        get
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(28))
            {
                return false;
            }

            if (OperatingSystem.IsAndroidVersionAtLeast(29))
            {
                var manager = (BiometricManager?)Ctx.GetSystemService(Java.Lang.Class.FromType(typeof(BiometricManager)));
#pragma warning disable CA1422 // проверка «есть ли отпечаток» одинакова для Android 10 и новее
                return manager?.CanAuthenticate() == BiometricCode.Success;
#pragma warning restore CA1422
            }

            return Ctx.PackageManager?.HasSystemFeature(PackageManager.FeatureFingerprint) == true;
        }
    }

    public Task<bool> AuthenticateBiometricAsync(string title, string subtitle)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return Task.FromResult(false);
        }

        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var activity = Activity;
        var executor = activity.MainExecutor!;
        var prompt = new BiometricPrompt.Builder(activity)
            .SetTitle(title)
            .SetSubtitle(subtitle)
            .SetNegativeButton("Ввести пароль", executor, new CancelListener(result))
            .Build();
        prompt.Authenticate(new CancellationSignal(), executor, new BiometricCallback(result));
        return result.Task;
    }

    public async Task<GeoPoint?> GetLocationAsync()
    {
        var granted = await Activity.RequestPermissionsAsync(
            Manifest.Permission.AccessFineLocation, Manifest.Permission.AccessCoarseLocation);
        if (!granted)
        {
            return null;
        }

        var manager = (LocationManager?)Ctx.GetSystemService(Context.LocationService);
        if (manager is null)
        {
            return null;
        }

        string[] providers = [LocationManager.GpsProvider, LocationManager.NetworkProvider];
        var enabled = providers.Where(p => manager.IsProviderEnabled(p)).ToList();

        // Свежая (не старше 10 минут) последняя точка — без ожидания спутников.
        var last = enabled
            .Select(p => manager.GetLastKnownLocation(p))
            .Where(l => l is not null)
            .OrderByDescending(l => l!.Time)
            .FirstOrDefault();
        if (last is not null && DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - last.Time < 10 * 60 * 1000)
        {
            return new GeoPoint(last.Latitude, last.Longitude);
        }

        if (enabled.Count == 0)
        {
            return last is null ? null : new GeoPoint(last.Latitude, last.Longitude);
        }

        var listener = new OneShotLocationListener();
        foreach (var provider in enabled)
        {
            manager.RequestLocationUpdates(provider, 0, 0f, listener, Looper.MainLooper);
        }

        try
        {
            var fresh = await listener.Result.WaitAsync(TimeSpan.FromSeconds(15));
            return new GeoPoint(fresh.Latitude, fresh.Longitude);
        }
        catch (TimeoutException)
        {
            return last is null ? null : new GeoPoint(last.Latitude, last.Longitude);
        }
        finally
        {
            manager.RemoveUpdates(listener);
        }
    }

    public async Task<PhotoData?> TakePhotoAsync()
    {
        var path = await CaptureAsync();
        if (path is null)
        {
            return null;
        }

        try
        {
            using var bitmap = LoadScaled(path, MaxPhotoSide);
            if (bitmap is null)
            {
                return null;
            }

            using var stream = new MemoryStream();
            bitmap.Compress(Bitmap.CompressFormat.Jpeg!, 80, stream);
            return new PhotoData(stream.ToArray(), "image/jpeg");
        }
        finally
        {
            File.Delete(path);
        }
    }

    public async Task<string?> ScanCodeAsync()
    {
        var path = await CaptureAsync();
        if (path is null)
        {
            return null;
        }

        try
        {
            using var bitmap = LoadScaled(path, 1600);
            if (bitmap is null)
            {
                return null;
            }

            var width = bitmap.Width;
            var height = bitmap.Height;
            var pixels = new int[width * height];
            bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);

            // Android хранит пиксель как ARGB в int; в памяти (little-endian) это B, G, R, A.
            var bytes = new byte[pixels.Length * 4];
            Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);

            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    CharacterSet = "UTF-8",
                },
            };
            var source = new RGBLuminanceSource(bytes, width, height, RGBLuminanceSource.BitmapFormat.BGRA32);
            return reader.Decode(source)?.Text;
        }
        finally
        {
            File.Delete(path);
        }
    }

    public void ShowNotification(string title, string text)
    {
        var manager = (NotificationManager?)Ctx.GetSystemService(Context.NotificationService);
        if (manager is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(33)
            && Ctx.CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted)
        {
            // Разрешение запрашивается один раз; уведомление покажется со следующего опроса.
            _ = MainActivity.Current?.RequestPermissionsAsync(Manifest.Permission.PostNotifications);
            return;
        }

        var open = Ctx.PackageManager?.GetLaunchIntentForPackage(Ctx.PackageName!);
        var pending = open is null
            ? null
            : PendingIntent.GetActivity(Ctx, 0, open, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        Notification.Builder builder;
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            if (manager.GetNotificationChannel(ChannelId) is null)
            {
                manager.CreateNotificationChannel(
                    new NotificationChannel(ChannelId, "Заявки бригады", NotificationImportance.High)
                    {
                        Description = "Назначения, изменения статусов и просрочки",
                    });
            }

            builder = new Notification.Builder(Ctx, ChannelId);
        }
        else
        {
#pragma warning disable CA1422 // для Android 6–7 другого конструктора нет
            builder = new Notification.Builder(Ctx);
#pragma warning restore CA1422
        }

        builder.SetSmallIcon(Resource.Drawable.Icon)
            .SetContentTitle(title)
            .SetContentText(text)
            .SetStyle(new Notification.BigTextStyle().BigText(text))
            .SetAutoCancel(true);
        if (pending is not null)
        {
            builder.SetContentIntent(pending);
        }

        manager.Notify(Interlocked.Increment(ref _notificationId), builder.Build());
    }

    /// <summary>Съёмка системной камерой в файл приложения; путь к снимку или null при отмене.</summary>
    private static async Task<string?> CaptureAsync()
    {
        var folder = new JavaFile(Ctx.CacheDir, "photos");
        folder.Mkdirs();
        var file = new JavaFile(folder, $"shot-{DateTime.Now:yyyyMMdd-HHmmss}.jpg");
        var uri = FileProvider.GetUriForFile(Ctx, Ctx.PackageName + ".files", file);

        var intent = new Intent(MediaStore.ActionImageCapture);
        intent.PutExtra(MediaStore.ExtraOutput, uri);
        intent.AddFlags(ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);

        var (code, _) = await Activity.StartForResultAsync(intent);
        if (code != global::Android.App.Result.Ok || !file.Exists() || file.Length() == 0)
        {
            file.Delete();
            return null;
        }

        return file.AbsolutePath;
    }

    /// <summary>Чтение снимка с уменьшением до maxSide по длинной стороне и поворотом по EXIF.</summary>
    private static Bitmap? LoadScaled(string path, int maxSide)
    {
        var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
        BitmapFactory.DecodeFile(path, bounds);
        var sample = 1;
        while (Math.Max(bounds.OutWidth, bounds.OutHeight) / (sample * 2) >= maxSide)
        {
            sample *= 2;
        }

        var bitmap = BitmapFactory.DecodeFile(path, new BitmapFactory.Options { InSampleSize = sample });
        if (bitmap is null)
        {
            return null;
        }

        var scale = (float)maxSide / Math.Max(bitmap.Width, bitmap.Height);
        var matrix = new Matrix();
        if (scale < 1f)
        {
            matrix.PostScale(scale, scale);
        }

        var orientation = new ExifInterface(path).GetAttributeInt(ExifInterface.TagOrientation, 1);
        var degrees = orientation switch { 6 => 90, 3 => 180, 8 => 270, _ => 0 };
        if (degrees != 0)
        {
            matrix.PostRotate(degrees);
        }

        if (matrix.IsIdentity)
        {
            return bitmap;
        }

        var result = Bitmap.CreateBitmap(bitmap, 0, 0, bitmap.Width, bitmap.Height, matrix, true);
        if (!ReferenceEquals(result, bitmap))
        {
            bitmap.Recycle();
        }

        return result;
    }

    private sealed class CancelListener(TaskCompletionSource<bool> result)
        : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => result.TrySetResult(false);
    }

    private sealed class BiometricCallback(TaskCompletionSource<bool> result) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? authResult) =>
            result.TrySetResult(true);

        public override void OnAuthenticationError(BiometricErrorCode errorCode, Java.Lang.ICharSequence? errString) =>
            result.TrySetResult(false);

        // Неудачная попытка (чужой палец) не закрывает окно: система даёт попробовать ещё.
        public override void OnAuthenticationFailed()
        {
        }
    }

    private sealed class OneShotLocationListener : Java.Lang.Object, ILocationListener
    {
        private readonly TaskCompletionSource<Location> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<Location> Result => _result.Task;

        public void OnLocationChanged(Location location) => _result.TrySetResult(location);

        public void OnProviderDisabled(string provider)
        {
        }

        public void OnProviderEnabled(string provider)
        {
        }

        public void OnStatusChanged(string? provider, Availability status, Bundle? extras)
        {
        }
    }
}
