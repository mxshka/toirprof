using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace ToirProf.Desktop.ViewModels.Screens;

/// <summary>Модальное сообщение: ошибка с подсказкой или подтверждение.</summary>
public sealed partial class MessageDialogViewModel : DialogViewModel
{
    private readonly Func<Task>? _retry;
    private readonly string _title;

    public MessageDialogViewModel(
        string title,
        string message,
        string? hint,
        string okText,
        string? cancelText,
        bool danger,
        Func<Task>? retry = null)
    {
        _title = title;
        Message = message;
        Hint = hint;
        OkText = okText;
        CancelText = cancelText;
        IsDanger = danger;
        _retry = retry;
    }

    public override string Title => _title;

    public override double DialogWidth => 460;

    public string Message { get; }

    public string? Hint { get; }

    public string OkText { get; }

    public string? CancelText { get; }

    public bool HasCancel => CancelText is not null;

    public bool HasRetry => _retry is not null;

    public bool IsDanger { get; }

    [RelayCommand]
    private void Ok() => CloseWith(true);

    [RelayCommand]
    private async Task RepeatAsync()
    {
        CloseWith(false);
        if (_retry is not null)
        {
            await _retry();
        }
    }
}
