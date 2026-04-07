import { useEffect, useMemo, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { Team, TeamInvite, UserProfile } from '../types'

interface TeamsPageProps {
  user: UserProfile
  users: UserProfile[]
  onUserRefresh: () => Promise<void>
}

export function TeamsPage({ user, users, onUserRefresh }: TeamsPageProps) {
  const [teamName, setTeamName] = useState('')
  const [selectedFriendId, setSelectedFriendId] = useState('')
  const [manualUsername, setManualUsername] = useState('')
  const [team, setTeam] = useState<Team | null>(null)
  const [status, setStatus] = useState('Kreiraj tim ili upravljaj pozivima za postojece clanove.')

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

  function resolveUserByUsername(username: string) {
    const normalizedUsername = username.trim().toLowerCase()
    return users.find(
      (candidate) => candidate.username.trim().toLowerCase() === normalizedUsername,
    )
  }

  function validateInviteTarget(targetUser: UserProfile | undefined) {
    if (!targetUser) {
      return 'Korisnik sa tim korisnickim imenom ne postoji.'
    }

    if (targetUser.id === user.id) {
      return 'Ne mozes da posaljes timski poziv samom sebi.'
    }

    if (teamMemberIds.has(targetUser.id)) {
      return 'Taj korisnik je vec clan tvog tima.'
    }

    if (pendingInviteIds.has(targetUser.id)) {
      return 'Poziv za tog korisnika je vec poslat.'
    }

    if (targetUser.currentTeamId && targetUser.currentTeamId !== currentTeamId) {
      return 'Taj korisnik je vec clan nekog drugog tima.'
    }

    return null
  }

  const currentTeamId = user.currentTeamId ?? team?.id ?? ''
  const teamMemberIds = new Set(team?.memberIds ?? [])
  const pendingInviteIds = new Set(team?.pendingInvites.map((invite) => invite.userId) ?? [])

  const acceptedFriends = useMemo(
    () =>
      user.friends
        .filter((friend) => friend.status === 'Accepted')
        .filter((friend) => friend.userId !== user.id)
        .filter((friend) => !teamMemberIds.has(friend.userId))
        .filter((friend) => !pendingInviteIds.has(friend.userId)),
    [pendingInviteIds, teamMemberIds, user.friends, user.id],
  )

  const incomingTeamInvites = user.teamInvites ?? []

  async function createTeam() {
    if (!teamName.trim()) {
      setStatus('Unesi naziv tima.')
      return
    }

    try {
      const nextTeam = await api.createTeam(teamName.trim(), user.id)
      setTeam(nextTeam)
      setStatus(`Tim ${nextTeam.name} je uspesno kreiran.`)
      setTeamName('')
      await onUserRefresh()
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Kreiranje tima nije uspelo.')
    }
  }

  async function loadCurrentTeam(teamId = currentTeamId) {
    if (!teamId) {
      setStatus('Trenutno nisi clan nijednog tima.')
      setTeam(null)
      return
    }

    try {
      const loadedTeam = await api.getTeam(teamId)
      setTeam(loadedTeam)
    } catch (caughtError) {
      setTeam(null)
      setStatus(caughtError instanceof Error ? caughtError.message : 'Tim nije pronadjen.')
    }
  }

  async function refreshPageData(teamId = currentTeamId) {
    await onUserRefresh()

    if (teamId) {
      await loadCurrentTeam(teamId)
    } else {
      setTeam(null)
    }
  }

  async function sendInviteToFriend() {
    if (!currentTeamId) {
      setStatus('Prvo moras da budes u timu da bi slao pozive.')
      return
    }

    if (!selectedFriendId) {
      setStatus('Izaberi prijatelja kom saljes poziv.')
      return
    }

    const selectedFriend = users.find((candidate) => candidate.id === selectedFriendId)
    const validationMessage = validateInviteTarget(selectedFriend)
    if (validationMessage) {
      setStatus(validationMessage)
      return
    }

    try {
      setStatus(await api.sendTeamInvite(currentTeamId, user.id, selectedFriendId))
      setSelectedFriendId('')
      await refreshPageData(currentTeamId)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Slanje poziva nije uspelo.')
    }
  }

  async function sendInviteByUsername() {
    if (!currentTeamId) {
      setStatus('Prvo moras da budes u timu da bi slao pozive.')
      return
    }

    const normalizedUsername = manualUsername.trim()

    if (!normalizedUsername) {
      setStatus('Unesi korisnicko ime igraca kom saljes poziv.')
      return
    }

    const matchedUser = resolveUserByUsername(normalizedUsername)
    const validationMessage = validateInviteTarget(matchedUser)
    if (validationMessage) {
      setStatus(validationMessage)
      return
    }

    try {
      setStatus(await api.sendTeamInviteByUsername(currentTeamId, user.id, normalizedUsername))
      setManualUsername('')
      await refreshPageData(currentTeamId)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Slanje poziva nije uspelo.')
    }
  }

  async function respondToInvite(action: 'accept' | 'reject', invite: TeamInvite) {
    try {
      setStatus(
        action === 'accept'
          ? await api.acceptTeamInvite(invite.teamId, user.id)
          : await api.rejectTeamInvite(invite.teamId, user.id),
      )
      await refreshPageData(action === 'accept' ? invite.teamId : currentTeamId)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Obrada poziva nije uspela.')
    }
  }

  async function cancelInvite(invitedUserId: string) {
    if (!currentTeamId) {
      setStatus('Trenutno nemas aktivan tim.')
      return
    }

    try {
      setStatus(await api.cancelTeamInvite(currentTeamId, user.id, invitedUserId))
      await refreshPageData(currentTeamId)
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Opozivanje poziva nije uspelo.')
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
        </div>

        <p className="status-banner">{status}</p>
      </SectionPanel>

      <SectionPanel title="Primljeni pozivi za tim">
        <div className="cards-grid">
          {incomingTeamInvites.length > 0 ? (
            incomingTeamInvites.map((invite) => (
              <article key={invite.teamId} className="friend-card">
                <h3>{invite.teamName}</h3>
                <span>Pozvao te je: {invite.requestedByUsername}</span>
                <div className="stack-actions">
                  <button
                    className="button button--accent"
                    onClick={() => void respondToInvite('accept', invite)}
                  >
                    Prihvati
                  </button>
                  <button
                    className="button button--ghost"
                    onClick={() => void respondToInvite('reject', invite)}
                  >
                    Odbij
                  </button>
                </div>
              </article>
            ))
          ) : (
            <p className="muted">Trenutno nemas novih timskih poziva.</p>
          )}
        </div>
      </SectionPanel>

      <SectionPanel title="Slanje poziva" eyebrow="Team management">
        {team ? (
          <div className="dual-grid">
            <div className="nested-card">
              <h3>Pozovi prijatelja</h3>
              <span>Izaberi prijatelja kog zelis da pozoves u tim.</span>
              <div className="toolbar-inline">
                <select
                  value={selectedFriendId}
                  onChange={(event) => setSelectedFriendId(event.target.value)}
                >
                  <option value="">Izaberi prijatelja</option>
                  {acceptedFriends.map((friend) => (
                    <option key={friend.userId} value={friend.userId}>
                      {friend.username}
                    </option>
                  ))}
                </select>
                <button className="button button--accent" onClick={sendInviteToFriend}>
                  Posalji poziv
                </button>
              </div>
              {acceptedFriends.length === 0 ? (
                <p className="muted">Nemas prijatelje koje trenutno mozes da pozoves u tim.</p>
              ) : null}
            </div>

            <div className="nested-card">
              <h3>Pozovi po korisnickom imenu</h3>
              <span>Unesi username igraca koji nije na tvojoj listi prijatelja.</span>
              <div className="toolbar-inline">
                <input
                  value={manualUsername}
                  onChange={(event) => setManualUsername(event.target.value)}
                  placeholder="Unesi korisnicko ime"
                />
                <button className="button button--accent" onClick={sendInviteByUsername}>
                  Posalji poziv
                </button>
              </div>
            </div>
          </div>
        ) : (
          <p className="muted">Kada budes u timu, ovde ces moci da saljes pozive drugim igracima.</p>
        )}
      </SectionPanel>

      {team ? (
        <>
          <SectionPanel title="Poslati pozivi">
            <div className="cards-grid">
              {team.pendingInvites.length > 0 ? (
                team.pendingInvites.map((invite) => (
                  <article key={invite.userId} className="friend-card">
                    <h3>{invite.username}</h3>
                    <span>Poziv poslao: {invite.requestedByUsername}</span>
                    <div className="stack-actions">
                      <button
                        className="button button--ghost"
                        onClick={() => void cancelInvite(invite.userId)}
                      >
                        Opozovi poziv
                      </button>
                    </div>
                  </article>
                ))
              ) : (
                <p className="muted">Trenutno nema aktivnih poziva za ovaj tim.</p>
              )}
            </div>
          </SectionPanel>

          <SectionPanel title="Tvoj tim">
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
          </SectionPanel>
        </>
      ) : (
        <SectionPanel title="Tvoj tim">
          <p className="muted">Trenutno nisi clan nijednog tima.</p>
        </SectionPanel>
      )}
    </>
  )
}
