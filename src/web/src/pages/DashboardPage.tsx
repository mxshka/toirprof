/* ===========================================================================
   ТОиР-Проф — главный дашборд руководителя.

   Из листа задания: шесть KPI-карточек, круговая, столбчатая и линейная
   диаграммы, топ-5 единиц по простою и таблица последних заявок.

   Сводка приходит потоком событий (SSE), а если он недоступен — резервным
   опросом раз в минуту; см. useDashboardStream. Топ-5 простоев в сводке
   нет: он считается в аналитике, поэтому эта страница дополнительно
   запрашивает /api/analytics. Повод для второго запроса — требование
   листа: дашборд показывает и аналитический срез.
   =========================================================================== */

import { useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { dashboardApi, insightsApi, workOrdersApi } from '../api/endpoints'
import type { DashboardSummary, KpiSummaryDto, ViolationDto, WorkOrderShortDto } from '../api/types'
import { DataTable } from '../components/DataTable'
import { Badge, Card, ConnectionBadge, EmptyState, ErrorBanner, Loading, LoadingBar, MetricCard } from '../components/Ui'
import { MonthlyLine, PlanDonut, StatusPie, TopDowntimeBar, WorkshopBar } from '../components/charts/Charts'
import { useConnection } from '../hooks/connection'
import { useDashboardStream } from '../hooks/useDashboardStream'
import { useLoad } from '../hooks/useLoad'
import { formatDate, formatHours, formatMoney, formatPercent, isoDaysAgo, periodLabel } from '../utils/format'

/** Шесть карточек из листа задания. Порядок повторяет отчёт руководителю. */
function buildCards(summary: DashboardSummary) {
  const workshops = summary.byWorkshop.map((item) => `${item.name}: ${item.total}`).join(' · ')

  return [
    {
      title: 'Заявок за период',
      value: String(summary.totalOrders),
      hint: `завершено ${summary.completed} · в работе ${summary.inProgress}`,
      tone: 'info' as const,
    },
    {
      title: 'Средний простой',
      value: formatHours(summary.averageDowntimeHours),
      hint: 'на одну завершённую заявку',
      tone: 'wait' as const,
    },
    {
      title: 'Доля аварийных',
      value: formatPercent(summary.emergencyShare),
      hint: summary.emergencyShare > 15 ? 'выше нормы — разберитесь с причинами' : 'в пределах нормы',
      tone: summary.emergencyShare > 15 ? ('bad' as const) : ('ok' as const),
    },
    {
      title: 'Выполнение плана ТО',
      value: formatPercent(summary.maintenancePlanShare),
      hint: 'доля выполненных плановых работ',
      tone: summary.maintenancePlanShare >= 80 ? ('ok' as const) : ('wait' as const),
    },
    {
      title: 'Списано материалов',
      value: formatMoney(summary.totalMaterialCost),
      hint: 'по всем завершённым работам',
      tone: 'info' as const,
    },
    {
      title: 'Цехов в работе',
      value: String(summary.byWorkshop.length),
      hint: workshops || 'нет данных',
      tone: 'info' as const,
    },
  ]
}

/** Подпись тренда к прошлому периоду той же длины: ▲ рост, ▼ снижение. */
function trend(value: number | null | undefined): string {
  if (value == null) return 'нет данных за прошлый период'
  if (value === 0) return 'без изменений к прошлому периоду'
  return `${value > 0 ? '▲' : '▼'} ${Math.abs(value).toFixed(1)}% к прошлому периоду`
}

/** KPI предприятия с трендами (GET /api/analytics/dashboard-summary). */
function buildKpi(kpi: KpiSummaryDto) {
  return [
    {
      title: 'Оборудование',
      value: String(kpi.equipmentTotal),
      hint: `в ремонте ${kpi.equipmentInRepair} · в простое ${kpi.equipmentIdle}`,
      tone: kpi.equipmentInRepair > 0 ? ('wait' as const) : ('ok' as const),
      to: '/equipment',
    },
    {
      title: 'Активные заявки',
      value: String(kpi.activeOrders),
      hint: kpi.overdueOrders > 0 ? `просрочено ${kpi.overdueOrders}` : 'просрочек нет',
      tone: kpi.overdueOrders > 0 ? ('bad' as const) : ('info' as const),
      to: '/work-orders',
    },
    {
      title: 'Создано за период',
      value: String(kpi.createdOrders),
      hint: trend(kpi.trend.created),
      tone: 'info' as const,
      to: '/work-orders',
    },
    {
      title: 'Простой за период',
      value: formatHours(kpi.downtimeHours),
      hint: trend(kpi.trend.downtimeHours),
      tone: (kpi.trend.downtimeHours ?? 0) > 0 ? ('bad' as const) : ('ok' as const),
      to: '/analytics',
    },
    {
      title: 'Среднее время выполнения',
      value: formatHours(kpi.averageCompletionHours),
      hint: trend(kpi.trend.averageCompletionHours),
      tone: 'info' as const,
      to: '/work-orders?status=Выполнена',
    },
    {
      title: 'Выполнение плана ТО',
      value: formatPercent(kpi.maintenancePlanShare),
      hint: trend(kpi.trend.maintenancePlanShare),
      tone: kpi.maintenancePlanShare >= 80 ? ('ok' as const) : ('wait' as const),
      to: '/schedule',
    },
  ]
}

const VIOLATION_TITLE: Record<ViolationDto['kind'], string> = {
  order: 'Заявка',
  maintenance: 'ТО',
  material: 'Склад',
}

const recentColumns = [
  {
    key: 'number',
    title: 'Номер',
    render: (row: WorkOrderShortDto) => <span className="mono nowrap">{row.number}</span>,
  },
  { key: 'equipment', title: 'Оборудование', render: (row: WorkOrderShortDto) => row.equipmentName },
  {
    key: 'inventory',
    title: 'Инв. номер',
    render: (row: WorkOrderShortDto) => <span className="mono">{row.inventoryNumber}</span>,
  },
  { key: 'priority', title: 'Приоритет', render: (row: WorkOrderShortDto) => <Badge text={row.priority} /> },
  { key: 'status', title: 'Статус', render: (row: WorkOrderShortDto) => <Badge text={row.status} /> },
  {
    key: 'downtime',
    title: 'Простой, ч',
    numeric: true,
    render: (row: WorkOrderShortDto) => (row.downtimeHours == null ? '—' : formatHours(row.downtimeHours)),
  },
  { key: 'crew', title: 'Бригада', render: (row: WorkOrderShortDto) => row.crewName ?? '—' },
]

export function DashboardPage() {
  const [from, setFrom] = useState(() => isoDaysAgo(-30))
  const [to, setTo] = useState(() => isoDaysAgo(0))
  const [retry, setRetry] = useState(0)

  const navigate = useNavigate()
  const { setConnection } = useConnection()
  const kpi = useLoad((signal) => insightsApi.summary(from, to, undefined, signal), [from, to, retry])
  const violations = useLoad((signal) => insightsApi.violations(signal), [retry])

  /** Переход из нарушения к объекту: заявка, график ТО или склад. */
  const openViolation = (item: ViolationDto) => {
    if (item.kind === 'order') navigate(`/work-orders?open=${item.targetId}`)
    else if (item.kind === 'maintenance') navigate('/schedule?status=Просрочено')
    else navigate('/materials?shortage=1')
  }
  const stream = useDashboardStream(from, to, retry)

  const recent = useLoad((signal) => workOrdersApi.recent(8, signal), [])
  const analytics = useLoad((signal) => dashboardApi.analytics(from, to, undefined, signal), [from, to])

  // Индикатор в верхней панели должен видеть статус потока с любой страницы,
  // а поток живёт только здесь — поэтому статус дублируется в контекст.
  useEffect(() => {
    setConnection(stream.status, stream.caption)
  }, [setConnection, stream.status, stream.caption])

  const summary = stream.data
  const cards = useMemo(() => (summary ? buildCards(summary) : []), [summary])
  const downtime = analytics.data?.downtime ?? []
  const plan = analytics.data?.plan ?? []

  const reload = () => {
    setRetry((value) => value + 1)
    analytics.reload()
    recent.reload()
    kpi.reload()
    violations.reload()
  }

  return (
    <div className="grid">
      <div className="section-title no-print">
        <h1>Дашборд руководителя</h1>
        <label className="check">
          с
          <input
            className="input"
            type="date"
            style={{ width: 140 }}
            value={from}
            max={to}
            onChange={(event) => setFrom(event.target.value)}
          />
        </label>
        <label className="check">
          по
          <input
            className="input"
            type="date"
            style={{ width: 140 }}
            value={to}
            min={from}
            onChange={(event) => setTo(event.target.value)}
          />
        </label>
        <button type="button" className="btn btn--sm" onClick={() => setFrom(isoDaysAgo(-30))}>
          30 дней
        </button>
        <button type="button" className="btn btn--sm" onClick={() => setFrom(isoDaysAgo(-90))}>
          90 дней
        </button>
        <ConnectionBadge status={stream.status} caption={stream.caption} />
        <span className="muted">
          {periodLabel(from, to)}
          {stream.lastUpdate ? ` · обновлено в ${stream.lastUpdate.toLocaleTimeString('ru-RU')}` : ''}
        </span>
      </div>

      <LoadingBar visible={!summary && stream.status !== 'offline'} />

      {stream.status === 'offline' ? (
        <ErrorBanner message="Нет связи с сервером. Показаны последние загруженные данные." onRetry={reload} />
      ) : null}

      {summary ? (
        <>
          {kpi.data ? (
            <div className="kpi-row">
              {buildKpi(kpi.data).map((card) => (
                <button
                  key={card.title}
                  type="button"
                  className="kpi-link"
                  title="Открыть подробности"
                  onClick={() => navigate(card.to)}
                >
                  <MetricCard title={card.title} value={card.value} hint={card.hint} tone={card.tone} />
                </button>
              ))}
            </div>
          ) : null}

          <Card
            title="Нарушения и просрочки"
            hint={violations.data ? `${violations.data.length} шт. · щелчок открывает объект` : 'загружаю…'}
          >
            {violations.error ? <ErrorBanner message={violations.error} onRetry={violations.reload} /> : null}
            {violations.data && violations.data.length === 0 ? <EmptyState caption="Нарушений нет" /> : null}
            <div className="violations">
              {(violations.data ?? []).slice(0, 12).map((item) => (
                <button
                  key={`${item.kind}-${item.targetId}-${item.date}`}
                  type="button"
                  className={`violation violation--${item.kind}`}
                  onClick={() => openViolation(item)}
                >
                  <span className="violation__kind">{VIOLATION_TITLE[item.kind]}</span>
                  <span className="violation__title">{item.title}</span>
                  <span className="violation__detail">{item.detail}</span>
                  <span className="violation__late">
                    {item.daysLate > 0 ? `+${item.daysLate} дн.` : formatDate(item.date)}
                  </span>
                </button>
              ))}
            </div>
          </Card>

          <div className="kpi-row">
            {cards.map((card) => (
              <MetricCard key={card.title} title={card.title} value={card.value} hint={card.hint} tone={card.tone} />
            ))}
          </div>

          <div className="grid grid--2">
            <Card title="Статусы заявок" hint="щелчок по сектору — список заявок">
              <StatusPie
                data={summary.byStatus}
                onSelect={(status) => navigate(`/work-orders?status=${encodeURIComponent(status)}`)}
              />
            </Card>
            <Card title="Заявки по цехам" hint="щелчок по столбцу — заявки цеха">
              <WorkshopBar data={summary.byWorkshop} onSelect={(id) => navigate(`/work-orders?workshopId=${id}`)} />
            </Card>
          </div>

          <div className="grid grid--2">
            <Card title="Динамика по месяцам" hint="линейная диаграмма">
              <MonthlyLine data={summary.byMonth} />
            </Card>
            <Card title="Топ-5 единиц по простою" hint="щелчок — карточка оборудования">
              {analytics.error ? <ErrorBanner message={analytics.error} onRetry={analytics.reload} /> : null}
              <TopDowntimeBar
                data={downtime}
                onSelect={(row) => navigate(`/equipment?search=${encodeURIComponent(row.inventoryNumber)}`)}
              />
            </Card>
          </div>

          <div className="grid grid--2">
            <Card title="Выполнение графика ТО" hint="доли по статусам">
              <PlanDonut data={plan} onSelect={(status) => navigate(`/schedule?status=${encodeURIComponent(status)}`)} />
            </Card>
            <Card title="Простой по цехам" hint="часы за период">
              <WorkshopBar
                data={summary.byWorkshop}
                metric="downtime"
                onSelect={(id) => navigate(`/work-orders?workshopId=${id}`)}
              />
            </Card>
          </div>

          <Card title="Последние заявки" hint="сортировка по дате создания">
            {recent.error ? <ErrorBanner message={recent.error} onRetry={recent.reload} /> : null}
            <DataTable
              columns={recentColumns}
              rows={recent.data?.items ?? []}
              rowKey={(row) => row.id}
              loading={recent.loading}
              empty="Заявок пока нет"
            />
          </Card>
        </>
      ) : stream.status === 'offline' ? (
        <EmptyState caption="Сводка недоступна: нет связи с сервером" />
      ) : (
        <Loading caption="Загружаю сводку…" />
      )}
    </div>
  )
}
