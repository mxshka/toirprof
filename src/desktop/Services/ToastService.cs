using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace ToirProf.Desktop.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Всплывающее уведомление. Одна запись живёт <see cref="DefaultLifetime"/>
/// секунд и исчезает сама — пользователю не нужно ничего закрывать.
/// </summary>
public sealed class Toast(Guid id, string message, ToastKind kind, DateTimeOffset createdAt)
{
    public Guid Id { get; } = id;

    public string Message { get; } = message;

    public ToastKind Kind { get; } = kind;

    public DateTimeOffset CreatedAt { get; } = createdAt;

    /// <summary>Заголовок для окна: один символ заменяет иконку из ресурсов.</summary>
    public string Glyph => Kind switch
    {
        ToastKind.Success => "✓",
        ToastKind.Warning => "!",
        ToastKind.Error => "X",
        _ => "i",
    };
}

/// <summary>
/// Всплывающие сообщения для всех экранов. Экран не знает, где именно
/// будет показано уведомление: он просто вызывает Info или Error, а
/// оболочка подписана на <see cref="Items"/> и рисует список поверх окна.
///
/// Класс не зависит от Avalonia — в нём только коллекция и таймеры,
/// поэтому его можно использовать и в тестах.
/// </summary>
public sealed class ToastService : IDisposable
{
    public const int DefaultLifetime = 4;

    /// <summary>Больше пяти сообщений на экране превращаются в стену текста.</summary>
    public const int MaxVisible = 5;

    private readonly ObservableCollection<Toast> _items = new();

    // У каждого сообщения свой таймер: новое сообщение не продлевает жизнь старым.
    private readonly Dictionary<Guid, CancellationTokenSource> _timers = new();

    // Поток окна: коллекцию, к которой привязан список, можно менять только из него.
    private readonly SynchronizationContext? _ui;

    public ReadOnlyObservableCollection<Toast> Items { get; }

    public ToastService()
    {
        Items = new ReadOnlyObservableCollection<Toast>(_items);
        _ui = SynchronizationContext.Current;
    }

    public void Info(string message) => Show(message, ToastKind.Info);

    public void Success(string message) => Show(message, ToastKind.Success);

    public void Warn(string message) => Show(message, ToastKind.Warning);

    public void Error(string message) => Show(message, ToastKind.Error);

    public void Show(string message, ToastKind kind = ToastKind.Info, int lifetimeSeconds = DefaultLifetime)
    {
        // Одно и то же сообщение дважды подряд не дублируется — старое уходит, новое встаёт сверху.
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            if (_items[i].Message == message && _items[i].Kind == kind)
            {
                Remove(_items[i].Id);
            }
        }

        var toast = new Toast(Guid.NewGuid(), message, kind, DateTimeOffset.UtcNow);
        _items.Insert(0, toast);

        while (_items.Count > MaxVisible)
        {
            Remove(_items[^1].Id);
        }

        DismissLater(toast, lifetimeSeconds);
    }

    /// <summary>Показать сразу несколько сообщений — так удобно после загрузки экрана.</summary>
    public void ShowMany(IEnumerable<(string Message, ToastKind Kind)> messages)
    {
        foreach (var (message, kind) in messages)
        {
            Show(message, kind);
        }
    }

    public void Dismiss(Guid id) => Remove(id);

    public void Clear()
    {
        foreach (var timer in _timers.Values)
        {
            timer.Cancel();
            timer.Dispose();
        }

        _timers.Clear();
        _items.Clear();
    }

    private void Remove(Guid id)
    {
        if (_timers.Remove(id, out var timer))
        {
            timer.Cancel();
            timer.Dispose();
        }

        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Id == id)
            {
                _items.RemoveAt(i);
                return;
            }
        }
    }

    private void DismissLater(Toast toast, int lifetimeSeconds)
    {
        var cts = new CancellationTokenSource();
        _timers[toast.Id] = cts;
        var token = cts.Token;

        _ = Task.Delay(TimeSpan.FromSeconds(Math.Max(1, lifetimeSeconds)), token)
            .ContinueWith(
                _ =>
                {
                    if (_ui is null)
                    {
                        Remove(toast.Id);
                    }
                    else
                    {
                        _ui.Post(_ => Remove(toast.Id), null);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);
    }

    public void Dispose() => Clear();
}
