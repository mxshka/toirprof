"""Диаграмма деятельности «Выполнение заявки на ремонт и ТО».

Требование листа задания: начало и конец, действия, ветвления, fork/join и
дорожки. Дорожки соответствуют участникам процесса, поэтому по диаграмме
видно, кто за что отвечает и где границы между человеком и системой.
"""

from __future__ import annotations

from svgkit import ACCENT, BAD, INK, LANE_A, LANE_B, LINE, MUTED, OK, PAPER, WARN, Canvas, Drawio

TITLE = "Диаграмма деятельности — выполнение заявки на ремонт и ТО"

WIDTH = 1280

# Дорожки слева направо. Каждый шаг стоит в своей дорожке и в своей строке,
# переходы проложены вручную по свободным коридорам: ни одна линия не
# проходит сквозь действие или ромб, у каждой есть стрелка.
LANES = ["Мастер", "Система", "Бригада", "Руководитель"]
LANE_W = 300.0
LANE_X = 40.0
LANE_TOP = 96.0
HEAD_H = 34.0

BOX_W = 214.0
BOX_H = 46.0
DIA_W = 96.0
DIA_H = 30.0
BAR_H = 7.0
ROW = 72.0
FIRST_ROW = LANE_TOP + HEAD_H + 40.0

M, S, B, R = 0, 1, 2, 3

# (ключ, дорожка, строка, подпись, вид)
STEPS = [
    ("start", M, 0, "", "start"),
    ("a1", M, 1, "Заполнить заявку:\nоборудование, описание, приоритет", "action"),
    ("d1", S, 2, "Оборудование\nв эксплуатации?", "decision"),
    ("e1", M, 3, "Отказ: заявка\nна выведенное оборудование", "action"),
    ("end0", M, 4, "", "end"),
    ("a2", S, 3, "Сгенерировать номер\nZ-YYYY-XXXX", "action"),
    ("a3", S, 4, "Сохранить заявку,\nстатус «Создана»", "action"),
    ("a4", R, 5, "Назначить бригаду", "action"),
    ("d2", S, 6, "Бригада\nсвободна?", "decision"),
    ("e2", R, 6, "Выбрать другую\nбригаду", "action"),
    ("a5", S, 7, "Статус «Назначена»,\nуведомление бригаде", "action"),
    ("b1", B, 8, "Подготовить\nматериалы и ЗИП", "action"),
    ("b2", B, 9, "Прийти на объект,\nнажать «Начать работы»", "action"),
    ("fork", S, 10, "", "fork"),
    ("a6", S, 11, "Статус «В работе»,\nоборудование «В ремонте»", "action"),
    ("b3", B, 11, "Выполнить ремонт\nили ТО, фото «до»", "action"),
    ("join", S, 12, "", "join"),
    ("d3", B, 13, "Нужен\nматериал?", "decision"),
    ("b4", B, 14, "Списать материал\nиз ЗИП", "action"),
    ("d4", S, 15, "Остаток\nдостаточен?", "decision"),
    ("e3", M, 15, "Оформить приход\nили заявку поставщику", "action"),
    ("b5", B, 16, "Завершить работу:\nпричина, фото «после»", "action"),
    ("d5", S, 17, "Даты и причина\nзаполнены?", "decision"),
    ("a7", S, 18, "Рассчитать простой,\nстатус «Выполнена»", "action"),
    ("a9", M, 19, "Проверить и закрыть\nзаявку", "action"),
    ("a10", S, 20, "Статус «Закрыта»,\nоборудование «В работе»", "action"),
    ("a8", S, 21, "Записать в журнал,\nобновить аналитику", "action"),
    ("end", S, 22, "", "end"),
]

KIND = {key: kind for key, _l, _r, _t, kind in STEPS}

# (из, в, подпись, маршрут). Маршрут: "down" — прямо вниз; "side" — выйти
# сбоку к колонке цели и войти сверху; иначе — функция, строящая точки.
EDGES = [
    ("start", "a1", "", "down"),
    ("a1", "d1", "", "side"),
    ("d1", "a2", "да", "down"),
    ("d1", "e1", "нет", "side"),
    ("e1", "end0", "", "down"),
    ("a2", "a3", "", "down"),
    ("a3", "a4", "", "side"),
    ("a4", "d2", "", "side"),
    ("d2", "a5", "да", "down"),
    ("d2", "e2", "нет", "row"),
    ("e2", "a4", "", "up"),
    ("a5", "b1", "", "side"),
    ("b1", "b2", "", "down"),
    ("b2", "fork", "", "bar_in"),
    ("fork", "a6", "", "bar_out"),
    ("fork", "b3", "", "bar_out"),
    ("a6", "join", "", "bar_in"),
    ("b3", "join", "", "bar_in"),
    ("join", "d3", "", "bar_out"),
    ("d3", "b4", "да", "down"),
    ("d3", "b5", "нет", "bypass_right"),
    ("b4", "d4", "", "side"),
    ("d4", "e3", "нет", "row"),
    ("d4", "b5", "да", "side"),
    ("e3", "d4", "после прихода", "loop_under"),
    ("b5", "d5", "", "side"),
    ("d5", "b5", "нет, уточнить", "back_up"),
    ("d5", "a7", "да", "down"),
    ("a7", "a9", "", "side"),
    ("a9", "a10", "", "side"),
    ("a10", "a8", "", "down"),
    ("a8", "end", "", "down"),
]

LAST_ROW = max(row for _k, _l, row, _t, _kind in STEPS)
LANE_H = FIRST_ROW + ROW * LAST_ROW + 50.0 - LANE_TOP
HEIGHT = int(LANE_TOP + LANE_H + 140)


def lane_center(lane: int) -> float:
    return LANE_X + LANE_W * lane + LANE_W / 2


def positions() -> dict[str, tuple[float, float]]:
    return {key: (lane_center(lane), FIRST_ROW + ROW * row) for key, lane, row, _t, _kind in STEPS}


def half(key: str) -> tuple[float, float]:
    """Половина ширины и высоты узла."""
    kind = KIND[key]
    if kind == "action":
        return BOX_W / 2, BOX_H / 2
    if kind == "decision":
        return DIA_W, DIA_H
    if kind in {"fork", "join"}:
        return BAR_SPAN / 2, BAR_H / 2
    return 15.0, 15.0


# Полоса fork/join перекрывает дорожки «Система» и «Бригада».
BAR_SPAN = lane_center(B) - lane_center(S) + 120.0
BAR_CX = (lane_center(S) + lane_center(B)) / 2


def center(pos: dict[str, tuple[float, float]], key: str) -> tuple[float, float]:
    if KIND[key] in {"fork", "join"}:
        return BAR_CX, pos[key][1]
    return pos[key]


def route(pos: dict[str, tuple[float, float]], source: str, target: str, how: str) -> list[tuple[float, float]]:
    sx, sy = center(pos, source)
    tx, ty = center(pos, target)
    shw, shh = half(source)
    thw, thh = half(target)
    if how == "down":
        return [(sx, sy + shh), (tx, ty - thh)]
    if how == "up":
        return [(sx, sy - shh), (tx, ty + thh)]
    if how == "side":
        exit_x = sx + shw if tx > sx else sx - shw
        return [(exit_x, sy), (tx, sy), (tx, ty - thh)]
    if how == "row":
        exit_x = sx + shw if tx > sx else sx - shw
        entry_x = tx - thw if tx > sx else tx + thw
        return [(exit_x, sy), (entry_x, ty)]
    if how == "bar_in":
        return [(pos[source][0], sy + shh), (pos[source][0], ty - thh)]
    if how == "bar_out":
        return [(pos[target][0], sy + shh), (pos[target][0], ty - thh)]
    if how == "bypass_right":
        lane_edge = sx + LANE_W / 2 - 18
        return [(sx + shw, sy), (lane_edge, sy), (lane_edge, ty), (tx + thw, ty)]
    if how == "loop_under":
        below = sy + shh + 18
        return [(sx, sy + shh), (sx, below), (tx, below), (tx, ty + thh)]
    if how == "back_up":
        return [(sx + shw, sy), (tx, sy), (tx, ty + thh)]
    raise ValueError(how)


def _terminal(canvas: Canvas, cx: float, cy: float, final: bool) -> None:
    if final:
        canvas.ellipse(cx, cy, 15, 15, fill=PAPER, stroke=INK, width=1.6)
        canvas.ellipse(cx, cy, 9, 9, fill=INK, stroke=INK, width=1)
    else:
        canvas.ellipse(cx, cy, 14, 14, fill=INK, stroke=INK, width=1.6)


def _action(canvas: Canvas, cx: float, cy: float, lines: list[str]) -> None:
    canvas.rect(cx - BOX_W / 2, cy - BOX_H / 2, BOX_W, BOX_H, fill=LANE_B, stroke=INK, width=1.3, rx=12)
    for index, line in enumerate(lines):
        offset = (index - (len(lines) - 1) / 2) * 14
        canvas.text(cx, cy + 4 + offset, line, size=11, anchor="middle")


def _decision(canvas: Canvas, cx: float, cy: float, lines: list[str]) -> None:
    canvas.path(
        f"M{cx:g},{cy - DIA_H:g} L{cx + DIA_W:g},{cy:g} L{cx:g},{cy + DIA_H:g} L{cx - DIA_W:g},{cy:g} Z",
        stroke=WARN,
        width=1.5,
        fill="#fff4dc",
    )
    for index, line in enumerate(lines):
        offset = (index - (len(lines) - 1) / 2) * 13
        canvas.text(cx, cy + 4 + offset, line, size=10, anchor="middle")


def _label(canvas: Canvas, points: list[tuple[float, float]], text: str, how: str) -> None:
    (x1, y1), (x2, y2) = points[0], points[1]
    if how == "loop_under":  # подпись под горизонтальным участком петли
        (hx1, hy), (hx2, _y) = points[1], points[2]
        x, y = (hx1 + hx2) / 2 - len(text) * 2.9, hy + 15
    elif abs(x1 - x2) < 1:  # первый отрезок вертикальный — подпись справа от него
        x, y = x1 + 8, y1 + 15
    else:  # горизонтальный — над ним, у начала
        x = x1 + (8 if x2 > x1 else -8 - len(text) * 5.8)
        y = y1 - 6
    width = len(text) * 5.8 + 8
    canvas.rect(x - 4, y - 11, width, 15, fill=PAPER, stroke="none", rx=3)
    canvas.text(x, y, text, size=10, fill=MUTED, italic=True)


def draw(canvas: Canvas, art: Drawio) -> None:
    pos = positions()

    canvas.text(24, 38, TITLE, size=20, weight="bold")
    canvas.text(
        24,
        60,
        "Дорожки по участникам процесса, ветвления — ромбы, параллельные действия — fork и join",
        size=12,
        fill=MUTED,
    )

    # ---- дорожки -----------------------------------------------------------
    for index, name in enumerate(LANES):
        x = LANE_X + LANE_W * index
        canvas.rect(x, LANE_TOP, LANE_W, LANE_H, fill=LANE_A if index % 2 == 0 else LANE_B, stroke=LINE)
        canvas.rect(x, LANE_TOP, LANE_W, HEAD_H, fill=ACCENT, stroke=ACCENT)
        canvas.text(x + LANE_W / 2, LANE_TOP + 23, name, size=13, weight="bold", anchor="middle", fill=PAPER)

    # ---- переходы -----------------------------------------------------------
    for source, target, label, how in EDGES:
        points = route(pos, source, target, how)
        canvas.polyline(points, stroke=INK, width=1.3, arrow=True)
        if label:
            _label(canvas, points, label, how)

    # ---- узлы ---------------------------------------------------------------
    for key, _lane, _row, label, kind in STEPS:
        cx, cy = center(pos, key)
        if kind == "start":
            _terminal(canvas, cx, cy, final=False)
        elif kind == "end":
            _terminal(canvas, cx, cy, final=True)
        elif kind == "action":
            _action(canvas, cx, cy, label.split("\n"))
        elif kind == "decision":
            _decision(canvas, cx, cy, label.split("\n"))
        else:
            canvas.rect(cx - BAR_SPAN / 2, cy - BAR_H / 2, BAR_SPAN, BAR_H, fill=INK, stroke=INK, rx=2)
            canvas.text(cx + BAR_SPAN / 2 + 8, cy + 4, kind, size=10, fill=MUTED, italic=True)

    # ---- то же самое в редактируемом исходнике Draw.io --------------------
    for index, name in enumerate(LANES):
        art.shape(
            LANE_X + LANE_W * index, LANE_TOP, LANE_W, LANE_H, name,
            style="swimlane;startSize=34;html=1;fillColor=#2f6fb0;fontColor=#ffffff;"
                  "swimlaneFillColor=%s;strokeColor=#7d8891;fontStyle=1;" % (LANE_A if index % 2 == 0 else LANE_B),
        )
    ids: dict[str, int] = {}
    for key, _lane, _row, label, kind in STEPS:
        cx, cy = center(pos, key)
        value = label.replace("\n", "<br>")
        if kind == "start":
            ids[key] = art.shape(cx - 14, cy - 14, 28, 28, "", style="ellipse;html=1;fillColor=#1f2a33;strokeColor=#1f2a33;")
        elif kind == "end":
            ids[key] = art.shape(cx - 15, cy - 15, 30, 30, "", style="ellipse;shape=endState;html=1;fillColor=#1f2a33;strokeColor=#1f2a33;")
        elif kind == "action":
            ids[key] = art.shape(cx - BOX_W / 2, cy - BOX_H / 2, BOX_W, BOX_H, value,
                                 style="rounded=1;arcSize=25;whiteSpace=wrap;html=1;fillColor=#ffffff;strokeColor=#1f2a33;fontSize=11;")
        elif kind == "decision":
            ids[key] = art.shape(cx - DIA_W, cy - DIA_H, DIA_W * 2, DIA_H * 2, value,
                                 style="rhombus;whiteSpace=wrap;html=1;fillColor=#fff4dc;strokeColor=#c79a2e;fontSize=10;")
        else:
            ids[key] = art.shape(cx - BAR_SPAN / 2, cy - BAR_H / 2, BAR_SPAN, BAR_H, "",
                                 style="rounded=0;html=1;fillColor=#1f2a33;strokeColor=#1f2a33;")
    for source, target, label, _how in EDGES:
        art.connect(ids[source], ids[target],
                    "edgeStyle=orthogonalEdgeStyle;rounded=0;html=1;endArrow=block;strokeColor=#1f2a33;fontSize=10;",
                    label)

    # ---- легенда ------------------------------------------------------------
    legend_y = LANE_TOP + LANE_H + 24
    canvas.rect(24, legend_y, WIDTH - 48, 96, fill=PAPER, stroke=LINE, rx=6)
    canvas.text(44, legend_y + 26, "Обозначения", size=13, weight="bold")
    ly = legend_y + 22
    canvas.ellipse(180, ly, 9, 9, fill=INK, stroke=INK)
    canvas.text(196, ly + 4, "начало", size=11, fill=MUTED)
    canvas.ellipse(266, ly, 11, 11, fill=PAPER, stroke=INK, width=1.4)
    canvas.ellipse(266, ly, 6, 6, fill=INK, stroke=INK)
    canvas.text(284, ly + 4, "конец", size=11, fill=MUTED)
    canvas.rect(346, ly - 12, 46, 24, fill=LANE_B, stroke=INK, rx=8)
    canvas.text(402, ly + 4, "действие", size=11, fill=MUTED)
    canvas.path(f"M486,{ly:g} L516,{ly - 12:g} L546,{ly:g} L516,{ly + 12:g} Z", stroke=WARN, width=1.4, fill="#fff4dc")
    canvas.text(556, ly + 4, "ветвление (условие)", size=11, fill=MUTED)
    canvas.rect(700, ly - 3, 46, 6, fill=INK, stroke=INK)
    canvas.text(756, ly + 4, "fork / join — параллельные действия", size=11, fill=MUTED)
    canvas.text(44, legend_y + 56, "Правила, которые проверяет система: триггер dates_check — даты и причина неисправности при завершении;", size=11, fill=MUTED)
    canvas.text(44, legend_y + 76, "ограничение остатка — нехватка ЗИП останавливает списание; ошибки приходят клиенту как 400 или 409.", size=11, fill=MUTED)


def to_svg() -> str:
    canvas = Canvas(WIDTH, HEIGHT)
    draw(canvas, Drawio(WIDTH, HEIGHT))
    return canvas.to_svg(TITLE)


def to_drawio() -> str:
    art = Drawio(WIDTH, HEIGHT)
    draw(Canvas(WIDTH, HEIGHT), art)
    return art.to_xml("activity")


REQUIREMENTS = [
    "Начало и конец показаны отдельными терминалами.",
    "28 узлов процесса, из них 5 ветвлений с подписанными ветвями «да» / «нет».",
    "Параллельные действия показаны через fork и join: бригада выполняет работу, пока система меняет статусы заявки и оборудования.",
    "Четыре дорожки: мастер, система, бригада, руководитель.",
    "Отдельные ветви отказов: отказ по выведенному из эксплуатации оборудованию, занятая бригада, нехватка материала, неполные данные при завершении.",
    "Каждое действие подписано правилом, которое проверяет система: генерация номера, расчёт простоя, запись в журнал.",
    "Закрытие отделено от выполнения: после «Выполнена» мастер закрывает заявку, простой фиксируется.",
]