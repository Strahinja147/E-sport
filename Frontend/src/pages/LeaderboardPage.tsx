import { useEffect, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { LeaderboardEntry } from '../types'

export function LeaderboardPage() {
  const [count, setCount] = useState(10)
  const [entries, setEntries] = useState<LeaderboardEntry[]>([])
  const [feedback, setFeedback] = useState('')

  useEffect(() => {
    void loadLeaderboard()
  }, [])

  async function loadLeaderboard(nextCount = count) {
    try {
      const board = await api.getLeaderboard(nextCount)
      setEntries(board)
      setFeedback('')
    } catch (caughtError) {
      setFeedback(caughtError instanceof Error ? caughtError.message : 'Greška pri učitavanju.')
    }
  }

  return (
    <SectionPanel
      title="Globalni leaderboard"
      actions={
        <div className="toolbar-inline">
          <input
            type="number"
            min={3}
            max={50}
            value={count}
            onChange={(event) => setCount(Number(event.target.value))}
          />
          <button className="button" onClick={() => loadLeaderboard()}>
            Osveži
          </button>
          <button className="button button--ghost" onClick={() => void api.snapshotLeaderboard()}>
            Snapshootuj leaderboard
          </button>
        </div>
      }
    >
      {feedback ? <p className="feedback feedback--error">{feedback}</p> : null}

      <div className="table-shell">
        <table className="data-table">
          <thead>
            <tr>
              <th>Rank</th>
              <th>Igrač</th>
              <th>ELO</th>
              <th>Pobede</th>
              <th>Turnirske pobede</th>
            </tr>
          </thead>
          <tbody>
            {entries.map((entry) => (
              <tr key={entry.userId}>
                <td>#{entry.rank}</td>
                <td>{entry.username}</td>
                <td>{entry.eloRating}</td>
                <td>{entry.wins}</td>
                <td>{entry.tournamentWins}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </SectionPanel>
  )
}
