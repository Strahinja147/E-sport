import { HttpTransportType, HubConnectionBuilder } from '@microsoft/signalr'
import { useEffect, useEffectEvent, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { SectionPanel } from '../components/SectionPanel'
import { api, resolveSignalRUrl } from '../lib/api'
import type { TicTacToeGame, TournamentDetails, UserProfile } from '../types'

interface GamePageProps {
  user: UserProfile
  onUserRefresh: () => Promise<void>
}

interface TournamentContext {
  tournamentName: string
  roundLabel: string
  opponentName: string
  status: string
}

export function GamePage({ user, onUserRefresh }: GamePageProps) {
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const [game, setGame] = useState<TicTacToeGame | null>(null)
  const [chat, setChat] = useState<string[]>([])
  const [chatInput, setChatInput] = useState('')
  const [gameStatus, setGameStatus] = useState('Ucitavanje aktivne partije...')
  const [connectionStatus, setConnectionStatus] = useState('Live veza nije aktivna.')
  const [tournamentContext, setTournamentContext] = useState<TournamentContext | null>(null)
  const [directOpponentName, setDirectOpponentName] = useState<string | null>(null)

  const matchId = searchParams.get('matchId') ?? ''

  const syncTournamentContext = useEffectEvent(async (nextGame: TicTacToeGame) => {
    if (!nextGame.tournamentId) {
      setTournamentContext(null)
      return
    }

    try {
      const details = await api.getTournamentDetails(nextGame.tournamentId)
      setTournamentContext(buildTournamentContext(details, nextGame, user))
    } catch {
      setTournamentContext((current) =>
        current
          ? {
              ...current,
              status: nextGame.status,
            }
          : null,
      )
    }
  })

  const syncDirectOpponent = useEffectEvent(async (nextGame: TicTacToeGame) => {
    if (nextGame.tournamentId) {
      setDirectOpponentName(null)
      return
    }

    try {
      const users = await api.getUsers()
      const opponentId = nextGame.player1Id === user.id ? nextGame.player2Id : nextGame.player1Id
      const opponent = users.find((candidate) => candidate.id === opponentId)
      setDirectOpponentName(opponent?.username ?? null)
    } catch {
      setDirectOpponentName(null)
    }
  })

  const handleReceiveMove = useEffectEvent((incomingGame: TicTacToeGame) => {
    setGame(incomingGame)
    const nextSymbol = resolvePlayerSymbol(incomingGame, user.id)
    const myTurn = nextSymbol === incomingGame.currentTurn
    setGameStatus(myTurn ? 'Ti si na potezu.' : 'Na potezu je protivnik.')
    void syncTournamentContext(incomingGame)
  })

  const handleGameFinished = useEffectEvent(async (message: string, board: string) => {
    let finishedGame: TicTacToeGame | null = null

    setGame((current) => {
      finishedGame = current
        ? {
            ...current,
            board,
            status: 'Finished',
          }
        : null

      return finishedGame
    })

    setTournamentContext((current) =>
      current
        ? {
            ...current,
            status: 'Finished',
          }
        : null,
    )
    setGameStatus(message)

    if (finishedGame) {
      void syncTournamentContext(finishedGame)
    }

    await onUserRefresh()
  })

  const handleReceiveMessage = useEffectEvent((username: string, message: string) => {
    setChat((current) => [`${username}: ${message}`, ...current].slice(0, 10))
  })

  useEffect(() => {
    if (!matchId) {
      navigate('/matchmaking', { replace: true })
      return
    }

    let active = true

    setConnectionStatus('Povezivanje na live mec...')
    void loadGame(matchId)
    void loadChat(matchId)

    const connection = new HubConnectionBuilder()
      .withUrl(resolveSignalRUrl('/gamehub'), {
        transport: HttpTransportType.LongPolling,
        withCredentials: false,
      })
      .withAutomaticReconnect()
      .build()

    connection.on('ReceiveMove', handleReceiveMove)
    connection.on('GameFinished', handleGameFinished)
    connection.on('ReceiveMessage', handleReceiveMessage)

    void connection
      .start()
      .then(async () => {
        if (!active) {
          return
        }

        setConnectionStatus('Live veza je aktivna.')
        await connection.invoke('JoinMatch', matchId)
      })
      .catch(() => {
        if (!active) {
          return
        }

        setConnectionStatus('Live sinhronizacija trenutno nije dostupna, radicemo preko osvezavanja.')
      })

    return () => {
      active = false
      connection.off('ReceiveMove', handleReceiveMove)
      connection.off('GameFinished', handleGameFinished)
      connection.off('ReceiveMessage', handleReceiveMessage)
      void connection.stop()
    }
  }, [matchId, navigate])

  useEffect(() => {
    if (!matchId) {
      return
    }

    let active = true

    const ensureCurrentMatchIsAllowed = async () => {
      try {
        const result = await api.checkMatchmaking(user.id)
        if (!active || !('matchId' in result)) {
          return
        }

        if (result.matchId !== matchId) {
          setGameStatus('Vec imas drugi aktivan mec. Prebacujemo te na njega.')
          navigate(`/game?matchId=${result.matchId}`, { replace: true })
        }
      } catch {
        // Ne prekidamo prikaz ako provera povremeno omane.
      }
    }

    void ensureCurrentMatchIsAllowed()
    const interval = window.setInterval(() => {
      void ensureCurrentMatchIsAllowed()
    }, 3000)

    return () => {
      active = false
      window.clearInterval(interval)
    }
  }, [matchId, navigate, user.id])

  useEffect(() => {
    if (!matchId) {
      return
    }

    const interval = window.setInterval(() => {
      void refreshMoveState(matchId)
    }, 1500)

    return () => {
      window.clearInterval(interval)
    }
  }, [matchId, user.id])

  async function loadGame(nextMatchId = matchId) {
    try {
      const loadedGame = await api.getGameState(nextMatchId)
      setGame(loadedGame)
      setGameStatus(resolveTurnStatus(loadedGame, user.id))
      await syncTournamentContext(loadedGame)
      await syncDirectOpponent(loadedGame)
    } catch (caughtError) {
      setGame(null)
      setTournamentContext(null)
      setDirectOpponentName(null)
      setGameStatus(caughtError instanceof Error ? caughtError.message : 'Mec nije ucitan.')
    }
  }

  async function refreshMoveState(nextMatchId = matchId) {
    try {
      const latestGame = await api.getMove(nextMatchId)
      let changed = false

      setGame((current) => {
        if (!current || current.version !== latestGame.version || current.board !== latestGame.board) {
          setGameStatus(resolveTurnStatus(latestGame, user.id))
          changed = true
          return latestGame
        }

        return current
      })

      if (changed || latestGame.status !== tournamentContext?.status) {
        await syncTournamentContext(latestGame)
      }

      if (changed) {
        await syncDirectOpponent(latestGame)
      }
    } catch {
      // Tihi fallback polling, ne menjamo status ako samo povremeno omane.
    }
  }

  async function loadChat(nextMatchId = matchId) {
    try {
      const history = await api.getMatchChat(nextMatchId)
      setChat(history)
    } catch {
      setChat([])
    }
  }

  async function submitMove(position: number) {
    if (!matchId || !game) {
      return
    }

    const mySymbol = resolvePlayerSymbol(game, user.id)
    if (!mySymbol) {
      setGameStatus('Ovaj nalog nije ucesnik meca.')
      return
    }

    if (game.currentTurn !== mySymbol) {
      setGameStatus('Na potezu je protivnik.')
      return
    }

    try {
      const response = await api.makeMove(matchId, user.id, position, mySymbol, game.version)
      setGameStatus(response.message)
      await loadGame(matchId)
      await onUserRefresh()
    } catch (caughtError) {
      setGameStatus(caughtError instanceof Error ? caughtError.message : 'Potez nije uspeo.')
    }
  }

  async function sendChat() {
    if (!matchId || !chatInput.trim()) {
      return
    }

    try {
      await api.sendChatMessage(matchId, user.id, chatInput.trim())
      setChatInput('')
      await loadChat(matchId)
    } catch (caughtError) {
      setGameStatus(caughtError instanceof Error ? caughtError.message : 'Chat poruka nije poslata.')
    }
  }

  const cells = game?.board?.split('') ?? Array.from({ length: 9 }, () => '_')
  const mySymbol = game ? resolvePlayerSymbol(game, user.id) : null
  const isMyTurn = !!game && !!mySymbol && game.currentTurn === mySymbol && game.status === 'InProgress'

  return (
    <>
      <SectionPanel title="Mec u toku" eyebrow={tournamentContext ? 'Turnirski mec' : 'Direktan mec'}>
        {tournamentContext ? (
          <div className="dual-grid">
            <div className="nested-card">
              <h3>{tournamentContext.tournamentName}</h3>
              <span>{tournamentContext.roundLabel}</span>
              <span>Protivnik: {tournamentContext.opponentName}</span>
              <span>Status: {tournamentContext.status}</span>
            </div>
            <div className="nested-card">
              <h3>Tvoj prikaz</h3>
              <span>Igras kao: {mySymbol ?? '-'}</span>
              <span>{isMyTurn ? 'Ti si na potezu.' : 'Na potezu je protivnik.'}</span>
              <span>Stanje meca: {game?.status ?? '-'}</span>
            </div>
          </div>
        ) : (
          <div className="nested-card">
            <h3>Detalji meca</h3>
            <span>Protivnik: {directOpponentName ?? '-'}</span>
            <span>Igras kao: {mySymbol ?? '-'}</span>
            <span>{isMyTurn ? 'Ti si na potezu.' : 'Na potezu je protivnik.'}</span>
            <span>Stanje meca: {game?.status ?? '-'}</span>
          </div>
        )}

        <div className="list-stack">
          <p className="status-banner">{gameStatus}</p>
          <p className="status-banner">{connectionStatus}</p>
        </div>
      </SectionPanel>

      <SectionPanel title="Live tabla">
        <div className="game-layout">
          <div className="board">
            {cells.map((cell, index) => (
              <button
                key={`${cell}-${index}`}
                className="board__cell"
                disabled={!matchId || cell !== '_' || !isMyTurn}
                onClick={() => submitMove(index)}
              >
                {cell === '_' ? '' : cell}
              </button>
            ))}
          </div>

          <div className="game-meta">
            <div className="nested-card">
              <h3>Status</h3>
              <span>Na potezu: {game?.currentTurn ?? '-'}</span>
              <span>Tvoj simbol: {mySymbol ?? '-'}</span>
              <span>Verzija: {game?.version ?? '-'}</span>
              <span>Stanje: {game?.status ?? '-'}</span>
            </div>

            <div className="nested-card">
              <h3>Chat</h3>
              <div className="chat-box">
                {chat.length > 0 ? (
                  chat.map((message, index) => <p key={`${message}-${index}`}>{message}</p>)
                ) : (
                  <p className="muted">Chat za ovaj mec je prazan.</p>
                )}
              </div>
              <div className="toolbar-inline">
                <input
                  value={chatInput}
                  onChange={(event) => setChatInput(event.target.value)}
                  placeholder="Upisi poruku"
                />
                <button className="button" onClick={sendChat}>
                  Posalji
                </button>
              </div>
            </div>
          </div>
        </div>
      </SectionPanel>
    </>
  )
}

function resolvePlayerSymbol(game: TicTacToeGame, userId: string) {
  if (game.player1Id === userId) {
    return 'X'
  }

  if (game.player2Id === userId) {
    return 'O'
  }

  return null
}

function resolveTurnStatus(game: TicTacToeGame, userId: string) {
  const mySymbol = resolvePlayerSymbol(game, userId)

  if (!mySymbol) {
    return 'Ovaj nalog nije ucesnik meca.'
  }

  if (game.status !== 'InProgress') {
    return `Mec je zavrsen.`
  }

  return game.currentTurn === mySymbol ? 'Ti si na potezu.' : 'Na potezu je protivnik.'
}

function buildTournamentContext(details: TournamentDetails, game: TicTacToeGame, user: UserProfile): TournamentContext {
  for (const round of details.rounds) {
    const match = round.matches.find((candidate) => candidate.id === game.id)
    if (!match) {
      continue
    }

    const opponent =
      match.player1?.id === user.id ? match.player2?.username : match.player1?.username

    return {
      tournamentName: details.name,
      roundLabel: `Runda ${round.roundNumber}`,
      opponentName: opponent ?? 'Ceka se protivnik',
      status: match.status,
    }
  }

  return {
    tournamentName: details.name,
    roundLabel: 'Turnirski mec',
    opponentName: 'Nepoznat protivnik',
    status: game.status,
  }
}
