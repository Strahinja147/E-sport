import { useEffect, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { Team, UserProfile } from '../types'

interface TeamsPageProps {
  user: UserProfile
  users: UserProfile[]
  onUserRefresh: () => Promise<void>
}

export function TeamsPage({ user, users, onUserRefresh }: TeamsPageProps) {
  const [teamName, setTeamName] = useState('')
  const [selectedMemberId, setSelectedMemberId] = useState('')
  const [team, setTeam] = useState<Team | null>(null)
  const [status, setStatus] = useState('Kreiraj tim ili otvori svoj postojeci tim.')

  useEffect(() => {
    if (user.currentTeamId) {
      void loadCurrentTeam()
    } else {
      setTeam(null)
    }
  }, [user.currentTeamId])

  function resolveUsername(userId: string) {
    return users.find((candidate) => candidate.id === userId)?.username ?? 'Nepoznat korisnik'
  }

  async function createTeam() {
    if (!teamName.trim()) {
      setStatus('Unesi naziv tima.')
      return
    }

    try {
      const nextTeam = await api.createTeam(teamName.trim(), user.id)
      setTeam(nextTeam)
      setStatus(`Tim ${nextTeam.name} je uspesno kreiran.`)
      await onUserRefresh()
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Kreiranje tima nije uspelo.')
    }
  }

  async function addMember() {
    if (!team?.id || !selectedMemberId) {
      setStatus('Otvori tim i izaberi clana.')
      return
    }

    try {
      const result = await api.addTeamMember(team.id, selectedMemberId)
      setStatus(result)
      await loadCurrentTeam(team.id)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Dodavanje clana nije uspelo.')
    }
  }

  async function loadCurrentTeam(teamId = user.currentTeamId ?? team?.id ?? '') {
    if (!teamId) {
      setStatus('Trenutno nisi clan nijednog tima.')
      setTeam(null)
      return
    }

    try {
      const loadedTeam = await api.getTeam(teamId)
      setTeam(loadedTeam)
      setStatus(`Ucitani detalji za tim ${loadedTeam.name}.`)
    } catch (caughtError) {
      setTeam(null)
      setStatus(caughtError instanceof Error ? caughtError.message : 'Tim nije pronadjen.')
    }
  }

  return (
    <>
      <SectionPanel title="Kreiranje i pregled tima">
        <div className="form-grid">
          <label className="field">
            <span>Naziv tima</span>
            <input
              value={teamName}
              onChange={(event) => setTeamName(event.target.value)}
              placeholder="Night Falcons"
            />
          </label>
          <button className="button" onClick={createTeam}>
            Kreiraj tim
          </button>

          <button className="button button--ghost" onClick={() => void loadCurrentTeam()}>
            Otvori moj tim
          </button>
        </div>

        <p className="status-banner">{status}</p>
      </SectionPanel>

      <SectionPanel title="Dodavanje clanova" eyebrow="Team management">
        <div className="toolbar-inline">
          <select value={selectedMemberId} onChange={(event) => setSelectedMemberId(event.target.value)}>
            <option value="">Izaberi korisnika</option>
            {users
              .filter((candidate) => candidate.id !== user.id)
              .map((candidate) => (
                <option key={candidate.id} value={candidate.id}>
                  {candidate.username}
                </option>
              ))}
          </select>
          <button className="button button--accent" onClick={addMember}>
            Dodaj clana u tim
          </button>
        </div>

        {team ? (
          <div className="dual-grid">
            <div className="nested-card">
              <h3>{team.name}</h3>
              <span>Owner: {resolveUsername(team.ownerId)}</span>
              <span>Team ELO: {team.teamElo}</span>
            </div>
            <div className="nested-card">
              <h3>Clanovi</h3>
              {team.memberIds.map((memberId) => (
                <span key={memberId}>{resolveUsername(memberId)}</span>
              ))}
            </div>
          </div>
        ) : (
          <p className="muted">Nema ucitanih podataka o timu.</p>
        )}
      </SectionPanel>
    </>
  )
}
