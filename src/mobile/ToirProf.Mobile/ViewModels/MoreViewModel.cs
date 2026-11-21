using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Mobile.Models;

namespace ToirProf.Mobile.ViewModels;

/// <summary>
/// Справка и профиль: инструкции по видам ТО, очередь неотправленных
/// операций, вход по отпечатку, синхронизация и выход.
/// </summary>
public sealed partial class MoreViewModel : PageViewModel
{
    public MoreViewModel(ShellViewModel shell)
        : base(shell)
    {
        foreach (var r in shell.Store.Reference)
        {
            Reference.Add(r);
        }

        Fill();
        _biometric = shell.Store.Session.BiometricEnabled;
    }

    public override string Title => "Справка";

    public override string Tab => "more";

    public ObservableCollection<ReferenceItem> Reference { get; } = [];

    public ObservableCollection<PendingOperation> Queue { get; } = [];

    [ObservableProperty]
    private bool _biometric;

    public bool BiometricHardware => Shell.Platform.BiometricAvailable;

    public string UserName => Shell.Store.Me?.FullName ?? Shell.Store.Session.Login ?? "—";

    public string UserDetails => Shell.Store.Me is { } me ? $"{me.Role} · {me.CrewsCaption}" : string.Empty;

    public string Server => Shell.Store.Session.ServerUrl;

    public bool HasQueue => Queue.Count > 0;

    partial void OnBiometricChanged(bool value)
    {
        Shell.Store.Session.BiometricEnabled = value && Shell.Platform.BiometricAvailable;
        Shell.Store.SaveSession();
    }

    [RelayCommand]
    private Task SyncAsync() => BusyAsync(async () =>
    {
        await Shell.SyncAsync(refresh: true);
        Reference.Clear();
        foreach (var r in Shell.Store.Reference)
        {
            Reference.Add(r);
        }

        Fill();
        Shell.ShowToast(Shell.Api.IsOnline ? "Данные обновлены." : "Нет связи с сервером.");
    });

    [RelayCommand]
    private void Logout() => Shell.Logout();

    private void Fill()
    {
        Queue.Clear();
        foreach (var q in Shell.Store.Queue)
        {
            Queue.Add(q);
        }

        OnPropertyChanged(nameof(HasQueue));
    }
}
