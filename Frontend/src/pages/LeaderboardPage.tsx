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
      setFeedback(caughtError instanceof Error ? caughtError.message : 'Greska pri ucitavanju.')
    }
  }

  return (
    <SectionPanel
      title="Globalna rang lista"
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
            Osvezi
          </button>
          <button className="button button--ghost" onClick={() => void api.snapshotLeaderboard()}>
            Sacuvaj presek rang liste
          </button>
        </div>
      }
    >
      {feedback ? <p className="feedback feedback--error">{feedback}</p> : null}

      <div className="table-shell">
        <table className="data-table">
          <thead>
            <tr>
              <th>Pozicija</th>
              <th>Igrac</th>
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
