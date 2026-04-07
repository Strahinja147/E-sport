import type { PropsWithChildren, ReactNode } from 'react'

interface SectionPanelProps extends PropsWithChildren {
  title: string
  eyebrow?: string
  actions?: ReactNode
}

export function SectionPanel({
  title,
  eyebrow,
  actions,
  children,
}: SectionPanelProps) {
  return (
    <section className="panel">
      <header className="panel__header">
        <div>
          {eyebrow ? <span className="panel__eyebrow">{eyebrow}</span> : null}
          <h2>{title}</h2>
        </div>
        {actions ? <div className="panel__actions">{actions}</div> : null}
      </header>
      {children}
    </section>
  )
}
