interface MetricCardProps {
  label: string
  value: string
  tone?: 'primary' | 'accent' | 'neutral'
  hint?: string
}

export function MetricCard({
  label,
  value,
  tone = 'primary',
  hint,
}: MetricCardProps) {
  return (
    <article className={`metric-card metric-card--${tone}`}>
      <span className="metric-card__label">{label}</span>
      <strong className="metric-card__value">{value}</strong>
      {hint ? <span className="metric-card__hint">{hint}</span> : null}
    </article>
  )
}
