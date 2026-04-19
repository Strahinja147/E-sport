import { useEffect, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import { formatAppDateTime, formatAppTime, isSameAppDay } from '../lib/datetime'
import type { AuditLog, UserProfile } from '../types'

interface SecurityPageProps {
  user: UserProfile
}

interface DeviceSnapshot {
  id: string
  browser: string
  operatingSystem: string
  subtitle: string
  timeLabel: string
}

export function SecurityPage({ user }: SecurityPageProps) {
  const [entries, setEntries] = useState<DeviceSnapshot[]>([])
  const [status, setStatus] = useState('Pregledaj sa kojih uredjaja si se prijavio.')

  useEffect(() => {
    void loadAudit()
  }, [user.id])

  async function loadAudit() {
    try {
      const logs = await api.getAuditLogs(user.id)
      const recentLogs = [...logs]
        .sort((left, right) => new Date(right.time).getTime() - new Date(left.time).getTime())
        .slice(0, 10)

      setEntries(recentLogs.map(toDeviceSnapshot))
      setStatus('Prikazano je poslednjih 10 zabelezenih prijava za tvoj nalog.')
    } catch (caughtError) {
      setEntries([])
      setStatus(caughtError instanceof Error ? caughtError.message : 'Audit podaci nisu dostupni.')
    }
  }

  return (
    <>
      <SectionPanel title="Sa kog uredjaja si se prijavio">
        <p className="status-banner">{status}</p>

        <div className="security-grid">
          {entries.length > 0 ? (
            entries.map((entry) => (
              <article key={entry.id} className="security-card">
                <span className="security-card__eyebrow">{entry.subtitle}</span>
                <h3>{entry.browser}</h3>
                <span>{entry.operatingSystem}</span>
                <strong>{entry.timeLabel}</strong>
              </article>
            ))
          ) : (
            <p className="muted">Jos nema zabelezenih prijava za prikaz.</p>
          )}
        </div>
      </SectionPanel>
    </>
  )
}

function toDeviceSnapshot(entry: AuditLog): DeviceSnapshot {
  const normalizedDevice = entry.device.toLowerCase()
  const browser = inferBrowser(normalizedDevice)
  const operatingSystem = inferOperatingSystem(normalizedDevice)
  const subtitle = formatLocation(entry.ip)

  return {
    id: `${entry.time}-${entry.ip}-${entry.device}`,
    browser,
    operatingSystem,
    subtitle,
    timeLabel: formatAuditTime(entry.time),
  }
}

function inferBrowser(userAgent: string) {
  if (userAgent.includes('firefox')) {
    return 'Firefox'
  }

  if (userAgent.includes('edg')) {
    return 'Edge'
  }

  if (userAgent.includes('chrome')) {
    return 'Chrome'
  }

  if (userAgent.includes('safari')) {
    return 'Safari'
  }

  if (userAgent.includes('opr') || userAgent.includes('opera')) {
    return 'Opera'
  }

  return 'Nepoznat browser'
}

function inferOperatingSystem(userAgent: string) {
  if (userAgent.includes('windows')) {
    return 'Windows'
  }

  if (userAgent.includes('android')) {
    return 'Android'
  }

  if (userAgent.includes('iphone') || userAgent.includes('ipad') || userAgent.includes('ios')) {
    return 'iOS'
  }

  if (userAgent.includes('mac os') || userAgent.includes('macintosh')) {
    return 'macOS'
  }

  if (userAgent.includes('linux')) {
    return 'Linux'
  }

  return 'Nepoznat sistem'
}

function formatLocation(ip: string) {
  if (!ip || ip === '127.0.0.1' || ip === '::1') {
    return 'Lokalni uredjaj'
  }

  return `IP ${ip}`
}

function formatAuditTime(value: string) {
  const date = new Date(value)
  const now = new Date()
  const sameDay = isSameAppDay(date, now)

  if (sameDay) {
    return `Danas u ${formatAppTime(date)}`
  }

  return formatAppDateTime(date)
}
