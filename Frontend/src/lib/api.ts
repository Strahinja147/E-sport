import type {
  AuditLog,
  InventoryItem,
  LeaderboardEntry,
  MatchHistoryItem,
  MatchFound,
  MatchMove,
  OnlineCount,
  PlayerProgress,
  RevenueReport,
  ShopItem,
  Team,
  TicTacToeGame,
  TournamentDetails,
  TournamentSummary,
  UserProfile,
} from '../types'

type QueryValue = string | number | boolean | null | undefined

const API_BASE = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.replace(/\/$/, '') ?? ''
const SIGNALR_BASE =
  (import.meta.env.VITE_SIGNALR_BASE_URL as string | undefined)?.replace(/\/$/, '') ??
  'https://localhost:7109'

export function resolveApiUrl(path: string) {
  const normalized = path.startsWith('/') ? path : `/${path}`
  return API_BASE ? `${API_BASE}${normalized}` : normalized
}

export function resolveSignalRUrl(path: string) {
  const normalized = path.startsWith('/') ? path : `/${path}`
  return `${SIGNALR_BASE}${normalized}`
}

function buildUrl(path: string, query?: Record<string, QueryValue>) {
  const url = new URL(resolveApiUrl(path), window.location.origin)

  if (query) {
    for (const [key, value] of Object.entries(query)) {
      if (value !== undefined && value !== null && value !== '') {
        url.searchParams.set(key, String(value))
      }
    }
  }

  return url.toString()
}

async function request<T>(
  path: string,
  options?: RequestInit,
  query?: Record<string, QueryValue>,
): Promise<T> {
  const response = await fetch(buildUrl(path, query), {
    headers: {
      Accept: 'application/json, text/plain, */*',
      ...options?.headers,
    },
    ...options,
  })

  const raw = await response.text()
  const data = parsePayload(raw)

  if (!response.ok) {
    throw new Error(extractErrorMessage(data, response.statusText))
  }

  return data as T
}

function parsePayload(raw: string) {
  if (!raw) {
    return null
  }

  try {
    return JSON.parse(raw)
  } catch {
    return raw
  }
}

function extractErrorMessage(payload: unknown, fallback: string) {
  if (typeof payload === 'string') {
    return payload
  }

  if (payload && typeof payload === 'object') {
    const maybeMessage = Reflect.get(payload, 'message')
    if (typeof maybeMessage === 'string') {
      return maybeMessage
    }
  }

  return fallback
}

function normalizeMessage(payload: unknown, fallback: string) {
  if (typeof payload === 'string') {
    return payload
  }

  if (payload && typeof payload === 'object') {
    const maybeMessage = Reflect.get(payload, 'message')
    if (typeof maybeMessage === 'string') {
      return maybeMessage
    }
  }

  return fallback
}

export const api = {
  getUsers: () => request<UserProfile[]>('/api/User/all'),
  register: (username: string, email: string, password: string) =>
    request<UserProfile>('/api/User/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username, email, password }),
    }),
  login: (email: string, password: string) =>
    request<UserProfile>('/api/User/login', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ email, password }),
    }),
  logout: (userId: string) =>
    request<string>('/api/User/logout', { method: 'POST' }, { userId }),
  getOnlineCount: () => request<OnlineCount>('/api/User/online-count'),
  getOnlineFriends: (userId: string) =>
    request<string[]>(`/api/User/online-friends/${userId}`),
  getAuditLogs: (userId: string) =>
    request<AuditLog[]>(`/api/User/audit-logs/${userId}`),
  getPlayerProgress: (userId: string) =>
    request<PlayerProgress[]>(`/api/User/progress/${userId}`),
  sendFriendRequestByUsername: (senderId: string, username: string) =>
    request<string>('/api/User/send-friend-request-by-username', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ senderId, username }),
    }),
  acceptFriendRequest: (myUserId: string, friendId: string) =>
    request<string>(
      '/api/User/accept-friend-request',
      { method: 'POST' },
      { myUserId, friendId },
    ),
  rejectFriendRequest: (myUserId: string, senderId: string) =>
    request<string>(
      '/api/User/reject-friend-request',
      { method: 'POST' },
      { myUserId, senderId },
    ),
  removeFriend: (myUserId: string, friendId: string) =>
    request<string>(
      '/api/User/remove-friend',
      { method: 'POST' },
      { myUserId, friendId },
    ),
  getLeaderboard: (count = 10) =>
    request<LeaderboardEntry[]>('/Matchmaking/leaderboard', undefined, { count }),
  joinMatchmaking: (userId: string) =>
    request<string>('/Matchmaking/join', { method: 'POST' }, { userId }),
  checkMatchmaking: (userId?: string) =>
    request<MatchFound | { message: string }>('/Matchmaking/check-match', undefined, { userId }),
  joinTournamentQueue: async (userId: string) =>
    normalizeMessage(
      await request<unknown>('/Matchmaking/join-tournament', { method: 'POST' }, { userId }),
      'Uspesno prijavljen u turnirski red.',
    ),
  getGameState: (matchId: string) => request<TicTacToeGame>(`/Game/${matchId}`),
  getMove: (matchId: string) => request<TicTacToeGame>(`/Game/${matchId}/move`),
  getMatchHistory: (userId: string) => request<MatchHistoryItem[]>(`/Game/history/${userId}`),
  getMatchMoves: (matchId: string) => request<MatchMove[]>(`/Game/${matchId}/moves`),
  makeMove: (
    matchId: string,
    playerId: string,
    position: number,
    symbol: string,
    version: number,
  ) =>
    request<{ message: string }>(
      '/Game/move',
      { method: 'POST' },
      { matchId, playerId, position, symbol, version },
    ),
  getMatchChat: (matchId: string) => request<string[]>(`/Game/${matchId}/chat`),
  sendChatMessage: (matchId: string, playerId: string, message: string) =>
    request<string>(`/Game/${matchId}/chat`, { method: 'POST' }, { playerId, message }),
  snapshotLeaderboard: () =>
    request<{ message: string }>('/Game/snapshot-leaderboard', { method: 'POST' }),
  getShopItems: () => request<ShopItem[]>('/api/Shop/items'),
  buyItem: (userId: string, itemId: string) =>
    request<{ message: string }>('/api/Shop/buy', { method: 'POST' }, { userId, itemId }),
  sellItem: (userId: string, itemId: string, purchasedAt: string) =>
    request<{ message: string }>('/api/Shop/sell', { method: 'DELETE' }, { userId, itemId, purchasedAt }),
  getRevenue: (yearMonth: string) =>
    request<RevenueReport>(`/api/Shop/revenue/${yearMonth}`),
  getInventory: (userId: string) =>
    request<InventoryItem[]>(`/Inventory/my-inventory/${userId}`),
  getTournaments: () => request<TournamentSummary[]>('/api/Tournament'),
  getTournamentDetails: (id: string) =>
    request<TournamentDetails>(`/api/Tournament/details/${id}`),
  createTeam: (name: string, ownerId: string) =>
    request<Team>('/api/Team/create', { method: 'POST' }, { name, ownerId }),
  addTeamMember: (teamId: string, userId: string) =>
    request<string>('/api/Team/add-member', { method: 'POST' }, { teamId, userId }),
  sendTeamInvite: (teamId: string, senderId: string, userId: string) =>
    request<string>('/api/Team/send-invite', { method: 'POST' }, { teamId, senderId, userId }),
  sendTeamInviteByUsername: (teamId: string, senderId: string, username: string) =>
    request<string>('/api/Team/send-invite-by-username', { method: 'POST' }, { teamId, senderId, username }),
  acceptTeamInvite: (teamId: string, userId: string) =>
    request<string>('/api/Team/accept-invite', { method: 'POST' }, { teamId, userId }),
  rejectTeamInvite: (teamId: string, userId: string) =>
    request<string>('/api/Team/reject-invite', { method: 'POST' }, { teamId, userId }),
  cancelTeamInvite: (teamId: string, senderId: string, userId: string) =>
    request<string>('/api/Team/cancel-invite', { method: 'POST' }, { teamId, senderId, userId }),
  getTeam: (teamId: string) => request<Team>(`/api/Team/${teamId}`),
}
