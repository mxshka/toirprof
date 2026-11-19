using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToirProf.Desktop.Services;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>
/// Основа модального окна: заголовок, список ошибок формы и закрытие
/// с результатом. Окно (Views/DialogWindow) находит разметку диалога
/// тем же ViewLocator, что и экраны.
/// </summary>
public abstract partial class DialogViewModel : PageViewModel
{
    protected DialogViewModel(ApiClient api, ToastService toasts, FileLogger log)
        : base(api, toasts, log)
    {
    }

    protected DialogViewModel()
    {
    }

    public event Action? CloseRequested;

    public bool Result { get; private set; }

    public abstract string Title { get; }

    public virtual double DialogWidth => 560;

    /// <summary>Ошибки проверки формы: показываются красным блоком над кнопками.</summary>
    public ObservableCollection<string> Errors { get; } = [];

    [ObservableProperty]
    private bool _hasErrors;

    public override Task RefreshAsync() => Task.CompletedTask;

    protected void SetErrors(params string[] errors) => SetErrors((System.Collections.Generic.IEnumerable<string>)errors);

    protected void SetErrors(System.Collections.Generic.IEnumerable<string> errors)
    {
        Errors.Clear();
        foreach (var e in errors)
        {
            Errors.Add(e);
        }

        HasErrors = Errors.Count > 0;
    }

    protected void CloseWith(bool result)
    {
        Result = result;
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseWith(false);
}
