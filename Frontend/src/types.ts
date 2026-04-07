export interface Friend {
  userId: string
  username: string
  status: string
  requestedByUserId?: string | null
}

export interface PlayerStatistics {
  totalGames: number
  wins: number
  losses: number
  tournamentsPlayed: number
  tournamentWins: number
  tournamentWinRate: number
  winRate: number
  lastGameAt: string
}

export interface UserProfile {
  id: string
  username: string
  email?: string | null
  eloRating: number
  coins: number
  stats: PlayerStatistics
  currentTeamId?: string | null
  friends: Friend[]
}

export interface LeaderboardEntry {
  rank: number
  userId: string
  username: string
  eloRating: number
  wins: number
  tournamentWins: number
}

export interface MatchFound {
  matchId: string
  player1: string
  player2: string
  player1Id?: string
  player2Id?: string
}

export interface TicTacToeGame {
  id: string
  board: string
  currentTurn: string
  version: number
  status: string
  player1Id?: string | null
  player2Id?: string | null
  tournamentId?: string | null
}

export interface ShopItem {
  id: string
  name: string
  price: number
  isLimited: boolean
  initialStock: number
  currentStock: number
}

export interface InventoryItem {
  itemId: string
  itemName: string
  purchasedAt: string
  purchasePrice: number
  resalePrice: number
}

export interface RevenueReport {
  month: string
  totalRevenue: number
  bestSellingItem: string
  salesCount: number
}

export interface Team {
  id: string
  name: string
  ownerId: string
  memberIds: string[]
  teamElo: number
  teamAchievements: string[]
  createdAt: string
}

export interface PlayerDto {
  id: string
  username: string
}

export interface MatchDetails {
  id: string
  status: string
  player1?: PlayerDto | null
  player2?: PlayerDto | null
  winner?: PlayerDto | null
}

export interface TournamentRound {
  roundNumber: number
  matches: MatchDetails[]
}

export interface TournamentDetails {
  id: string
  name: string
  rounds: TournamentRound[]
}

export interface TournamentSummary {
  id: string
  name: string
  status: string
  rounds: { roundNumber: number; matchIds: string[] }[]
}

export interface AuditLog {
  time: string
  ip: string
  device: string
}

export interface PlayerProgress {
  userId: string
  timestamp: string
  elo: number
  coins: number
  changeReason: string
}

export interface MatchHistoryItem {
  matchId: string
  opponentName: string
  result: string
  symbol: string
  playedAt: string
  isTournament: boolean
  tournamentName?: string | null
}

export interface MatchMove {
  moveNumber: number
  playerName: string
  symbol: string
  position: number
  movedAt: string
}

export interface OnlineCount {
  onlinePlayers: number
}

export interface SystemNotice {
  id: string
  title: string
  detail: string
}
