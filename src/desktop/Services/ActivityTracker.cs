using System;

namespace ToirProf.Desktop.Services;

/// <summary>
/// Время последнего действия пользователя (мышь, клавиатура) во всех
/// окнах клиента. По нему главное окно включает автоблокировку.
/// </summary>
public static class ActivityTracker
{
    public static DateTime LastActivity { get; private set; } = DateTime.UtcNow;

    /// <summary>Сколько модальных окон сейчас открыто: при открытом окне экран не блокируется.</summary>
    public static int OpenDialogs { get; set; }

    public static void Touch() => LastActivity = DateTime.UtcNow;

    public static double IdleSeconds => (DateTime.UtcNow - LastActivity).TotalSeconds;
}
