import { useEffect, useState } from 'react'
import { SectionPanel } from '../components/SectionPanel'
import { api } from '../lib/api'
import type { Friend, UserProfile } from '../types'

interface SocialPageProps {
  user: UserProfile
  users: UserProfile[]
  onUserRefresh: () => Promise<void>
}

export function SocialPage({ user, users, onUserRefresh }: SocialPageProps) {
  const [status, setStatus] = useState('Upravljaj prijateljima i pregledaj aktivnost.')
  const [friendUsername, setFriendUsername] = useState('')
  const [onlineFriendIds, setOnlineFriendIds] = useState<string[]>([])

  useEffect(() => {
    void loadActivity()
  }, [user.id])

  async function loadActivity() {
    try {
      const onlineIds = await api.getOnlineFriends(user.id)
      setOnlineFriendIds(onlineIds)
    } catch {
      setOnlineFriendIds([])
    }
  }

  async function runFriendAction(
    action: 'sendByUsername' | 'accept' | 'reject' | 'remove',
    otherUserId?: string,
  ) {
    try {
      if (action === 'sendByUsername') {
        setStatus(await api.sendFriendRequestByUsername(user.id, friendUsername.trim()))
        setFriendUsername('')
      }

      if (action === 'accept' && otherUserId) {
        setStatus(await api.acceptFriendRequest(user.id, otherUserId))
      }

      if (action === 'reject' && otherUserId) {
        setStatus(await api.rejectFriendRequest(user.id, otherUserId))
      }

      if (action === 'remove' && otherUserId) {
        await api.removeFriend(user.id, otherUserId)

        const friend = user.friends.find((entry) => entry.userId === otherUserId)
        const isOutgoingRequest = friend?.status === 'Pending' && friend.requestedByUserId === user.id

        setStatus(
          isOutgoingRequest ? 'Zahtev za prijateljstvo opozvan.' : 'Prijatelj obrisan sa liste!',
        )
      }

      await onUserRefresh()
      await loadActivity()
    } catch (caughtError) {
      setStatus(caughtError instanceof Error ? caughtError.message : 'Akcija nije uspela.')
    }
  }

  function resolveUser(friend: Friend) {
    return users.find((candidate) => candidate.id === friend.userId)
  }

  const acceptedFriends = user.friends.filter((friend) => friend.status === 'Accepted')
  const incomingRequests = user.friends.filter(
    (friend) => friend.status === 'Pending' && friend.requestedByUserId === friend.userId,
  )
  const outgoingRequests = user.friends.filter(
    (friend) => friend.status === 'Pending' && friend.requestedByUserId === user.id,
  )

  return (
    <>
      <SectionPanel title="Dodaj prijatelja">
        <p className="status-banner">{status}</p>
        <div className="dual-grid">
          <label className="field">
            <span>Korisnicko ime</span>
            <input
              value={friendUsername}
              onChange={(event) => setFriendUsername(event.target.value)}
              placeholder="Unesi username igraca"
            />
          </label>
          <div className="stack-actions">
            <button
              className="button"
              disabled={!friendUsername.trim()}
              onClick={() => void runFriendAction('sendByUsername')}
            >
              Posalji zahtev
            </button>
          </div>
        </div>
      </SectionPanel>

      <SectionPanel title="Primljeni zahtevi">
        <div className="cards-grid">
          {incomingRequests.length > 0 ? (
            incomingRequests.map((friend) => {
              const profile = resolveUser(friend)

              return (
                <article key={friend.userId} className="friend-card">
                  <h3>{friend.username}</h3>
                  <span>ELO {profile?.eloRating ?? '-'}</span>
                  <span>Status: Primljen zahtev</span>
                  <div className="stack-actions">
                    <button
                      className="button button--accent"
                      onClick={() => void runFriendAction('accept', friend.userId)}
                    >
                      Prihvati
                    </button>
                    <button
                      className="button button--ghost"
                      onClick={() => void runFriendAction('reject', friend.userId)}
                    >
                      Odbij
                    </button>
                  </div>
                </article>
              )
            })
          ) : (
            <p className="muted">Trenutno nemas novih zahteva.</p>
          )}
        </div>
      </SectionPanel>

      <SectionPanel title="Poslati zahtevi">
        <div className="cards-grid">
          {outgoingRequests.length > 0 ? (
            outgoingRequests.map((friend) => (
              <article key={friend.userId} className="friend-card">
                <h3>{friend.username}</h3>
                <span>Status: Zahtev poslat</span>
                <div className="stack-actions">
                  <button
                    className="button button--ghost"
                    onClick={() => void runFriendAction('remove', friend.userId)}
                  >
                    Otkazi zahtev
                  </button>
                </div>
              </article>
            ))
          ) : (
            <p className="muted">Nemas aktivnih poslatih zahteva.</p>
          )}
        </div>
      </SectionPanel>

      <SectionPanel title="Tvoji prijatelji">
        <div className="cards-grid">
          {acceptedFriends.length > 0 ? (
            acceptedFriends.map((friend) => {
              const profile = resolveUser(friend)
              const isOnline = onlineFriendIds.includes(friend.userId)

              return (
                <article key={friend.userId} className="friend-card">
                  <h3>{friend.username}</h3>
                  <span>ELO {profile?.eloRating ?? '-'}</span>
                  <span>Status: {isOnline ? 'Online' : 'Offline'}</span>
                  <div className="stack-actions">
                    <button
                      className="button button--ghost"
                      onClick={() => void runFriendAction('remove', friend.userId)}
                    >
                      Ukloni prijatelja
                    </button>
                  </div>
                </article>
              )
            })
          ) : (
            <p className="muted">Jos nemas prihvacenih prijatelja.</p>
          )}
        </div>
      </SectionPanel>

    </>
  )
}
