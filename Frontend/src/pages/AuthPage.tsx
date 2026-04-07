import { useState } from 'react'
import { api } from '../lib/api'
import type { UserProfile } from '../types'

interface AuthPageProps {
  onAuthenticated: (user: UserProfile) => void
}

export function AuthPage({ onAuthenticated }: AuthPageProps) {
  const [loginEmail, setLoginEmail] = useState('')
  const [loginPassword, setLoginPassword] = useState('')
  const [registerName, setRegisterName] = useState('')
  const [registerEmail, setRegisterEmail] = useState('')
  const [registerPassword, setRegisterPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function handleLogin() {
    if (!loginEmail.trim() || !loginPassword) {
      setError('Unesi email i lozinku za prijavu.')
      return
    }

    setBusy(true)
    setError('')

    try {
      const user = await api.login(loginEmail.trim(), loginPassword)
      onAuthenticated(user)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Login nije uspeo.')
    } finally {
      setBusy(false)
    }
  }

  async function handleRegister() {
    if (!registerName.trim() || !registerEmail.trim() || !registerPassword) {
      setError('Popuni korisnicko ime, email i lozinku za registraciju.')
      return
    }

    setBusy(true)
    setError('')

    try {
      const user = await api.register(
        registerName.trim(),
        registerEmail.trim(),
        registerPassword,
      )
      onAuthenticated(user)
    } catch (caughtError) {
      setError(caughtError instanceof Error ? caughtError.message : 'Registracija nije uspela.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="auth-shell">
      <section className="auth-hero">
        <span className="auth-hero__badge">Pulse Arena</span>
        <h1>Prijava u sistem</h1>
        <p>
          Pristupi mecevima, turnirima, prodavnici, rang listi i drustvenim funkcijama
          kroz jedan korisnicki nalog.
        </p>
      </section>

      <section className="auth-card">
        <div className="auth-card__grid">
          <div className="panel auth-panel">
            <header className="panel__header">
              <div>
                <span className="panel__eyebrow">Login</span>
                <h2>Prijavi se na postojeci nalog</h2>
              </div>
            </header>

            <label className="field">
              <span>Email</span>
              <input
                type="email"
                autoComplete="email"
                value={loginEmail}
                onChange={(event) => setLoginEmail(event.target.value)}
                placeholder="npr. igrac@arena.rs"
              />
            </label>

            <label className="field">
              <span>Lozinka</span>
              <input
                type="password"
                autoComplete="current-password"
                value={loginPassword}
                onChange={(event) => setLoginPassword(event.target.value)}
                placeholder="Unesi lozinku"
              />
            </label>

            <button
              className="button"
              disabled={busy || !loginEmail.trim() || !loginPassword}
              onClick={handleLogin}
            >
              {busy ? 'Prijava...' : 'Login'}
            </button>
          </div>

          <div className="panel auth-panel">
            <header className="panel__header">
              <div>
                <span className="panel__eyebrow">Registracija</span>
                <h2>Napravi novi nalog</h2>
              </div>
            </header>

            <label className="field">
              <span>Korisnicko ime</span>
              <input
                autoComplete="username"
                value={registerName}
                onChange={(event) => setRegisterName(event.target.value)}
                placeholder="npr. ArenaChampion"
              />
            </label>

            <label className="field">
              <span>Email</span>
              <input
                type="email"
                autoComplete="email"
                value={registerEmail}
                onChange={(event) => setRegisterEmail(event.target.value)}
                placeholder="npr. igrac@arena.rs"
              />
            </label>

            <label className="field">
              <span>Lozinka</span>
              <input
                type="password"
                autoComplete="new-password"
                value={registerPassword}
                onChange={(event) => setRegisterPassword(event.target.value)}
                placeholder="Najmanje 8 karaktera"
              />
            </label>

            <button className="button button--accent" disabled={busy} onClick={handleRegister}>
              {busy ? 'Kreiranje...' : 'Registruj korisnika'}
            </button>
          </div>
        </div>

        {error ? <p className="feedback feedback--error">{error}</p> : null}
      </section>
    </div>
  )
}
