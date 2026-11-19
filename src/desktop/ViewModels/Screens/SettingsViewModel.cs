using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>Вариант тайм-аута автоблокировки для выпадающего списка.</summary>
public sealed record LockOption(int Seconds, string Caption)
{
    public override string ToString() => Caption;
}

/// <summary>Настройки клиента: шрифт, строки на странице, автоблокировка, «Запомнить меня».</summary>
public sealed partial class SettingsViewModel : DialogViewModel
{
    public SettingsViewModel()
    {
        var s = UserSettings.Current;
        FontSize = (decimal)s.FontSize;
        PageSize = s.PageSize is 25 or 50 or 100 ? s.PageSize : 25;
        RememberMe = s.RememberMe;
        Lock = LockOptions.Find(o => o.Seconds == s.LockSeconds) ?? LockOptions[1];
    }

    public override string Title => "Настройки";

    public override double DialogWidth => 440;

    public IReadOnlyList<int> PageSizes { get; } = [25, 50, 100];

    public List<LockOption> LockOptions { get; } =
    [
        new(0, "не блокировать"),
        new(15, "через 15 секунд"),
        new(60, "через 1 минуту"),
        new(300, "через 5 минут"),
    ];

    [ObservableProperty]
    private decimal? _fontSize = 13;

    [ObservableProperty]
    private int _pageSize = 25;

    [ObservableProperty]
    private bool _rememberMe;

    [ObservableProperty]
    private LockOption? _lock;

    public string SettingsPath => UserSettings.FilePath;

    [RelayCommand]
    private void Save()
    {
        var s = UserSettings.Current;
        s.FontSize = (double)(FontSize ?? 13);
        s.PageSize = PageSize;
        s.RememberMe = RememberMe;
        s.LockSeconds = Lock?.Seconds ?? 15;
        s.Save();
        CloseWith(true);
    }
}
