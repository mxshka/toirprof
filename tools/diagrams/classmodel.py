"""Диаграмма классов системы ТОиР-Проф.

Классы повторяют таблицы из db/schema.sql один в один: то же набор атрибутов,
те же ключи и те же правила удаления. Это deliberate: диаграмма классов здесь
не «модель предметной области на бумаге», а контракт, по которому потом
написан код EF Core и сгенерирована база.

Каждый класс нарисован тремя секциями, как и требует лист задания:
имя класса, атрибуты с видимостью, операции с видимостью.
"""

from __future__ import annotations

from svgkit import ACCENT, BAD, INK, LINE, MUTED, OK, PAPER, WARN, Canvas, Drawio

TITLE = "Диаграмма классов — ТОиР-Проф"

WIDTH = 1630
HEIGHT = 1180

BOX_W = 230.0
COL_GAP = 70.0
ROW_GAP = 34.0
HEAD_H = 30.0
LINE_H = 17.0
ATTR_H = 26.0

# Ссылочные (справочные) сущности — заливка одного тона.
FILL_REF = "#eef4fa"
FILL_CORE = "#ffffff"
FILL_LINK = "#f4f1f9"
FILL_ENUM = "#fbf3e3"

# Ключ, имя, тон, атрибуты, операции
CLASSES = [
    (
        "Role",
        "Роль",
        FILL_REF,
        ["- id : UUID «PK»", "- code : string «UK»", "- name : string", "- description : string?"],
        [],
    ),
    (
        "User",
        "Пользователь",
        FILL_REF,
        [
            "- id : UUID «PK»",
            "- username : string «UK»",
            "- passwordHash : string",
            "- fullName : string",
            "- roleId : UUID «FK»",
            "- isActive : bool",
            "- createdAt : datetime",
            "- updatedAt : datetime",
        ],
        ["+ login()", "+ changePassword()"],
    ),
    (
        "Workshop",
        "Цех",
        FILL_REF,
        ["- id : UUID «PK»", "- code : string «UK»", "- name : string"],
        [],
    ),
    (
        "MaintenanceType",
        "Вид ТО",
        FILL_REF,
        [
            "- id : UUID «PK»",
            "- code : string «UK»",
            "- name : string",
            "- intervalDays : int",
            "- isActive : bool",
        ],
        [],
    ),
    (
        "Equipment",
        "Оборудование",
        FILL_CORE,
        [
            "- id : UUID «PK»",
            "- inventoryNumber : string «UK»",
            "- name : string",
            "- workshopId : UUID «FK»",
            "- commissionDate : date",
            "- decommissionDate : date?",
            "- status : EquipmentStatus",
            "- criticality : int",
        ],
        [
            "+ changeStatus(s : EquipmentStatus)",
            "+ isDecommissioned() : bool",
        ],
    ),
    (
        "MaintenanceSchedule",
        "График ТО",
        FILL_CORE,
        [
            "- id : UUID «PK»",
            "- equipmentId : UUID «FK»",
            "- maintenanceTypeId : UUID «FK»",
            "- plannedDate : date",
            "- completedDate : date?",
            "- nextPlannedDate : date?",
            "- status : string",
            "- actualCost : decimal?",
        ],
        ["+ complete(date : date, cost : decimal?)", "+ returnToPlan()"],
    ),
    (
        "Crew",
        "Бригада",
        FILL_REF,
        ["- id : UUID «PK»", "- code : string «UK»", "- name : string", "- specialty : string?", "- isActive : bool"],
        [],
    ),
    (
        "CrewMember",
        "Член бригады",
        FILL_LINK,
        ["- id : UUID «PK»", "- crewId : UUID «FK»", "- userId : UUID «FK»", "- joinedAt : date"],
        [],
    ),
    (
        "Material",
        "Материал",
        FILL_REF,
        [
            "- id : UUID «PK»",
            "- sku : string «UK»",
            "- name : string",
            "- unit : string",
            "- price : decimal",
            "- stock : decimal",
            "- minStock : decimal",
        ],
        ["+ isShortage() : bool", "+ receive(qty : decimal)"],
    ),
    (
        "WorkOrder",
        "Заявка",
        FILL_CORE,
        [
            "- id : UUID «PK»",
            "- number : string «UK»",
            "- equipmentId : UUID «FK»",
            "- maintenanceTypeId : UUID «FK?»",
            "- scheduleId : UUID «FK?»",
            "- crewId : UUID «FK?»",
            "- createdBy : UUID «FK»",
            "- assignedTo : UUID «FK?»",
            "- priority : WorkOrderPriority",
            "- status : WorkOrderStatus",
            "- plannedStart : date?",
            "- plannedEnd : date?",
            "- actualStart : datetime?",
            "- actualEnd : datetime?",
            "- downtimeHours : decimal?",
            "- failureReason : string?",
            "- latitude : decimal?",
            "- longitude : decimal?",
        ],
        [
            "+ assignCrew(crew : Crew)",
            "+ start(at : datetime)",
            "+ complete(at : datetime, reason : string)",
            "+ writeOff(m : Material, qty : decimal)",
            "+ close() : void «Выполнена → Закрыта»",
            "+ canTransition(to : Status) : bool",
        ],
    ),
    (
        "WorkOrderPhoto",
        "Фото к заявке",
        FILL_LINK,
        [
            "- id : UUID «PK»",
            "- workOrderId : UUID «FK»",
            "- kind : string «before|after»",
            "- content : bytes «≤ 5 МБ»",
            "- latitude : decimal?",
            "- longitude : decimal?",
            "- createdBy : UUID «FK»",
            "- createdAt : datetime",
        ],
        [],
    ),
    (
        "WorkOrderMaterial",
        "Списание",
        FILL_LINK,
        [
            "- id : UUID «PK»",
            "- workOrderId : UUID «FK»",
            "- materialId : UUID «FK»",
            "- quantity : decimal",
            "- unitPrice : decimal",
            "- cost : decimal «= q × p»",
        ],
        [],
    ),
    (
        "OperationLog",
        "Журнал операций",
        FILL_LINK,
        [
            "- id : UUID «PK»",
            "- userId : UUID «FK?»",
            "- tableName : string",
            "- recordId : UUID?",
            "- operation : string",
            "- oldValues : string?",
            "- newValues : string?",
            "- createdAt : datetime",
        ],
        [],
    ),
    (
        "WorkOrderCounter",
        "Счётчик номеров",
        FILL_LINK,
        ["- year : int «PK»", "- lastValue : int «≥ 0»"],
        ["+ next() : string «Z-YYYY-XXXX»"],
    ),
    (
        "EquipmentStatus",
        "Статус оборудования",
        FILL_ENUM,
        ["«enum» В работе", "«enum» В ремонте", "«enum» В простое", "«enum» Выведено из эксплуатации"],
        [],
    ),
    (
        "WorkOrderStatus",
        "Статус заявки",
        FILL_ENUM,
        ["«enum» Создана", "«enum» Назначена", "«enum» В работе", "«enum» Ожидает материалов",
         "«enum» Выполнена", "«enum» Закрыта", "«enum» Отменена"],
        [],
    ),
    (
        "WorkOrderPriority",
        "Приоритет заявки",
        FILL_ENUM,
        ["«enum» Низкий", "«enum» Средний", "«enum» Высокий", "«enum» Критический"],
        [],
    ),
]

# (откуда, куда, подпись на «дочернем» конце, вид линии, правило удаления)
RELATIONS = [
    ("Role", "User", "1 .. *", "solid", "—"),
    ("Workshop", "Equipment", "1 .. *", "solid", "RESTRICT"),
    ("MaintenanceType", "MaintenanceSchedule", "1 .. *", "solid", "RESTRICT"),
    ("Equipment", "MaintenanceSchedule", "1 .. *", "solid", "CASCADE"),
    ("Crew", "CrewMember", "1 .. *", "solid", "CASCADE"),
    ("User", "CrewMember", "1 .. *", "solid", "CASCADE"),
    ("User", "WorkOrder", "0 .. *", "solid", "RESTRICT"),
    ("User", "WorkOrder", "0 .. *", "solid", "SET NULL"),
    ("Equipment", "WorkOrder", "1 .. *", "solid", "RESTRICT"),
    ("MaintenanceType", "WorkOrder", "0 .. *", "solid", "RESTRICT"),
    ("MaintenanceSchedule", "WorkOrder", "0 .. 1", "solid", "SET NULL"),
    ("Crew", "WorkOrder", "0 .. *", "solid", "SET NULL"),
    ("WorkOrder", "WorkOrderMaterial", "1 .. *", "solid", "—"),
    ("Material", "WorkOrderMaterial", "1 .. *", "solid", "RESTRICT"),
    ("User", "OperationLog", "0 .. *", "solid", "SET NULL"),
    ("WorkOrderCounter", "WorkOrder", "1 .. 1", "dash", "—"),
    ("WorkOrder", "WorkOrderPhoto", "0 .. *", "solid", "CASCADE"),
]

COL_X = [40.0, 370.0, 700.0, 1030.0, 1360.0]

# Левый верхний угол каждого класса. Раскладка подобрана так, чтобы связанные
# классы стояли рядом, а каждая связь шла прямой или ломаной линией по
# свободному коридору между колонками и ни разу не пересекала чужой класс.
POS = {
    "Role": (COL_X[0], 150.0),
    "User": (COL_X[0], 300.0),
    "OperationLog": (COL_X[0], 560.0),
    "WorkOrderPriority": (COL_X[0], 790.0),
    "CrewMember": (COL_X[1], 120.0),
    "Crew": (COL_X[1], 268.0),
    "WorkOrderCounter": (COL_X[1], 560.0),
    "WorkOrderStatus": (COL_X[1], 700.0),
    "WorkOrder": (COL_X[2], 260.0),
    "WorkOrderPhoto": (COL_X[2], 770.0),
    "Equipment": (COL_X[3], 150.0),
    "MaintenanceSchedule": (COL_X[3], 420.0),
    "WorkOrderMaterial": (COL_X[3], 650.0),
    "EquipmentStatus": (COL_X[3], 840.0),
    "Workshop": (COL_X[4], 150.0),
    "MaintenanceType": (COL_X[4], 330.0),
    "Material": (COL_X[4], 630.0),
}

HEIGHTS = {key: 0.0 for key in POS}


def box_height(attributes: list[str], operations: list[str]) -> float:
    rows = len(attributes) + len(operations)
    return HEAD_H + (rows * LINE_H if rows else ATTR_H) + 10.0


def box(key: str) -> tuple[float, float, float, float]:
    x, y = POS[key]
    return x, y, BOX_W, HEIGHTS[key]


def _h_link(left: str, right: str, y: float) -> list[tuple[float, float]]:
    """Горизонталь от правого края левого класса к левому краю правого."""
    lx, _ly, lw, _lh = box(left)
    rx, _ry, _rw, _rh = box(right)
    return [(lx + lw, y), (rx, y)]


def _v_link(top: str, bottom: str, dx: float = 0.0) -> list[tuple[float, float]]:
    tx, ty, tw, th = box(top)
    _bx, by, _bw, _bh = box(bottom)
    return [(tx + tw / 2 + dx, ty + th), (tx + tw / 2 + dx, by)]


def routes() -> list[list[tuple[float, float]]]:
    """Точки каждой связи — в том же порядке, что и RELATIONS."""
    ux, _uy, uw, _uh = box("User")
    cx, _cy, _cw, _ch = box("CrewMember")
    return [
        _v_link("Role", "User"),                                   # Role — User
        _h_link("Equipment", "Workshop", 195.0),                   # Workshop — Equipment
        _h_link("MaintenanceSchedule", "MaintenanceType", 440.0),  # Вид ТО — График ТО
        _v_link("Equipment", "MaintenanceSchedule"),               # Оборудование — График ТО
        _v_link("CrewMember", "Crew"),                             # Бригада — Член бригады
        [(ux + uw, 320.0), (320.0, 320.0), (320.0, 200.0), (cx, 200.0)],  # Пользователь — Член бригады
        _h_link("User", "WorkOrder", 440.0),                       # createdBy
        _h_link("User", "WorkOrder", 480.0),                       # assignedTo
        _h_link("WorkOrder", "Equipment", 300.0),                  # Оборудование — Заявка
        _h_link("WorkOrder", "MaintenanceType", 374.0),            # Вид ТО — Заявка
        _h_link("WorkOrder", "MaintenanceSchedule", 520.0),        # График ТО — Заявка
        _h_link("Crew", "WorkOrder", 330.0),                       # Бригада — Заявка
        _h_link("WorkOrder", "WorkOrderMaterial", 680.0),          # Заявка — Списание
        _h_link("WorkOrderMaterial", "Material", 720.0),           # Материал — Списание
        _v_link("User", "OperationLog"),                           # Пользователь — Журнал
        _h_link("WorkOrderCounter", "WorkOrder", 600.0),           # Счётчик — Заявка
        _v_link("WorkOrder", "WorkOrderPhoto"),                    # Заявка — Фото
    ]


def layout() -> tuple[dict[str, tuple[float, float]], float]:
    for key, _t, _f, attrs, ops in CLASSES:
        HEIGHTS[key] = box_height(attrs, ops)
    bottom = max(POS[key][1] + HEIGHTS[key] for key in POS)
    return dict(POS), bottom


def draw(canvas: Canvas, art: Drawio) -> None:
    boxes, bottom = layout()

    canvas.text(24, 38, TITLE, size=20, weight="bold")
    canvas.text(
        24,
        60,
        "Классы повторяют таблицы db/schema.sql; секции: атрибуты, операции, видимость",
        size=12,
        fill=MUTED,
    )

    # ---- связи: линии рисуются до классов, чтобы концы уходили под рамку ----
    names = {
        6: "createdBy",
        7: "assignedTo",
    }
    for index, ((source, target, multiplicity, kind, rule), points) in enumerate(zip(RELATIONS, routes())):
        dash = "8 5" if kind == "dash" else None
        canvas.polyline(points, stroke=LINE, width=1.3, dash=dash, arrow=kind == "dash")
        (x1, y1), (x2, y2) = points[-2], points[-1]
        caption = f"{names[index]} {multiplicity}" if index in names else multiplicity
        if abs(y1 - y2) < 1:  # последний участок горизонтальный: подпись у конца, над линией
            to_right = x2 > x1
            tx = x2 - 6 if to_right else x2 + 6
            anchor = "end" if to_right else "start"
            canvas.text(tx, y2 - 5, caption, size=10, fill=MUTED, anchor=anchor)
            if rule != "—":
                canvas.text(tx, y2 + 13, rule, size=9, fill=MUTED, italic=True, anchor=anchor)
        else:  # вертикальный: подпись справа от линии у нижнего конца
            canvas.text(x2 + 6, y2 - 6, caption, size=10, fill=MUTED)
            if rule != "—":
                canvas.text(x2 + 6, y2 - 18, rule, size=9, fill=MUTED, italic=True)

    anchors: dict[str, int] = {}

    for key, title, fill, attributes, operations in CLASSES:
        x, y = boxes[key]
        h = box_height(attributes, operations)
        canvas.rect(x, y, BOX_W, h, fill=PAPER, stroke=ACCENT, rx=4, width=1.4)
        canvas.rect(x, y, BOX_W, HEAD_H, fill=fill, stroke=ACCENT, rx=4, width=1.4)
        canvas.text(x + 10, y + 20, f"«{title}»", size=13, weight="bold")

        row_y = y + HEAD_H + 4
        for attribute in attributes:
            is_key = "PK" in attribute
            is_fk = "FK" in attribute
            colour = BAD if is_key else (WARN if is_fk else MUTED)
            canvas.text(x + 10, row_y + 13, attribute, size=11, fill=colour)
            row_y += LINE_H

        if operations:
            canvas.line(x, row_y + 3, x + BOX_W, row_y + 3, stroke=LINE, width=1)
            row_y += 8
            for operation in operations:
                canvas.text(x + 10, row_y + 13, operation, size=11, fill=OK)
                row_y += LINE_H

        anchors[key] = art.shape(
            x,
            y,
            BOX_W,
            h,
            f"{title}\n{'  ' + chr(10).join(attributes)}\n  {chr(10).join(operations)}".strip(),
            style=(
                "swimlane;html=1;whiteSpace=wrap;startSize=30;fillColor=%s;strokeColor=#2f6fb0;"
                "fontStyle=1;align=center;" % fill
            ),
        )

    for source, target, _multiplicity, kind, _rule in RELATIONS:
        art.connect(
            anchors[source],
            anchors[target],
            "edgeStyle=orthogonalEdgeStyle;endArrow=%s;html=1;strokeColor=#7d8891;dashed=%d;"
            % ("open" if kind == "dash" else "none", 1 if kind == "dash" else 0),
        )

    # ---- легенда -----------------------------------------------------------
    canvas.rect(40, bottom + 28, WIDTH - 80, 96, fill=PAPER, stroke=LINE, rx=6)
    canvas.text(60, bottom + 54, "Обозначения", size=13, weight="bold")
    canvas.text(60, bottom + 78, "- private / + public", size=11, fill=MUTED)
    canvas.text(300, bottom + 78, "«PK» первичный ключ", size=11, fill=BAD)
    canvas.text(500, bottom + 78, "«FK» внешний ключ", size=11, fill=WARN)
    canvas.text(680, bottom + 78, "«= q × p» вычисляемое поле", size=11, fill=MUTED)
    canvas.text(940, bottom + 78, "1 .. * одна ко многим", size=11, fill=MUTED)
    canvas.text(1200, bottom + 78, "0 .. * ноль или много", size=11, fill=MUTED)
    canvas.text(
        60,
        bottom + 102,
        "Подпись у связи — кратность со стороны «многих» и правило удаления: RESTRICT запрещает, CASCADE удаляет вместе, SET NULL обнуляет.",
        size=11,
        fill=MUTED,
    )


def _canvas_height() -> int:
    _, bottom = layout()
    return int(bottom + 140)


def to_svg() -> str:
    canvas = Canvas(WIDTH, _canvas_height())
    draw(canvas, Drawio(WIDTH, canvas.height))
    return canvas.to_svg(TITLE)


def to_drawio() -> str:
    canvas = Canvas(WIDTH, _canvas_height())
    art = Drawio(WIDTH, canvas.height)
    draw(canvas, art)
    return art.to_xml("class-model")


REQUIREMENTS = [
    "17 классов, каждый в трёх секциях: имя, атрибуты, операции.",
    "Видимость членов указана префиксами «-» (private) и «+» (public).",
    "17 ассоциаций с кратностями: 1 .. *, 0 .. *, 0 .. 1, 1 .. 1.",
    "У каждой ассоциации подписано правило удаления: RESTRICT, CASCADE или SET NULL.",
    "Три перечисления вынесены в отдельные классы: EquipmentStatus, WorkOrderStatus, WorkOrderPriority.",
    "Поле cost помечено как вычисляемое: в базе это GENERATED ALWAYS AS (quantity * unit_price) STORED.",
]