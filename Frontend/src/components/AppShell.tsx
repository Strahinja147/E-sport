import { NavLink } from 'react-router-dom'
import type { PropsWithChildren } from 'react'
import type { SystemNotice, UserProfile } from '../types'

interface AppShellProps extends PropsWithChildren {
  user: UserProfile
  notices: SystemNotice[]
  onLogout: () => void
}

const navigation = [
  { to: '/', label: 'Pocetna' },
  { to: '/leaderboard', label: 'Rang lista' },
  { to: '/matchmaking', label: 'Uparivanje' },
  { to: '/shop', label: 'Prodavnica i inventar' },
  { to: '/tournament', label: 'Turniri' },
  { to: '/teams', label: 'Timovi' },
  { to: '/social', label: 'Drustvo' },
  { to: '/security', label: 'Bezbednost' },
]

export function AppShell({ user, notices, onLogout, children }: AppShellProps) {
  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="brand-card">
          <span className="brand-card__badge">Arena takmicenja</span>
          <h1>Pulse Arena</h1>
          <p>Pregled meceva, turnira, inventara, timova i aktivnosti igraca.</p>
        </div>

        <nav className="sidebar__nav">
          {navigation.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              className={({ isActive }) =>
                isActive ? 'sidebar__link sidebar__link--active' : 'sidebar__link'
              }
            >
              {item.label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar__user">
          <div>
            <span className="sidebar__eyebrow">Aktivni igrac</span>
            <strong>{user.username}</strong>
          </div>
          <div className="sidebar__user-meta">
            <span>ELO {user.eloRating}</span>
            <span>{user.coins} novcica</span>
          </div>
          <button className="button button--ghost" onClick={onLogout}>
            Odjava
          </button>
        </div>
      </aside>

      <div className="shell__content">
        <header className="topbar">
          <div>
            <span className="topbar__eyebrow">Operativni panel</span>
            <h2>Dobrodosao nazad, {user.username}</h2>
          </div>
        </header>

        {notices.length > 0 ? (
          <section className="notice-strip">
            {notices.map((notice) => (
              <article key={notice.id} className="notice-strip__item">
                <strong>{notice.title}</strong>
                <span>{notice.detail}</span>
              </article>
            ))}
          </section>
        ) : null}

        <main className="page-grid">{children}</main>
      </div>
    </div>
  )
}
