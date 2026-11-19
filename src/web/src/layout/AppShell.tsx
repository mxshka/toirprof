/* ===========================================================================
   ТОиР-Проф — оболочка приложения: боковое меню, верхняя панель,
   область содержимого. Меню и права ролей описаны здесь, а не размазаны
   по страницам: при добавлении раздела достаточно одной строки.
   =========================================================================== */

import { useEffect, useRef, useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { notificationsApi, searchApi } from '../api/endpoints'
import type { NotificationDto, RoleCode, SearchHitDto } from '../api/types'

interface NavEntry {
  to: string
  label: string
  glyph: string
  group: string
  roles: RoleCode[]
}

/**
 * Права по разделам. Администратор видит всё, руководитель — сводку и
 * справочники, мастер — только свою работу с оборудованием и заявками.
 * Сервер проверяет права повторно, но скрывать недоступное нужно и в меню:
 * иначе руководитель увидит кнопку, которая вернёт 403.
 */
const MENU: NavEntry[] = [
  { to: '/', label: 'Дашборд руководителя', glyph: 'Д', group: 'Сводка', roles: ['ADMIN', 'MANAGER', 'MASTER'] },
  { to: '/analytics', label: 'Аналитика', glyph: 'А', group: 'Сводка', roles: ['ADMIN', 'MANAGER'] },
  { to: '/reports', label: 'Отчёты', glyph: 'Р', group: 'Сводка', roles: ['ADMIN', 'MANAGER'] },
  { to: '/equipment', label: 'Оборудование', glyph: 'О', group: 'Учёт', roles: ['ADMIN', 'MANAGER', 'MASTER'] },
  { to: '/schedule', label: 'График ТО', glyph: 'Г', group: 'Учёт', roles: ['ADMIN', 'MANAGER', 'MASTER'] },
  { to: '/work-orders', label: 'Заявки', glyph: 'З', group: 'Учёт', roles: ['ADMIN', 'MANAGER', 'MASTER'] },
  { to: '/crews', label: 'Бригады', glyph: 'Б', group: 'Справочники', roles: ['ADMIN', 'MANAGER'] },
  { to: '/materials', label: 'Материалы и ЗИП', glyph: 'М', group: 'Справочники', roles: ['ADMIN', 'MANAGER'] },
  { to: '/users', label: 'Пользователи', glyph: 'П', group: 'Справочники', roles: ['ADMIN'] },
]

const ROLE_TITLE: Record<RoleCode, string> = {
  ADMIN: 'Администратор',
  MANAGER: 'Руководитель',
  MASTER: 'Мастер',
}

const COLLAPSED_KEY = 'toirprof.menu.collapsed'
const SEEN_KEY = 'toirprof.notifications.seen'

function readStorage(key: string): string | null {
  try {
    return localStorage.getItem(key)
  } catch {
    return null
  }
}

function writeStorage(key: string, value: string): void {
  try {
    localStorage.setItem(key, value)
  } catch {
    // хранилище недоступно — настройка живёт до перезагрузки
  }
}

/** Куда ведёт результат поиска. */
function hitPath(hit: SearchHitDto): string {
  if (hit.kind === 'order') return `/work-orders?open=${hit.id}`
  if (hit.kind === 'equipment') return `/equipment?search=${encodeURIComponent(hit.title.split(' · ')[0])}`
  return '/users'
}

const HIT_KIND: Record<SearchHitDto['kind'], string> = { order: 'Заявка', equipment: 'Оборудование', user: 'Сотрудник' }

/** Глобальный поиск в верхней панели: заявки, оборудование, сотрудники. */
function GlobalSearch() {
  const navigate = useNavigate()
  const [query, setQuery] = useState('')
  const [hits, setHits] = useState<SearchHitDto[]>([])
  const [open, setOpen] = useState(false)

  useEffect(() => {
    const text = query.trim()
    if (text.length < 2) {
      setHits([])
      return
    }
    const controller = new AbortController()
    const timer = window.setTimeout(() => {
      searchApi
        .search(text, controller.signal)
        .then((rows) => {
          setHits(rows)
          setOpen(true)
        })
        .catch(() => setHits([]))
    }, 250)
    return () => {
      window.clearTimeout(timer)
      controller.abort()
    }
  }, [query])

  const go = (hit: SearchHitDto) => {
    setOpen(false)
    setQuery('')
    navigate(hitPath(hit))
  }

  return (
    <div className="search">
      <input
        className="input search__input"
        type="search"
        value={query}
        placeholder="Поиск: заявка, инв. номер, сотрудник"
        aria-label="Глобальный поиск"
        onChange={(event) => setQuery(event.target.value)}
        onFocus={() => setOpen(hits.length > 0)}
        onBlur={() => window.setTimeout(() => setOpen(false), 150)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && hits.length > 0) go(hits[0])
          if (event.key === 'Escape') setOpen(false)
        }}
      />
      {open && query.trim().length >= 2 ? (
        <div className="search__popup">
          {hits.length === 0 ? <div className="search__empty">Ничего не найдено</div> : null}
          {hits.map((hit) => (
            <button key={`${hit.kind}-${hit.id}`} type="button" className="search__hit" onMouseDown={() => go(hit)}>
              <span className="search__kind">{HIT_KIND[hit.kind]}</span>
              <span className="search__title">{hit.title}</span>
              <span className="search__sub">{hit.subtitle}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}

/** Уведомления со счётчиком непрочитанных: опрос раз в 30 секунд. */
function NotificationsBell() {
  const navigate = useNavigate()
  const [items, setItems] = useState<NotificationDto[]>([])
  const [open, setOpen] = useState(false)
  const [seen, setSeen] = useState(() => readStorage(SEEN_KEY) ?? new Date(Date.now() - 86_400_000).toISOString())
  const panelRef = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    let active = true
    const load = () =>
      notificationsApi
        .list()
        .then((rows) => {
          if (active) setItems(rows)
        })
        .catch(() => undefined)
    void load()
    const timer = window.setInterval(() => void load(), 30_000)
    return () => {
      active = false
      window.clearInterval(timer)
    }
  }, [])

  const unread = items.filter((item) => item.createdAt > seen).length

  const toggle = () => {
    const next = !open
    setOpen(next)
    if (next && items.length > 0) {
      const newest = items.reduce((max, item) => (item.createdAt > max ? item.createdAt : max), seen)
      setSeen(newest)
      writeStorage(SEEN_KEY, newest)
    }
  }

  return (
    <div className="bell" ref={panelRef}>
      <button type="button" className="btn btn--sm bell__button" onClick={toggle} title="Уведомления">
        Уведомления
        {unread > 0 ? <span className="bell__count">{unread > 99 ? '99+' : unread}</span> : null}
      </button>
      {open ? (
        <div className="bell__panel">
          <div className="bell__head">
            <strong>Последние события</strong>
            <button type="button" className="btn btn--ghost btn--sm" onClick={() => setOpen(false)}>
              Закрыть
            </button>
          </div>
          {items.length === 0 ? <div className="search__empty">Событий нет</div> : null}
          {items.map((item) => (
            <button
              key={item.id}
              type="button"
              className="bell__item"
              onClick={() => {
                setOpen(false)
                if (item.kind === 'order' && item.targetId) navigate(`/work-orders?open=${item.targetId}`)
                else navigate('/equipment')
              }}
            >
              <span className="bell__title">{item.title}</span>
              <span className="bell__text">{item.text}</span>
              <span className="bell__time">{new Date(item.createdAt).toLocaleString('ru-RU')}</span>
            </button>
          ))}
        </div>
      ) : null}
    </div>
  )
}

export function AppShell({ connection }: { connection?: React.ReactNode }) {
  const { session, logout, can } = useAuth()
  const [collapsed, setCollapsed] = useState(() => readStorage(COLLAPSED_KEY) === '1')

  const toggleMenu = () => {
    setCollapsed((value) => {
      writeStorage(COLLAPSED_KEY, value ? '0' : '1')
      return !value
    })
  }

  const groups: string[] = []
  for (const entry of MENU) {
    if (!groups.includes(entry.group)) groups.push(entry.group)
  }

  return (
    <div className={collapsed ? 'shell shell--collapsed' : 'shell'}>
      <div className="brand">
        <button
          type="button"
          className="menu-toggle"
          onClick={toggleMenu}
          title={collapsed ? 'Развернуть меню' : 'Свернуть меню'}
          aria-label={collapsed ? 'Развернуть меню' : 'Свернуть меню'}
        >
          ☰
        </button>
        <span className="brand__mark">ТП</span>
        <span className="brand__title">
          ТОиР-Проф
          <span className="brand__sub">учёт обслуживания</span>
        </span>
      </div>

      <header className="topbar">
        <GlobalSearch />
        {connection}
        {session ? <NotificationsBell /> : null}
        {session ? (
          <>
            <span className="topbar__user">
              {session.user.fullName} · {ROLE_TITLE[session.role]}
            </span>
            <button type="button" className="btn btn--sm" onClick={logout}>
              Выйти
            </button>
          </>
        ) : null}
      </header>

      <nav className="sidebar">
        {groups.map((group) => (
          <div key={group}>
            <div className="sidebar__group">{group}</div>
            {MENU.filter((entry) => entry.group === group && can(...entry.roles)).map((entry) => (
              <NavLink
                key={entry.to}
                to={entry.to}
                end={entry.to === '/'}
                className={({ isActive }) => (isActive ? 'menu-item menu-item--active' : 'menu-item')}
                title={entry.label}
              >
                <span className="menu-item__glyph" aria-hidden="true">
                  {entry.glyph}
                </span>
                <span className="menu-item__label">{entry.label}</span>
              </NavLink>
            ))}
          </div>
        ))}
      </nav>

      <main className="main">
        <Outlet />
      </main>
    </div>
  )
}

export { MENU, ROLE_TITLE }
export type { NavEntry }
