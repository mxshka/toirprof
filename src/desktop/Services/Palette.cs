using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace ToirProf.Desktop.Services;

/// <summary>
/// Цвета для подсветки из кода: приоритет и статус заявки, ячейки
/// календаря ТО, столбики диаграмм. Значения совпадают с Styles/Theme.axaml.
/// </summary>
public static class Palette
{
    public static readonly IBrush Bad = Make("#D0553F");
    public static readonly IBrush BadSoft = Make("#43241F");
    public static readonly IBrush Wait = Make("#C79A2E");
    public static readonly IBrush WaitSoft = Make("#3C3320");
    public static readonly IBrush Ok = Make("#3FA66B");
    public static readonly IBrush OkSoft = Make("#1F3A2C");
    public static readonly IBrush Accent = Make("#3D7DD6");
    public static readonly IBrush AccentSoft = Make("#26364D");
    public static readonly IBrush Surface = Make("#242A31");
    public static readonly IBrush SurfaceAlt = Make("#2C333B");
    public static readonly IBrush Muted = Make("#9AA6B2");
    public static readonly IBrush None = Brushes.Transparent;

    public static IBrush Make(string hex) => new SolidColorBrush(Color.Parse(hex));

    public static IBrush ForPriority(string? priority) => priority switch
    {
        "Критический" => Bad,
        "Высокий" => Wait,
        "Средний" => Accent,
        _ => Muted,
    };

    public static IBrush ForStatus(string? status) => status switch
    {
        "Создана" => Muted,
        "Назначена" => Accent,
        "В работе" => Wait,
        "Ожидает материалов" => Bad,
        "Выполнена" => Ok,
        "Закрыта" => Ok,
        "Просрочено" => Bad,
        "Выполнено" => Ok,
        "Назначено" => Accent,
        _ => Muted,
    };
}

/// <summary>Столбик горизонтальной диаграммы: подпись, значение и ширина в пикселях.</summary>
public sealed record BarItem(string Label, string Caption, double Width, IBrush Fill)
{
    public static IReadOnlyList<BarItem> Build(
        IEnumerable<(string Label, decimal Value, string Caption)> data,
        double maxWidth = 320,
        IBrush? fill = null)
    {
        var rows = data.ToList();
        var max = rows.Count == 0 ? 0m : rows.Max(r => r.Value);
        return rows
            .Select(r => new BarItem(
                r.Label,
                r.Caption,
                max <= 0 ? 2 : Math.Max(2, (double)(r.Value / max) * maxWidth),
                fill ?? Palette.Accent))
            .ToList();
    }
}
