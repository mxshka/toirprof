using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ToirProf.Desktop.Services;

/// <summary>Сохранённый шаблон отчёта: вид, длина периода и цех.</summary>
public sealed class ReportTemplate
{
    public string Name { get; set; } = string.Empty;

    public string Kind { get; set; } = "downtime";

    public int PeriodDays { get; set; } = 30;

    public Guid? WorkshopId { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Пользовательские настройки клиента: «Запомнить меня», последний логин,
/// размер шрифта, строк на странице, тайм-аут автоблокировки, шаблоны
/// отчётов и отметка о прочитанных уведомлениях.
///
/// Хранятся в %LOCALAPPDATA%\ToirProf\settings.json и переживают
/// перезапуск. Ошибка чтения не роняет клиент — берутся значения по умолчанию.
/// </summary>
public sealed class UserSettings
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static UserSettings? _current;

    public static string FilePath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ToirProf",
        "settings.json");

    public static UserSettings Current => _current ??= Load();

    public bool RememberMe { get; set; }

    public string? LastLogin { get; set; }

    public double FontSize { get; set; } = 13;

    public int PageSize { get; set; } = 25;

    /// <summary>Автоблокировка после бездействия, секунд. 0 — выключена.</summary>
    public int LockSeconds { get; set; } = 15;

    public DateTime NotificationsSeenAt { get; set; } = DateTime.UtcNow.AddDays(-1);

    public List<ReportTemplate> ReportTemplates { get; set; } = [];

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(FilePath), Json) ?? new UserSettings();
            }
        }
        catch (Exception)
        {
            // Повреждённый файл не мешает запуску: начинаем с настроек по умолчанию.
        }

        return new UserSettings();
    }

    public void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception)
        {
            // Нет прав на запись — настройки останутся до конца сеанса.
        }
    }
}
