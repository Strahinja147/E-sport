import { HttpTransportType, HubConnectionBuilder } from '@microsoft/signalr'
import { useEffect, useEffectEvent, useRef, useState } from 'react'
import { BrowserRouter, Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom'
import { AppShell } from './components/AppShell'
import { AuthPage } from './pages/AuthPage'
import { DashboardPage } from './pages/DashboardPage'
import { GamePage } from './pages/GamePage'
import { LeaderboardPage } from './pages/LeaderboardPage'
import { MatchmakingPage } from './pages/MatchmakingPage'
import { SecurityPage } from './pages/SecurityPage'
import { ShopPage } from './pages/ShopPage'
import { SocialPage } from './pages/SocialPage'
import { TeamsPage } from './pages/TeamsPage'
import { TournamentPage } from './pages/TournamentPage'
import { api, resolveSignalRUrl } from './lib/api'
import type { SystemNotice, UserProfile } from './types'

const storageKey = 'esport-frontend-user'

function readStoredUser() {
  const raw = window.localStorage.getItem(storageKey)
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw) as UserProfile
  } catch {
    return null
  }
}

export default function App() {
  return (
    <BrowserRouter>
      <AppContent />
    </BrowserRouter>
  )
}

function AppContent() {
  const navigate = useNavigate()
  const location = useLocation()
  const [user, setUser] = useState<UserProfile | null>(() => readStoredUser())
  const [users, setUsers] = useState<UserProfile[]>([])
  const [notices, setNotices] = useState<SystemNotice[]>([])
  const lastHandledMatchIdRef = useRef<string | null>(null)

  const addNotice = useEffectEvent((title: string, detail: string) => {
    const noticeId = crypto.randomUUID()
    setNotices((current) =>
      [{ id: noticeId, title, detail }, ...current].slice(0, 5),
    )

    window.setTimeout(() => {
      setNotices((current) => current.filter((notice) => notice.id !== noticeId))
    }, 8000)
  })

  const handleAssignedMatch = useEffectEvent((match: {
    matchId: string
    player1: string
    player2: string
    player1Id?: string
    player2Id?: string
  }) => {
    const currentGameMatchId =
      location.pathname === '/game' ? new URLSearchParams(location.search).get('matchId') : null

    if (currentGameMatchId === match.matchId) {
      lastHandledMatchIdRef.current = match.matchId
      return
    }

    if (lastHandledMatchIdRef.current === match.matchId) {
      return
    }

    lastHandledMatchIdRef.current = match.matchId

    const currentUser = readStoredUser()
    const opponent =
      currentUser && (match.player1Id === currentUser.id || match.player1 === currentUser.username)
        ? match.player2
        : match.player1

    addNotice('Mec spreman', `Ulazis u mec protiv ${opponent}.`)
    window.setTimeout(() => navigate(`/game?matchId=${match.matchId}`), 700)
  })

  const syncCurrentUser = useEffectEvent(async () => {
    const allUsers = await api.getUsers()
    setUsers(allUsers)

    const storedUser = readStoredUser()

    if (!storedUser) {
      return
    }

    const refreshedUser = allUsers.find((candidate) => candidate.id === storedUser.id)
    if (refreshedUser) {
      setUser(refreshedUser)
      window.localStorage.setItem(storageKey, JSON.stringify(refreshedUser))
    }
  })

  useEffect(() => {
    void syncCurrentUser().catch(() => undefined)
  }, [syncCurrentUser])

  const userId = user?.id ?? null

  useEffect(() => {
    if (!userId) {
      return
    }

    const connection = new HubConnectionBuilder()
      .withUrl(resolveSignalRUrl('/gamehub'), {
        transport: HttpTransportType.LongPolling,
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .build()

    connection.on(
      'MatchFound',
      (match: {
        matchId: string
        player1: string
        player2: string
        player1Id?: string
        player2Id?: string
      }) => {
        const currentUser = readStoredUser()
        if (!currentUser) {
          return
        }

        const isParticipant =
          match.player1Id === currentUser.id ||
          match.player2Id === currentUser.id ||
          match.player1 === currentUser.username ||
          match.player2 === currentUser.username

        if (!isParticipant) {
          return
        }

        handleAssignedMatch(match)
      },
    )

    connection.on('TournamentStarted', (_tournamentId: string, players: string[]) => {
      addNotice('Tournament started', `Turnir je poceo sa ${players.length} igraca.`)
    })

    void connection.start().catch(() => undefined)

    return () => {
      void connection.stop()
    }
  }, [addNotice, userId])

  useEffect(() => {
    if (!userId) {
      return
    }

    let active = true

    const checkAssignedMatch = async () => {
      if (!active) {
        return
      }

      try {
        const result = await api.checkMatchmaking(userId)
        if (!active || !('matchId' in result)) {
          return
        }

        handleAssignedMatch(result)
      } catch {
      }
    }

    void checkAssignedMatch()
    const interval = window.setInterval(() => {
      void checkAssignedMatch()
    }, 3000)

    return () => {
      active = false
      window.clearInterval(interval)
    }
  }, [handleAssignedMatch, userId])

  function handleAuthenticated(nextUser: UserProfile) {
    setUser(nextUser)
    window.localStorage.setItem(storageKey, JSON.stringify(nextUser))
    void syncCurrentUser()
  }

  async function handleLogout() {
    const currentUser = user

    setUser(null)
    window.localStorage.removeItem(storageKey)
    lastHandledMatchIdRef.current = null

    if (currentUser) {
      try {
        await api.logout(currentUser.id)
      } catch {
      }
    }
  }

  if (!user) {
    return <AuthPage onAuthenticated={handleAuthenticated} />
  }

  return (
    <AppShell user={user} notices={notices} onLogout={() => void handleLogout()}>
      <Routes>
        <Route path="/" element={<DashboardPage user={user} />} />
        <Route path="/leaderboard" element={<LeaderboardPage />} />
        <Route path="/matchmaking" element={<MatchmakingPage user={user} />} />
        <Route
          path="/game"
          element={<GamePage user={user} onUserRefresh={syncCurrentUser} />}
        />
        <Route
          path="/shop"
          element={<ShopPage user={user} onUserRefresh={syncCurrentUser} />}
        />
        <Route path="/tournament" element={<TournamentPage user={user} />} />
        <Route
          path="/teams"
          element={<TeamsPage user={user} users={users} onUserRefresh={syncCurrentUser} />}
        />
        <Route
          path="/social"
          element={<SocialPage user={user} users={users} onUserRefresh={syncCurrentUser} />}
        />
        <Route path="/security" element={<SecurityPage user={user} />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Routes>
    </AppShell>
  )
}
