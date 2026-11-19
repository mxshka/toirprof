/* ===========================================================================
   ТОиР-Проф — календарь планового ТО.

   Сетка месяца 6×7 с навигацией. Цвет записи: красный — просрочено
   (дата прошла, ТО не выполнено), жёлтый — плановая дата в ближайшие
   три дня, зелёный — выполнено, фиолетовый — по записи создана заявка.
   =========================================================================== */

import { useMemo, useState } from 'react'
import { scheduleApi } from '../api/endpoints'
import type { MaintenanceScheduleDto } from '../api/types'
import { useLoad } from '../hooks/useLoad'

type Kind = 'bad' | 'soon' | 'done' | 'assigned' | 'plan'

const WEEK = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс']

function iso(date: Date): string {
  const y = date.getFullYear()
  const m = String(date.getMonth() + 1).padStart(2, '0')
  const d = String(date.getDate()).padStart(2, '0')
  return `${y}-${m}-${d}`
}

export function classify(row: MaintenanceScheduleDto, today: string, soonLimit: string): Kind {
  if (row.status === 'Выполнено') return 'done'
  if (row.status === 'Просрочено' || row.plannedDate < today) return 'bad'
  if (row.plannedDate <= soonLimit) return 'soon'
  return row.status === 'Назначено' ? 'assigned' : 'plan'
}

export function MaintenanceCalendar({
  selectedId,
  onSelect,
  reloadKey,
}: {
  selectedId: string | null
  onSelect: (row: MaintenanceScheduleDto) => void
  reloadKey: number
}) {
  const [month, setMonth] = useState(() => {
    const now = new Date()
    return new Date(now.getFullYear(), now.getMonth(), 1)
  })

  const gridStart = useMemo(() => {
    const shift = (month.getDay() + 6) % 7
    return new Date(month.getFullYear(), month.getMonth(), 1 - shift)
  }, [month])
  const days = useMemo(
    () => Array.from({ length: 42 }, (_, i) => new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + i)),
    [gridStart],
  )
  const from = iso(days[0])
  const to = iso(days[41])

  const load = useLoad((signal) => scheduleApi.list({ from, to }, signal), [from, to, reloadKey])

  const today = iso(new Date())
  const soon = new Date()
  soon.setDate(soon.getDate() + 3)
  const soonLimit = iso(soon)

  const byDate = useMemo(() => {
    const map = new Map<string, MaintenanceScheduleDto[]>()
    for (const row of load.data ?? []) {
      const list = map.get(row.plannedDate) ?? []
      list.push(row)
      map.set(row.plannedDate, list)
    }
    return map
  }, [load.data])

  const counts = useMemo(() => {
    const result = { bad: 0, soon: 0 }
    for (const row of load.data ?? []) {
      const kind = classify(row, today, soonLimit)
      if (kind === 'bad') result.bad++
      if (kind === 'soon') result.soon++
    }
    return result
  }, [load.data, today, soonLimit])

  const rawCaption = month.toLocaleDateString('ru-RU', { month: 'long', year: 'numeric' })
  const caption = rawCaption.charAt(0).toUpperCase() + rawCaption.slice(1)
  const shiftMonth = (delta: number) => setMonth(new Date(month.getFullYear(), month.getMonth() + delta, 1))

  return (
    <div className="calendar">
      <div className="calendar__nav">
        <button type="button" className="btn btn--sm" onClick={() => shiftMonth(-1)} aria-label="Предыдущий месяц">
          ‹
        </button>
        <strong className="calendar__caption">{caption}</strong>
        <button type="button" className="btn btn--sm" onClick={() => shiftMonth(1)} aria-label="Следующий месяц">
          ›
        </button>
        <button
          type="button"
          className="btn btn--ghost btn--sm"
          onClick={() => {
            const now = new Date()
            setMonth(new Date(now.getFullYear(), now.getMonth(), 1))
          }}
        >
          Сегодня
        </button>
        <span className="calendar__legend">
          <span className="cal-chip cal-chip--bad">просрочено: {counts.bad}</span>
          <span className="cal-chip cal-chip--soon">ближайшие 3 дня: {counts.soon}</span>
          <span className="cal-chip cal-chip--done">выполнено</span>
          <span className="cal-chip cal-chip--assigned">заявка создана</span>
        </span>
      </div>

      {load.error ? <div className="banner">{load.error}</div> : null}

      <div className="calendar__grid">
        {WEEK.map((day) => (
          <div key={day} className="calendar__weekday">
            {day}
          </div>
        ))}
        {days.map((day) => {
          const key = iso(day)
          const entries = byDate.get(key) ?? []
          const kinds = entries.map((row) => classify(row, today, soonLimit))
          const tone = kinds.includes('bad') ? ' calendar__day--bad' : kinds.includes('soon') ? ' calendar__day--soon' : ''
          const outside = day.getMonth() !== month.getMonth() ? ' calendar__day--outside' : ''
          const isToday = key === today ? ' calendar__day--today' : ''
          return (
            <div key={key} className={`calendar__day${tone}${outside}${isToday}`}>
              <span className="calendar__num">{day.getDate()}</span>
              {entries.map((row, index) => (
                <button
                  key={row.id}
                  type="button"
                  className={`cal-chip cal-chip--${kinds[index]}${row.id === selectedId ? ' cal-chip--selected' : ''}`}
                  title={`${row.equipmentName}\n${row.maintenanceTypeName}\nСтатус: ${row.status}`}
                  onClick={() => onSelect(row)}
                >
                  {row.inventoryNumber} · {row.maintenanceTypeName}
                </button>
              ))}
            </div>
          )
        })}
      </div>
    </div>
  )
}
