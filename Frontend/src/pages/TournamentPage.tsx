import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { TournamentDetails, TournamentSummary, UserProfile } from '../types'

interface TournamentPageProps {
  user: UserProfile
}

export function TournamentPage({ user }: TournamentPageProps) {
  const [tournaments, setTournaments] = useState<TournamentSummary[]>([])
  const [selectedTournamentId, setSelectedTournamentId] = useState('')
  const [details, setDetails] = useState<TournamentDetails | null>(null)
  const [status, setStatus] = useState('Ucitavanje turnira...')
  const [activeMatchId, setActiveMatchId] = useState<string | null>(null)

  useEffect(() => {
    void loadTournaments()
  }, [])

  useEffect(() => {
    void loadActiveMatch()
  }, [user.id])

  useEffect(() => {
    if (!selectedTournamentId) {
      setDetails(null)
      return
    }

    void loadTournamentDetails(selectedTournamentId)
  }, [selectedTournamentId])

  async function loadTournaments() {
    try {
      const loadedTournaments = await api.getTournaments()
      const activeTournaments = loadedTournaments.filter(
        (tournament) => tournament.status !== 'Completed',
      )

      setTournaments(activeTournaments)

      if (activeTournaments.length > 0) {
        setSelectedTournamentId((current) => current || activeTournaments[0].id)
        setStatus('Prikazan je pregled aktivnih turnira.')
      } else {
        setSelectedTournamentId('')
        setStatus('Trenutno nema aktivnih turnira.')
      }
    } catch (caughtError) {
      setTournaments([])
      setSelectedTournamentId('')
      setStatus(caughtError instanceof Error ? caughtError.message : 'Turniri nisu dostupni.')
    }
  }

  async function loadTournamentDetails(tournamentId: string) {
    try {
      const result = await api.getTournamentDetails(tournamentId)
      setDetails(result)
    } catch {
      setDetails(null)
    }
  }

  async function loadActiveMatch() {
    try {
      const result = await api.checkMatchmaking(user.id)
      setActiveMatchId('matchId' in result ? result.matchId : null)
    } catch {
      setActiveMatchId(null)
    }
  }

  const playerStatus = useMemo(() => {
    if (!details) {
      return 'Izaberi turnir za pregled.'
    }

    const matches = details.rounds.flatMap((round) =>
      round.matches.map((match) => ({ roundNumber: round.roundNumber, match })),
    )

    const myMatches = matches.filter(
      ({ match }) => match.player1?.id === user.id || match.player2?.id === user.id,
    )

    if (myMatches.length === 0) {
      return 'Nisi prijavljen na ovaj turnir.'
    }

    const latestMatch = myMatches.sort((left, right) => right.roundNumber - left.roundNumber)[0]

    if (latestMatch.match.status === 'Finished') {
      return latestMatch.match.winner?.id === user.id
        ? 'Prosao si dalje u narednu rundu.'
        : 'Ispao si iz turnira.'
    }

    if (latestMatch.match.status === 'InProgress') {
      return `Tvoj mec u rundi ${latestMatch.roundNumber} je u toku.`
    }

    return `Cekas mec u rundi ${latestMatch.roundNumber}.`
  }, [details, user.id])

  return (
    <>
      <SectionPanel title="Aktivni turniri">
        <p className="status-banner">{status}</p>

        <div className="cards-grid">
          {tournaments.length > 0 ? (
            tournaments.map((tournament) => (
              <article key={tournament.id} className="selectable-card">
                <input
                  type="radio"
                  name="active-tournament"
                  checked={selectedTournamentId === tournament.id}
                  onChange={() => setSelectedTournamentId(tournament.id)}
                />
                <div>
                  <strong>{tournament.name}</strong>
                  <span>Status: {tournament.status}</span>
                  <span>Broj rundi: {tournament.rounds.length}</span>
                </div>
              </article>
            ))
          ) : (
            <p className="muted">Nema aktivnih turnira za prikaz.</p>
          )}
        </div>
      </SectionPanel>

      <SectionPanel title="Tvoj status u turniru">
        <div className="nested-card">
          <h3>{details?.name ?? 'Turnir nije izabran'}</h3>
          <span>{playerStatus}</span>
        </div>
      </SectionPanel>

      <SectionPanel title="Bracket pregled">
        {details ? (
          <div className="tournament-rounds">
            {details.rounds.map((round) => (
              <article key={round.roundNumber} className="round-card">
                <h3>Runda {round.roundNumber}</h3>
                {round.matches.map((match) => (
                  <div key={match.id} className="match-line">
                    <div>
                      <strong>
                        {match.player1?.username ?? 'TBD'} vs {match.player2?.username ?? 'TBD'}
                      </strong>
                      <span>Status: {match.status}</span>
                      {match.winner ? <span>Pobednik: {match.winner.username}</span> : null}
                    </div>
                    {activeMatchId && activeMatchId !== match.id ? (
                      <span className="muted">Prvo zavrsi svoj aktivan mec.</span>
                    ) : (
                      <Link className="button button--ghost" to={`/game?matchId=${match.id}`}>
                        Otvori mec
                      </Link>
                    )}
                  </div>
                ))}
              </article>
            ))}
          </div>
        ) : (
          <p className="muted">Izaberi turnir da bi video bracket pregled.</p>
        )}
      </SectionPanel>
    </>
  )
}
