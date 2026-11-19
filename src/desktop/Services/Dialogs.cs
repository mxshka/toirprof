using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ToirProf.Desktop.ViewModels.Screens;
using ToirProf.Desktop.Views;

namespace ToirProf.Desktop.Services;

/// <summary>
/// Модальные окна: формы, подтверждения и ошибки с подсказкой,
/// что делать дальше. Владелец — главное окно приложения.
/// </summary>
public static class Dialogs
{
    public static Window? Owner =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    public static async Task<bool> ShowAsync(DialogViewModel model)
    {
        var window = new DialogWindow(model);
        _ = model.RefreshAsync();

        var owner = Owner;
        if (owner is null)
        {
            window.Show();
            return false;
        }

        ActivityTracker.OpenDialogs++;
        try
        {
            await window.ShowDialog(owner);
        }
        finally
        {
            ActivityTracker.OpenDialogs--;
            ActivityTracker.Touch();
        }

        return model.Result;
    }

    public static Task<bool> ConfirmAsync(string title, string message, string okText = "Да", bool danger = false)
        => ShowAsync(new MessageDialogViewModel(title, message, null, okText, "Отмена", danger));

    public static Task ErrorAsync(string title, string message, string? hint = null, Func<Task>? retry = null)
        => ShowAsync(new MessageDialogViewModel(title, message, hint, "Понятно", null, true, retry));

    /// <summary>
    /// Ошибка ответа API в модальном окне: по коду ответа подбирается
    /// пояснение и действие (войти заново, обновить список, повторить).
    /// </summary>
    public static Task ApiErrorAsync<T>(ApiResult<T> result, Func<Task>? retry = null)
    {
        var (title, hint) = result.StatusCode switch
        {
            0 => ("Нет связи с сервером",
                "Проверьте, что API запущен (http://localhost:5080). Изменения сохранены в очереди и уйдут при появлении связи."),
            400 => ("Данные не прошли проверку", "Исправьте отмеченные поля и повторите."),
            401 => ("Сеанс истёк", "Выйдите и войдите заново."),
            403 => ("Недостаточно прав", "Операция доступна другой роли. Обратитесь к руководителю."),
            404 => ("Запись не найдена", "Возможно, её уже удалили. Обновите список (F5)."),
            409 => ("Конфликт данных", "Запись изменилась или уже используется. Обновите список (F5)."),
            >= 500 => ("Ошибка сервера", "Повторите позже. Подробности — в журнале API."),
            _ => ("Операция не выполнена", "Повторите действие."),
        };

        return ErrorAsync(title, result.Message, hint, retry);
    }
}
