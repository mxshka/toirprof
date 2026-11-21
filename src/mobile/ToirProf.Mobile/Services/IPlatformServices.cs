namespace ToirProf.Mobile.Services;

/// <summary>Координаты места: широта и долгота в градусах.</summary>
public sealed record GeoPoint(double Latitude, double Longitude)
{
    public override string ToString() =>
        $"{Latitude.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)}, " +
        $"{Longitude.ToString("0.00000", System.Globalization.CultureInfo.InvariantCulture)}";
}

/// <summary>Снимок с камеры: байты и тип содержимого (image/jpeg или image/png).</summary>
public sealed record PhotoData(byte[] Content, string ContentType);

/// <summary>
/// Возможности телефона, которые нужны клиенту бригады. На Android их
/// реализует AndroidPlatform, в проверке экранов без телефона — PreviewPlatform.
/// </summary>
public interface IPlatformServices
{
    /// <summary>Папка приложения для кэша, черновиков и очереди операций.</summary>
    string DataDirectory { get; }

    /// <summary>Адрес сервера по умолчанию (эмулятор Android видит компьютер как 10.0.2.2).</summary>
    string DefaultServerUrl { get; }

    /// <summary>Есть ли на устройстве биометрия (отпечаток, лицо).</summary>
    bool BiometricAvailable { get; }

    /// <summary>Биометрическая проверка: true — пользователь подтверждён.</summary>
    Task<bool> AuthenticateBiometricAsync(string title, string subtitle);

    /// <summary>Текущие координаты или null, если геолокация недоступна или запрещена.</summary>
    Task<GeoPoint?> GetLocationAsync();

    /// <summary>Снимок камерой (уже сжатый) или null, если съёмку отменили.</summary>
    Task<PhotoData?> TakePhotoAsync();

    /// <summary>Сканирование QR- или штрихкода камерой: текст кода или null.</summary>
    Task<string?> ScanCodeAsync();

    /// <summary>Системное уведомление (в шторке телефона).</summary>
    void ShowNotification(string title, string text);
}

/// <summary>
/// Платформа без телефона: для запуска на компьютере и проверки экранов.
/// Отпечатка и камеры нет, координаты — условная точка завода.
/// </summary>
public sealed class PreviewPlatform : IPlatformServices
{
    // PNG 1×1 — чтобы цепочка «снять фото → отправить» работала и без камеры.
    private static readonly byte[] DotPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    public PreviewPlatform(string? dataDirectory = null)
    {
        DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), "toirprof-mobile-preview");
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public string DefaultServerUrl => "http://localhost:5080";

    public bool BiometricAvailable => false;

    public List<string> Notifications { get; } = [];

    public string? NextScan { get; set; }

    public Task<bool> AuthenticateBiometricAsync(string title, string subtitle) => Task.FromResult(false);

    public Task<GeoPoint?> GetLocationAsync() => Task.FromResult<GeoPoint?>(new GeoPoint(55.751244, 37.618423));

    public Task<PhotoData?> TakePhotoAsync() => Task.FromResult<PhotoData?>(new PhotoData(DotPng, "image/png"));

    public Task<string?> ScanCodeAsync() => Task.FromResult(NextScan);

    public void ShowNotification(string title, string text) => Notifications.Add($"{title}: {text}");
}
