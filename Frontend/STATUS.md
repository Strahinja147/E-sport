# Frontend Status

## Sta je uradjeno

- Napravljen je novi `React + TypeScript + Vite` frontend u folderu `Frontend/`.
- Dodati su `react-router-dom` i `@microsoft/signalr`.
- Postavljen je centralni API sloj u `src/lib/api.ts`.
- Postavljen je proxy ka backendu u `vite.config.ts` za:
  - `/api`
  - `/Game`
  - `/Matchmaking`
  - `/Inventory`
  - `/DatabaseTest`
  - `/gamehub`
- Implementirane su glavne stranice:
  - `AuthPage`
  - `DashboardPage`
  - `LeaderboardPage`
  - `MatchmakingPage`
  - `GamePage`
  - `ShopPage`
  - `TournamentPage`
  - `TeamsPage`
  - `SocialPage`
- Implementiran je osnovni layout u `src/components/AppShell.tsx`.
- Produkcioni build je uspesno prosao.

## Vazni fajlovi

- `src/App.tsx`
- `src/lib/api.ts`
- `src/types.ts`
- `src/components/AppShell.tsx`
- `src/pages/*`
- `vite.config.ts`
- `package.json`

## Kako se pokrece

### Backend

- Backend treba da radi na `http://localhost:5002`
- Ako koristis development profil iz .NET projekta, proveri da li je backend stvarno podignut na tom portu

### Frontend

Iz foldera `Frontend/`:

```bash
npm install
npm run dev
```

Za produkcioni build:

```bash
npm run build
```

## Sta trenutno frontend pokriva

- login i registraciju korisnika
- dashboard sa pregledom baze, online igraca, leaderboard-a, progresa, tima i inventara
- leaderboard
- matchmaking
- pokretanje i ucitavanje meca
- live tablu i chat preko SignalR-a i REST fallback-a
- shop i inventory
- revenue pregled
- turnire i generisanje bracket-a
- timove
- prijatelje i audit/progress pregled

## Poznate napomene

- Backend nema pravi auth sistem, pa frontend radi sa jednostavnim login/register tokom koji backend vec koristi.
- Za igru je izbor simbola `X/O` trenutno rucan na frontendu, jer backend ne vraca sigurnu mapu igrac-simbol.
- SignalR je povezan na `/gamehub`.
- Ako backend nije podignut, frontend ce se ucitati ali ce API pozivi padati.

## Sledeci preporuceni koraci

1. Pokrenuti backend i frontend zajedno i proci kroz sve glavne tokove.
2. Proveriti da li Redis, MongoDB i Cassandra rade lokalno kroz `docker-compose.yml`.
3. Doraditi backend za sigurnije game tokove:
   - validacija igraca po mecu
   - mapiranje igrac -> simbol
   - bolji response modeli za frontend
4. Dodati sitan UX polish:
   - loading stanja
   - prazna stanja
   - bolji error prikaz
5. Po potrebi dodati README za frontend ili deploy korake.

## Ako otvaras novi thread

U novom thread-u samo reci nesto poput:

`Procitaj Frontend/STATUS.md i nastavi odatle.`

To je najkraci nacin da se posao nastavi bez gubljenja konteksta.
