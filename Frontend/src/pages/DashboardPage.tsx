import { useEffect, useState } from 'react'
import { MetricCard } from '../components/MetricCard'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import { formatAppDate, formatAppDateTime, formatAppTime } from '../lib/datetime'
import type {
  InventoryItem,
  LeaderboardEntry,
  MatchHistoryItem,
  MatchMove,
  OnlineCount,
  PlayerProgress,
  Team,
  UserProfile,
} from '../types'

interface DashboardPageProps {
  user: UserProfile
}

export function DashboardPage({ user }: DashboardPageProps) {
  const [online, setOnline] = useState<OnlineCount | null>(null)
  const [leaderboard, setLeaderboard] = useState<LeaderboardEntry[]>([])
  const [inventory, setInventory] = useState<InventoryItem[]>([])
  const [progress, setProgress] = useState<PlayerProgress[]>([])
  const [matchHistory, setMatchHistory] = useState<MatchHistoryItem[]>([])
  const [selectedMatchId, setSelectedMatchId] = useState<string | null>(null)
  const [selectedMoves, setSelectedMoves] = useState<MatchMove[]>([])
  const [isLoadingMoves, setIsLoadingMoves] = useState(false)
  const [team, setTeam] = useState<Team | null>(null)

  useEffect(() => {
    void loadDashboard()
  }, [user.id, user.currentTeamId])

  async function loadDashboard() {
    const tasks = await Promise.allSettled([
      api.getOnlineCount(),
      api.getLeaderboard(5),
      api.getInventory(user.id),
      api.getPlayerProgress(user.id),
      api.getMatchHistory(user.id),
      user.currentTeamId ? api.getTeam(user.currentTeamId) : Promise.resolve(null),
    ])

    if (tasks[0].status === 'fulfilled') {
      setOnline(tasks[0].value)
    }

    if (tasks[1].status === 'fulfilled') {
      setLeaderboard(tasks[1].value)
    }

    if (tasks[2].status === 'fulfilled') {
      setInventory(tasks[2].value)
    }

    if (tasks[3].status === 'fulfilled') {
      setProgress(tasks[3].value)
    }

    if (tasks[4].status === 'fulfilled') {
      setMatchHistory(tasks[4].value)

      if (tasks[4].value.length > 0) {
        const firstMatchId = tasks[4].value[0].matchId
        setSelectedMatchId(firstMatchId)
        void loadMatchMoves(firstMatchId)
      } else {
        setSelectedMatchId(null)
        setSelectedMoves([])
      }
    }

    if (tasks[5].status === 'fulfilled') {
      setTeam(tasks[5].value)
    }
  }

  async function loadMatchMoves(matchId: string) {
    setIsLoadingMoves(true)

    try {
      const moves = await api.getMatchMoves(matchId)
      setSelectedMoves(moves)
    } finally {
      setIsLoadingMoves(false)
    }
  }

  async function handleSelectMatch(matchId: string) {
    setSelectedMatchId(matchId)
    await loadMatchMoves(matchId)
  }

  const latestProgress = progress[0]
  const thirtyDaysAgo = Date.now() - 30 * 24 * 60 * 60 * 1000
  const chartEntries = [...progress]
    .filter((entry) => new Date(entry.timestamp).getTime() >= thirtyDaysAgo)
    .reverse()
  const selectedMatch = selectedMatchId
    ? matchHistory.find((match) => match.matchId === selectedMatchId) ?? null
    : null

  return (
    <>
      <section className="hero-panel">
        <div className="hero-panel__content">
          <span className="hero-panel__badge">Pregled naloga</span>
          <h1>Kontrolna tabla</h1>
          <p>
            Na jednom mestu imas pregled aktivnosti, inventara, tima i poslednjih promena na
            nalogu.
          </p>
        </div>
      </section>

      <section className="metrics-grid">
        <MetricCard
          label="Tvoj ELO"
          value={String(user.eloRating)}
          tone="primary"
          hint={`${user.stats.wins} pobeda / ${user.stats.losses} poraza`}
        />
        <MetricCard
          label="Novcici"
          value={String(user.coins)}
          tone="accent"
          hint="Koristi ih za kupovinu skinova i drugih predmeta"
        />
        <MetricCard
          label="Online igraci"
          value={String(online?.onlinePlayers ?? 0)}
          tone="neutral"
          hint="Broj trenutno aktivnih igraca"
        />
        <MetricCard
          label="Inventar"
          value={String(inventory.length)}
          tone="primary"
          hint="Broj predmeta koje trenutno posedujes"
        />
      </section>

      <SectionPanel title="Top 5 igraca">
        <div className="list-stack">
          {leaderboard.map((entry) => (
            <article key={entry.userId} className="list-item">
              <div>
                <strong>
                  #{entry.rank} {entry.username}
                </strong>
                <span>
                  Pobede: {entry.wins} | Turniri: {entry.tournamentWins}
                </span>
              </div>
              <span className="list-item__accent">{entry.eloRating} ELO</span>
            </article>
          ))}
        </div>
      </SectionPanel>

      <SectionPanel title="Tvoj progres">
        {latestProgress && chartEntries.length > 0 ? (
          <div className="progress-panel">
            <ProgressChart entries={chartEntries} />
          </div>
        ) : (
          <p className="muted">Jos nema promena progresa u poslednjih 30 dana.</p>
        )}
      </SectionPanel>

      <SectionPanel title="Istorija meceva">
        {matchHistory.length > 0 ? (
          <div className="match-history-layout">
            <div className="match-history-list">
              {matchHistory.map((match) => (
                <button
                  key={match.matchId}
                  type="button"
                  className={`match-history-card${selectedMatchId === match.matchId ? ' match-history-card--active' : ''}`}
                  onClick={() => void handleSelectMatch(match.matchId)}
                >
                  <div className="match-history-card__top">
                    <strong>{match.opponentName}</strong>
                    <span className={`result-pill result-pill--${toResultTone(match.result)}`}>
                      {match.result}
                    </span>
                  </div>
                  <span>{match.isTournament ? match.tournamentName ?? 'Turnirski mec' : 'Direktan mec'}</span>
                  <span>{formatAppDateTime(match.playedAt)}</span>
                </button>
              ))}
            </div>

            <div className="match-replay-card">
              {selectedMatch ? (
                <>
                  <div className="match-replay-card__header">
                    <div>
                      <h3>{selectedMatch.opponentName}</h3>
                      <span>{selectedMatch.isTournament ? selectedMatch.tournamentName ?? 'Turnirski mec' : 'Direktan mec'}</span>
                    </div>
                    <div className="match-replay-card__meta">
                      <span>Igrao si kao: {selectedMatch.symbol}</span>
                      <span>{formatAppDateTime(selectedMatch.playedAt)}</span>
                    </div>
                  </div>

                  <div className="match-replay-card__body">
                    <ReplayBoard moves={selectedMoves} />

                    <div className="match-moves-panel">
                      <div className="match-moves-panel__header">
                        <strong>Odigrani potezi</strong>
                        <span className={`result-pill result-pill--${toResultTone(selectedMatch.result)}`}>
                          {selectedMatch.result}
                        </span>
                      </div>

                      {isLoadingMoves ? (
                        <p className="muted">Ucitavanje poteza...</p>
                      ) : selectedMoves.length > 0 ? (
                        <div className="match-moves-list">
                          {selectedMoves.map((move) => (
                            <article key={`${move.moveNumber}-${move.movedAt}`} className="match-move-item">
                              <strong>
                                {move.moveNumber}. {move.playerName}
                              </strong>
                              <span>{describePosition(move.position)}</span>
                              <span>Simbol {move.symbol} · {formatAppTime(move.movedAt)}</span>
                            </article>
                          ))}
                        </div>
                      ) : (
                        <p className="muted">Za ovaj mec nema sacuvanih poteza.</p>
                      )}
                    </div>
                  </div>
                </>
              ) : (
                <p className="muted">Izaberi mec da vidis njegov tok.</p>
              )}
            </div>
          </div>
        ) : (
          <p className="muted">Jos nema zavrsenih meceva za prikaz.</p>
        )}
      </SectionPanel>

      <SectionPanel title="Tim i inventar" eyebrow="Korisnicki pregled">
        <div className="dual-grid">
          <div className="nested-card">
            <h3>Aktivni tim</h3>
            {team ? (
              <>
                <strong>{team.name}</strong>
                <span>Team ELO: {team.teamElo}</span>
                <span>Clanova: {team.memberIds.length}</span>
              </>
            ) : (
              <p className="muted">Trenutno nisi clan nijednog tima.</p>
            )}
          </div>

          <div className="nested-card">
            <h3>Poslednje kupovine</h3>
            {inventory.length > 0 ? (
              inventory.slice(0, 4).map((item) => (
                <div key={`${item.itemId}-${item.purchasedAt}`} className="inventory-preview">
                  <strong>{item.itemName}</strong>
                  <span>{formatAppDateTime(item.purchasedAt)}</span>
                </div>
              ))
            ) : (
              <p className="muted">Inventar je za sada prazan.</p>
            )}
          </div>
        </div>
      </SectionPanel>
    </>
  )
}

interface ProgressChartProps {
  entries: PlayerProgress[]
}

function ProgressChart({ entries }: ProgressChartProps) {
  if (entries.length === 0) {
    return null
  }

  const width = 760
  const height = 260
  const paddingLeft = 56
  const paddingRight = 20
  const paddingTop = 20
  const paddingBottom = 42
  const plotWidth = width - paddingLeft - paddingRight
  const plotHeight = height - paddingTop - paddingBottom

  const minElo = Math.min(...entries.map((entry) => entry.elo))
  const maxElo = Math.max(...entries.map((entry) => entry.elo))
  const eloPadding = Math.max(20, Math.round((maxElo - minElo || 40) * 0.15))
  const chartMin = minElo - eloPadding
  const chartMax = maxElo + eloPadding
  const chartRange = Math.max(1, chartMax - chartMin)

  const points = entries.map((entry, index) => {
    const x =
      entries.length === 1
        ? paddingLeft + plotWidth / 2
        : paddingLeft + (index / (entries.length - 1)) * plotWidth
    const y = paddingTop + (1 - (entry.elo - chartMin) / chartRange) * plotHeight

    return {
      x,
      y,
      label: formatAppDate(entry.timestamp),
      elo: entry.elo,
    }
  })

  const path = points.map((point, index) => `${index === 0 ? 'M' : 'L'} ${point.x} ${point.y}`).join(' ')
  const fillPath = `${path} L ${points[points.length - 1].x} ${height - paddingBottom} L ${points[0].x} ${height - paddingBottom} Z`
  const yTicks = Array.from({ length: 4 }, (_, index) => chartMin + (chartRange / 3) * index).reverse()
  const uniqueDatePoints = Array.from(
    points.reduce((map, point) => {
      const existing = map.get(point.label)

      if (existing) {
        existing.lastX = point.x
        existing.x = (existing.firstX + existing.lastX) / 2
        return map
      }

      map.set(point.label, {
        ...point,
        firstX: point.x,
        lastX: point.x,
      })
      return map
    }, new Map<string, ((typeof points)[number] & { firstX: number; lastX: number })>()).values(),
  )
  const xLabels = uniqueDatePoints.filter((_, index) => {
    if (uniqueDatePoints.length <= 6) {
      return true
    }

    if (index === 0 || index === uniqueDatePoints.length - 1) {
      return true
    }

    const step = Math.max(1, Math.ceil((uniqueDatePoints.length - 1) / 5))
    return index % step === 0
  })

  return (
    <div className="progress-chart-card">
      <div className="progress-chart__header">
        <strong>ELO kroz vreme</strong>
        <span className="muted">Poslednjih 30 dana</span>
      </div>
      <svg viewBox={`0 0 ${width} ${height}`} className="progress-chart" role="img" aria-label="Grafik progresa ELO rejtinga kroz vreme">
        {yTicks.map((tick) => {
          const y = paddingTop + (1 - (tick - chartMin) / chartRange) * plotHeight
          return (
            <g key={tick}>
              <line
                x1={paddingLeft}
                y1={y}
                x2={width - paddingRight}
                y2={y}
                className="progress-chart__grid"
              />
              <text x={paddingLeft - 10} y={y + 4} textAnchor="end" className="progress-chart__axis">
                {Math.round(tick)}
              </text>
            </g>
          )
        })}

        <path d={fillPath} className="progress-chart__area" />
        <path d={path} className="progress-chart__line" />

        {points.map((point) => (
          <g key={`${point.label}-${point.elo}`}>
            <circle cx={point.x} cy={point.y} r={5} className="progress-chart__point" />
            <title>{`${point.label} - ELO ${point.elo}`}</title>
          </g>
        ))}

        {xLabels.map((point) => (
          <text
            key={`${point.label}-x`}
            x={point.x}
            y={height - 14}
            textAnchor={
              point.x <= paddingLeft + 8
                ? 'start'
                : point.x >= width - paddingRight - 8
                  ? 'end'
                  : 'middle'
            }
            className="progress-chart__axis"
          >
            {point.label}
          </text>
        ))}
      </svg>
    </div>
  )
}

function ReplayBoard({ moves }: { moves: MatchMove[] }) {
  const board = Array.from({ length: 9 }, () => '')

  for (const move of moves) {
    board[move.position] = move.symbol
  }

  return (
    <div className="replay-board-card">
      <div className="replay-board">
        {board.map((cell, index) => (
          <div key={index} className="replay-board__cell">
            {cell}
          </div>
        ))}
      </div>
    </div>
  )
}

function toResultTone(result: string) {
  if (result === 'Pobeda') {
    return 'win'
  }

  if (result === 'Poraz') {
    return 'loss'
  }

  return 'draw'
}

function describePosition(position: number) {
  const labels = [
    'Gore levo',
    'Gore sredina',
    'Gore desno',
    'Sredina levo',
    'Centar',
    'Sredina desno',
    'Dole levo',
    'Dole sredina',
    'Dole desno',
  ]

  return labels[position] ?? `Polje ${position + 1}`
}
