import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { MatchFound, UserProfile } from '../types'

interface MatchmakingPageProps {
  user: UserProfile
}

export function MatchmakingPage({ user }: MatchmakingPageProps) {
  const navigate = useNavigate()
  const [status, setStatus] = useState('Spreman za matchmaking.')
  const [match, setMatch] = useState<MatchFound | null>(null)

  useEffect(() => {
    let cancelled = false

    const interval = window.setInterval(() => {
      void checkMatch(true)
    }, 3000)

    return () => {
      cancelled = true
      window.clearInterval(interval)
    }

    async function checkMatch(silent = false) {
      try {
        const result = await api.checkMatchmaking(user.id)
        if ('matchId' in result) {
          if (!cancelled) {
            setMatch(result)
            setStatus(`Mec je pronadjen: ${result.player1} vs ${result.player2}. Otvaramo partiju...`)
            window.setTimeout(() => navigate(`/game?matchId=${result.matchId}`), 700)
          }
          return
        }

        if (!silent && !cancelled) {
          setStatus(result.message)
        }
      } catch (caughtError) {
        if (!silent && !cancelled) {
          setStatus(caughtError instanceof Error ? caughtError.message : 'Greska pri proveri meca.')
        }
      }
    }
  }, [navigate])

  async function joinQueue() {
    try {
      const response = await api.joinMatchmaking(user.id)
      setStatus(typeof response === 'string' ? response : 'Uspesno dodat u queue.')
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Greska pri ulasku u red.')
    }
  }

  async function checkMatch() {
      try {
      const result = await api.checkMatchmaking(user.id)
      if ('matchId' in result) {
        setMatch(result)
        setStatus(`Mec je pronadjen: ${result.player1} vs ${result.player2}. Otvaramo partiju...`)
        window.setTimeout(() => navigate(`/game?matchId=${result.matchId}`), 700)
        return
      }

      setStatus(result.message)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Greska pri proveri meca.')
    }
  }

  async function joinTournamentQueue() {
    try {
      const response = await api.joinTournamentQueue(user.id)
      setStatus(response)
    } catch (caughtError) {
      setStatus(
        caughtError instanceof Error
          ? caughtError.message
          : 'Greska pri prijavi u turnirski red.',
      )
    }
  }

  return (
    <>
      <SectionPanel title="Ranked matchmaking">
        <div className="action-row">
          <button className="button" onClick={joinQueue}>
            Udji u matchmaking red
          </button>
          <button className="button button--accent" onClick={checkMatch}>
            Proveri da li je mec pronadjen
          </button>
          <button className="button button--ghost" onClick={joinTournamentQueue}>
            Prijavi se za turnirski red
          </button>
        </div>

        <p className="status-banner">{status}</p>

        {match ? (
          <article className="match-card">
            <strong>Poslednji pronadjeni mec</strong>
            <span>
              {match.player1} vs {match.player2}
            </span>
            <span>Mec je spreman za otvaranje.</span>
          </article>
        ) : null}
      </SectionPanel>

      <SectionPanel title="Kako radi matchmaking" eyebrow="Tok uparivanja">
        <div className="list-stack">
          <article className="list-item">
            <div>
              <strong>1. Ulazak u red</strong>
              <span>Igrac se prijavljuje za trazenje protivnika.</span>
            </div>
          </article>
          <article className="list-item">
            <div>
              <strong>2. Obrada reda</strong>
              <span>Sistem uparuje igrace prema njihovom rangu.</span>
            </div>
          </article>
          <article className="list-item">
            <div>
              <strong>3. Pokretanje meca</strong>
              <span>Pronadjeni par prelazi na ekran za igru i live sinhronizaciju.</span>
            </div>
          </article>
        </div>
      </SectionPanel>
    </>
  )
}
