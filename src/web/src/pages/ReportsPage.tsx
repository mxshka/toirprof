/* ===========================================================================
   ТОиР-Проф — экран «Отчёты».

   Параметры (вид, период, цех) → PDF по временной ссылке (живёт 15 секунд)
   или Excel из тех же данных. Ниже — отчёт по цеху из процедуры
   generate_workshop_report: парк, статусы, динамика, простой, топ-5 материалов.
   =========================================================================== */

import { useState } from 'react'
import { dashboardApi, insightsApi, reportsApi, workshopsApi } from '../api/endpoints'
import type { AnalyticsReport, ReportKind, WorkshopReportRowDto } from '../api/types'
import { REPORT_KINDS } from '../api/types'
import { useToast } from '../components/ToastProvider'
import { Card, EmptyState, ErrorBanner, LoadingBar } from '../components/Ui'
import { exportXlsx, stamp } from '../hooks/useExport'
import type { ExportColumn } from '../hooks/useExport'
import { useAction, useLoad } from '../hooks/useLoad'
import { isoDaysAgo, periodLabel } from '../utils/format'

type Row = Record<string, string | number | null>

/** Строки и колонки Excel для каждого вида отчёта. */
function tableOf(kind: ReportKind, report: AnalyticsReport): { columns: ExportColumn<Row>[]; rows: Row[] } {
  const col = (title: string, key: string): ExportColumn<Row> => ({ title, value: (row) => row[key] ?? null })
  switch (kind) {
    case 'crews':
      return {
        columns: [col('Код', 'code'), col('Бригада', 'name'), col('Назначено', 'assigned'), col('Выполнено', 'completed'), col('В срок', 'inTime'), col('Рейтинг, %', 'rating')],
        rows: report.crews.map((c) => ({ ...c })),
      }
    case 'materials':
      return {
        columns: [col('Артикул', 'sku'), col('Материал', 'name'), col('Ед.', 'unit'), col('Количество', 'quantity'), col('Сумма, ₽', 'cost'), col('Заявок', 'ordersCount')],
        rows: report.materials.map((m) => ({ ...m })),
      }
    case 'plan':
      return {
        columns: [col('Статус', 'status'), col('Записей', 'count'), col('Доля, %', 'percent')],
        rows: report.plan.map((p) => ({ ...p })),
      }
    default:
      return {
        columns: [col('Инв. номер', 'inventoryNumber'), col('Оборудование', 'name'), col('Цех', 'workshopName'), col('Заявок', 'ordersCount'), col('Простой, ч', 'totalDowntimeHours'), col('Доля, %', 'sharePercent')],
        rows: report.downtime.map((d) => ({ ...d })),
      }
  }
}

export function ReportsPage() {
  const toast = useToast()
  const action = useAction()
  const [kind, setKind] = useState<ReportKind>('downtime')
  const [from, setFrom] = useState(() => isoDaysAgo(-30))
  const [to, setTo] = useState(() => isoDaysAgo(0))
  const [workshop, setWorkshop] = useState('')
  const [link, setLink] = useState<string | null>(null)

  const workshops = useLoad((signal) => workshopsApi.list(signal), [])
  const report = useLoad(
    (signal) => dashboardApi.analytics(from, to, workshop === '' ? undefined : workshop, signal),
    [from, to, workshop],
  )
  const workshopReport = useLoad<WorkshopReportRowDto[]>(
    (signal) => (workshop === '' ? Promise.resolve([]) : insightsApi.workshopReport(workshop, from, to, signal)),
    [from, to, workshop],
  )

  const title = REPORT_KINDS.find((item) => item.kind === kind)?.title ?? kind
  const table = report.data ? tableOf(kind, report.data) : null

  const makePdf = async () => {
    await action.run(async () => {
      try {
        const created = await reportsApi.createLink({ kind, from, to, workshopId: workshop === '' ? undefined : workshop })
        setLink(created.url)
        window.open(created.url, '_blank', 'noopener')
        toast.success(`PDF «${title}» сформирован; ссылка действует ${created.ttlSeconds} с`)
      } catch (cause) {
        toast.error(cause instanceof Error ? cause.message : 'Не удалось сформировать PDF')
      }
    })
  }

  const makeExcel = () => {
    if (!table || table.rows.length === 0) {
      toast.warning('За период нет данных для выгрузки')
      return
    }
    exportXlsx(stamp(`отчёт-${kind}`), table.columns, table.rows)
  }

  const makeWorkshopExcel = () => {
    const rows = workshopReport.data ?? []
    if (rows.length === 0) return
    exportXlsx(
      stamp('отчёт-по-цеху'),
      [
        { title: 'Раздел', value: (row: WorkshopReportRowDto) => row.section },
        { title: 'Показатель', value: (row: WorkshopReportRowDto) => row.indicator },
        { title: 'Значение', value: (row: WorkshopReportRowDto) => row.value },
      ],
      rows,
    )
  }

  return (
    <div className="grid">
      <div className="section-title no-print">
        <h1>Отчёты</h1>
        <span className="muted">{periodLabel(from, to)}</span>
      </div>

      <LoadingBar visible={report.loading || action.busy} />

      <Card title="Параметры отчёта">
        <div className="form-grid">
          <label className="field">
            Вид отчёта
            <select className="select" value={kind} onChange={(event) => setKind(event.target.value as ReportKind)}>
              {REPORT_KINDS.map((item) => (
                <option key={item.kind} value={item.kind}>
                  {item.title}
                </option>
              ))}
            </select>
          </label>
          <label className="field">
            Период с
            <input className="input" type="date" value={from} max={to} onChange={(event) => setFrom(event.target.value)} />
          </label>
          <label className="field">
            по
            <input className="input" type="date" value={to} min={from} onChange={(event) => setTo(event.target.value)} />
          </label>
          <label className="field">
            Цех
            <select className="select" value={workshop} onChange={(event) => setWorkshop(event.target.value)}>
              <option value="">Все цеха</option>
              {(workshops.data ?? []).map((item) => (
                <option key={item.id} value={item.id}>
                  {item.name}
                </option>
              ))}
            </select>
          </label>
        </div>
        <div className="btn-row">
          <button type="button" className="btn btn--primary btn--sm" disabled={action.busy} onClick={() => void makePdf()}>
            Сформировать PDF
          </button>
          <button type="button" className="btn btn--sm" disabled={report.loading} onClick={makeExcel}>
            Выгрузить в Excel
          </button>
          {link ? (
            <a className="muted" href={link} target="_blank" rel="noreferrer">
              открыть PDF ещё раз (ссылка живёт 15 с)
            </a>
          ) : null}
        </div>
      </Card>

      {report.error ? <ErrorBanner message={report.error} onRetry={report.reload} /> : null}

      <Card title={`Предпросмотр: ${title}`} hint={table ? `${table.rows.length} строк` : 'загружаю…'}>
        {table && table.rows.length > 0 ? (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  {table.columns.map((c) => (
                    <th key={c.title}>{c.title}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {table.rows.slice(0, 20).map((row, index) => (
                  <tr key={index}>
                    {table.columns.map((c) => (
                      <td key={c.title}>{c.value(row) ?? '—'}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <EmptyState caption="За выбранный период данных нет" />
        )}
      </Card>

      <Card
        title="Отчёт по цеху"
        hint="процедура generate_workshop_report: парк, статусы, динамика, простой, топ-5 материалов"
        actions={
          <button type="button" className="btn btn--ghost btn--sm" disabled={(workshopReport.data ?? []).length === 0} onClick={makeWorkshopExcel}>
            Excel
          </button>
        }
      >
        {workshop === '' ? (
          <EmptyState caption="Выберите цех в параметрах" />
        ) : workshopReport.error ? (
          <ErrorBanner message={workshopReport.error} onRetry={workshopReport.reload} />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Раздел</th>
                  <th>Показатель</th>
                  <th className="table__num">Значение</th>
                </tr>
              </thead>
              <tbody>
                {(workshopReport.data ?? []).map((row, index) => (
                  <tr key={index}>
                    <td className="muted">{row.section}</td>
                    <td>{row.indicator}</td>
                    <td className="table__num mono">{row.value}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
