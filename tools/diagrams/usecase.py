"""Диаграмма вариантов использования системы ТОиР-Проф.

Актёры, граница системы, ассоциации и два стереотипа отношений —
include (обязательная часть) и extend (необязательное поведение).

Раскладка — классическая для UML: актёры стоят слева за границей системы,
у каждого актёра своя полоса, в ней столбиком его варианты. Справа от
основного варианта на той же строке стоит служебный вариант, который он
включает (include) или который его расширяет (extend). Поэтому каждая
стрелка include/extend — короткая горизонталь между соседями, и ни одна
линия не пересекает чужие варианты и подписи.
"""

from __future__ import annotations

from svgkit import ACCENT, ACCENT_SOFT, INK, LANE_A, LANE_B, LINE, MUTED, PAPER, Canvas, Drawio

TITLE = "Диаграмма вариантов использования — ТОиР-Проф"

WIDTH = 1320

ACTOR_X = 120.0
BOUNDARY_X = 250.0
BOUNDARY_W = 1040.0
BOUNDARY_Y = 96.0
MAIN_CX = 540.0
SERVICE_CX = 1020.0
RX = 122.0
RY = 26.0
ROW = 86.0
BAND_HEAD = 44.0
BAND_PAD = 14.0

# Полосы: (актёр, что делает, [(основной вариант, служебный или None)])
BANDS = [
    (
        "Мастер участка",
        "составляет заявки,\nведёт работы, списывает ЗИП",
        [
            ("uc_auth", "uc_rights"),
            ("uc_create", "uc_offline"),
            ("uc_view_schedule", "uc_notify"),
            ("uc_work", "uc_stock"),
        ],
    ),
    (
        "Руководитель цеха",
        "назначает бригады,\nсмотрит аналитику и отчёты",
        [
            ("uc_kpi", None),
            ("uc_assign", None),
            ("uc_analytics", "uc_search"),
            ("uc_report", "uc_export"),
        ],
    ),
    (
        "Администратор",
        "ведёт справочники,\nроли и журнал операций",
        [
            ("uc_schedule", "uc_next_date"),
            ("uc_users", None),
            ("uc_audit", None),
        ],
    ),
]

# (ключ, подпись, служебный?)
USE_CASES = [
    ("uc_auth", "Авторизация", False),
    ("uc_create", "Создать заявку", False),
    ("uc_view_schedule", "Смотреть график ТО", False),
    ("uc_work", "Выполнить работы", False),
    ("uc_kpi", "Дашборд", False),
    ("uc_assign", "Назначить бригаду", False),
    ("uc_analytics", "Смотреть аналитику", False),
    ("uc_report", "Сформировать отчёт", False),
    ("uc_schedule", "Вести график ТО", False),
    ("uc_users", "Пользователи и роли", False),
    ("uc_audit", "Журнал операций", False),
    ("uc_rights", "Проверить права роли", True),
    ("uc_offline", "Офлайн-очередь", True),
    ("uc_notify", "Уведомить о просрочке", True),
    ("uc_stock", "Проверить остаток ЗИП", True),
    ("uc_search", "Поиск и фильтры", True),
    ("uc_export", "Экспорт отчёта", True),
    ("uc_next_date", "Рассчитать дату ТО", True),
]

# include: базовый → включаемый; extend: расширяющий → базовый (UML-направление стрелки).
RELATIONS = [
    ("uc_auth", "uc_rights", "include"),
    ("uc_schedule", "uc_next_date", "include"),
    ("uc_work", "uc_stock", "include"),
    ("uc_report", "uc_analytics", "include"),
    ("uc_notify", "uc_view_schedule", "extend"),
    ("uc_export", "uc_report", "extend"),
    ("uc_search", "uc_analytics", "extend"),
    ("uc_offline", "uc_create", "extend"),
]

LABELS = {key: label for key, label, _ in USE_CASES}
SERVICE = {key for key, _, service in USE_CASES if service}


def layout() -> tuple[dict[str, tuple[float, float]], list[tuple[float, float]], float]:
    """Центры вариантов, границы полос (верх, низ) и низ границы системы."""
    pos: dict[str, tuple[float, float]] = {}
    bands: list[tuple[float, float]] = []
    y = BOUNDARY_Y + 62.0
    for _actor, _note, rows in BANDS:
        top = y
        for index, (main, service) in enumerate(rows):
            cy = top + BAND_HEAD + RY + index * ROW
            pos[main] = (MAIN_CX, cy)
            if service:
                pos[service] = (SERVICE_CX, cy)
        bottom = top + BAND_HEAD + (len(rows) - 1) * ROW + 2 * RY + BAND_PAD
        bands.append((top, bottom))
        y = bottom + 12.0
    return pos, bands, y + 6.0


def _height() -> int:
    _, _, boundary_bottom = layout()
    return int(boundary_bottom + 150)


def _stick_figure(canvas: Canvas, cx: float, y: float) -> None:
    canvas.ellipse(cx, y, 11, 11, fill=PAPER, stroke=INK, width=1.5)
    canvas.line(cx, y + 11, cx, y + 38, stroke=INK, width=1.5)
    canvas.line(cx - 18, y + 22, cx + 18, y + 22, stroke=INK, width=1.5)
    canvas.line(cx, y + 38, cx - 14, y + 60, stroke=INK, width=1.5)
    canvas.line(cx, y + 38, cx + 14, y + 60, stroke=INK, width=1.5)


def _tag(canvas: Canvas, x: float, y: float, text: str) -> None:
    width = len(text) * 6.0 + 10
    canvas.rect(x - width / 2, y - 10, width, 14, fill=PAPER, stroke="none", rx=2)
    canvas.text(x, y + 1, text, size=10, fill=MUTED, anchor="middle", italic=True)


def draw(canvas: Canvas, art: Drawio) -> None:
    pos, bands, boundary_bottom = layout()
    height = boundary_bottom - BOUNDARY_Y

    canvas.text(24, 38, TITLE, size=20, weight="bold")
    canvas.text(
        24,
        60,
        "Актёры, граница системы, ассоциации и отношения «include» / «extend»",
        size=12,
        fill=MUTED,
    )

    # ---- граница системы ---------------------------------------------------
    canvas.rect(BOUNDARY_X, BOUNDARY_Y, BOUNDARY_W, height, fill="#fbfcfd", stroke=ACCENT, width=1.6)
    canvas.text(BOUNDARY_X + 18, BOUNDARY_Y + 28, "ТОиР-Проф", size=17, weight="bold", fill=ACCENT)
    canvas.text(BOUNDARY_X + 130, BOUNDARY_Y + 28, "— граница системы", size=12, fill=MUTED)
    canvas.text(MAIN_CX, BOUNDARY_Y + 50, "основные варианты", size=11, fill=MUTED, anchor="middle")
    canvas.text(SERVICE_CX, BOUNDARY_Y + 50, "служебные варианты", size=11, fill=MUTED, anchor="middle")
    art.shape(
        BOUNDARY_X,
        BOUNDARY_Y,
        BOUNDARY_W,
        height,
        "ТОиР-Проф",
        style="rounded=0;whiteSpace=wrap;html=1;fillColor=none;strokeColor=#2f6fb0;verticalAlign=top;align=left;"
        "spacingLeft=12;fontStyle=1;fontColor=#2f6fb0;",
    )

    nodes: dict[str, int] = {}
    actors: list[int] = []

    for (actor, note, rows), (top, bottom) in zip(BANDS, bands):
        # Полоса актёра внутри границы: подпись и лёгкая подложка.
        canvas.rect(BOUNDARY_X + 14, top, BOUNDARY_W - 28, bottom - top, fill=ACCENT_SOFT, stroke="none", rx=8)
        canvas.text(BOUNDARY_X + 28, top + 24, actor, size=12, weight="bold", fill=ACCENT)

        # Актёр — слева за границей, по центру своей полосы.
        mid = (top + bottom) / 2
        figure_top = mid - 52
        _stick_figure(canvas, ACTOR_X, figure_top)
        canvas.text(ACTOR_X, figure_top + 80, actor, size=12, weight="bold", anchor="middle")
        for index, row in enumerate(note.split("\n")):
            canvas.text(ACTOR_X, figure_top + 96 + index * 13, row, size=10, fill=MUTED, anchor="middle")
        actors.append(
            art.shape(
                ACTOR_X - 15,
                figure_top - 11,
                30,
                72,
                actor,
                style="shape=umlActor;verticalLabelPosition=bottom;verticalAlign=top;html=1;outlineConnect=0;",
            )
        )

        # Ассоциации: от руки актёра к левому краю каждого основного варианта.
        hand = (ACTOR_X + 20, figure_top + 22)
        for main, _service in rows:
            mx, my = pos[main]
            canvas.line(hand[0], hand[1], mx - RX, my, stroke=INK, width=1.2)

    # ---- варианты использования -------------------------------------------
    for key, label, service in USE_CASES:
        cx, cy = pos[key]
        canvas.ellipse(cx, cy, RX, RY, fill=LANE_A if service else LANE_B, stroke=ACCENT, width=1.4)
        canvas.text(cx, cy + 4, label, size=12, anchor="middle")
        nodes[key] = art.shape(
            cx - RX,
            cy - RY,
            RX * 2,
            RY * 2,
            label,
            style="ellipse;whiteSpace=wrap;html=1;fillColor=%s;strokeColor=#2f6fb0;" % (LANE_A if service else LANE_B),
        )

    for (actor_node, (_a, _n, rows)) in zip(actors, BANDS):
        for main, _service in rows:
            art.connect(actor_node, nodes[main], "endArrow=none;html=1;strokeColor=#1f2429;")

    # ---- отношения include / extend ----------------------------------------
    for source, target, kind in RELATIONS:
        sx, sy = pos[source]
        tx, ty = pos[target]
        if sy == ty:
            # Соседи по строке: короткая горизонталь между краями эллипсов.
            x1 = sx + RX if sx < tx else sx - RX
            x2 = tx - RX if sx < tx else tx + RX
            canvas.polyline([(x1, sy), (x2 - (3 if sx < tx else -3), ty)], stroke=MUTED, width=1.3,
                            dash="7 5", arrow=True)
            _tag(canvas, (x1 + x2) / 2, sy - 8, f"«{kind}»")
        else:
            # Соседи по столбцу: короткая вертикаль между эллипсами.
            y1 = sy - RY if sy > ty else sy + RY
            y2 = ty + RY if sy > ty else ty - RY
            canvas.polyline([(sx, y1), (tx, y2 + (3 if sy > ty else -3))], stroke=MUTED, width=1.3,
                            dash="7 5", arrow=True)
            canvas.text(sx + 10, (y1 + y2) / 2 + 4, f"«{kind}»", size=10, fill=MUTED, italic=True)
        art.connect(
            nodes[source],
            nodes[target],
            "endArrow=open;html=1;dashed=1;strokeColor=#5b6670;fontColor=#5b6670;fontStyle=2;",
            f"«{kind}»",
        )

    # ---- легенда ------------------------------------------------------------
    top = boundary_bottom + 24
    canvas.rect(24, top, WIDTH - 48, 104, fill=PAPER, stroke=LINE, rx=6)
    canvas.text(40, top + 26, "Обозначения", size=13, weight="bold")

    canvas.line(40, top + 52, 84, top + 52, stroke=INK, width=1.2)
    canvas.text(94, top + 56, "ассоциация актёра с вариантом", size=11, fill=MUTED)

    canvas.polyline([(330, top + 52), (374, top + 52)], stroke=MUTED, dash="7 5", width=1.3, arrow=True)
    canvas.text(384, top + 56, "«include»: базовый вариант всегда выполняет включаемый", size=11, fill=MUTED)

    canvas.polyline([(764, top + 52), (720, top + 52)], stroke=MUTED, dash="7 5", width=1.3, arrow=True)
    canvas.text(774, top + 56, "«extend»: необязательное расширение базового", size=11, fill=MUTED)

    canvas.ellipse(62, top + 82, 22, 10, fill=LANE_B, stroke=ACCENT)
    canvas.text(94, top + 86, "основной вариант — его вызывает актёр", size=11, fill=MUTED)
    canvas.ellipse(352, top + 82, 22, 10, fill=LANE_A, stroke=ACCENT)
    canvas.text(384, top + 86, "служебный вариант — вызывается из основного", size=11, fill=MUTED)


def to_svg() -> str:
    canvas = Canvas(WIDTH, _height())
    draw(canvas, Drawio(WIDTH, canvas.height))
    return canvas.to_svg(TITLE)


def to_drawio() -> str:
    height = _height()
    art = Drawio(WIDTH, height)
    draw(Canvas(WIDTH, height), art)
    return art.to_xml("usecase")


REQUIREMENTS = [
    "Актёры: мастер участка, руководитель цеха, администратор — слева за границей системы.",
    "Граница системы: прямоугольник «ТОиР-Проф».",
    "18 вариантов использования: 11 основных и 7 служебных.",
    "Отношения «include» — 4: авторизация → проверка прав, график ТО → расчёт даты, выполнение работ → проверка остатка, отчёт → аналитика.",
    "Отношения «extend» — 4: уведомление о просрочке → просмотр графика, экспорт → отчёт, поиск и фильтры → аналитика, офлайн-очередь → создание заявки.",
    "Служебный вариант стоит на одной строке со своим базовым, поэтому стрелки короткие и не пересекают другие варианты.",
]
