# E-Sport Platform

Web aplikacija za e-sport takmicenja sa backend delom u `ASP.NET Core`, frontend delom u `React + TypeScript` i bazama `MongoDB`, `Redis` i `Cassandra`.

## Tehnologije

- Backend: `.NET 8`, `ASP.NET Core Web API`, `SignalR`
- Frontend: `React`, `TypeScript`, `Vite`
- Baze i servisi: `MongoDB`, `Redis`, `Cassandra`
- Kontejneri: `Docker Compose`

## Preduslovi

Pre pokretanja projekta potrebno je da budu instalirani:

- `Docker` sa podrskom za `docker compose`
- `.NET 8 SDK`
- `Node.js` i `npm`

## Brzo pokretanje

### 1. Pokretanje baza preko Docker-a

Iz korena projekta pokrenuti:

```powershell
docker compose up -d
```

Provera da li su kontejneri podignuti:

```powershell
docker ps
```

Ocekivani kontejneri:

- `esports_mongo`
- `esports_redis`
- `esports_cassandra`

### 2. Inicijalizacija Mongo replica set-a

Backend koristi Mongo konekciju sa `replicaSet=rs0`, pa je ovaj korak obavezan posle prvog pokretanja kontejnera.

Pokrenuti:

```powershell
docker exec -it esports_mongo mongosh --eval "rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'localhost:27017' }] })"
```

Ako je replica set vec inicijalizovan, komanda moze vratiti poruku da je vec podesen. To je u redu.

Opciona provera:

```powershell
docker exec esports_mongo mongosh --eval "rs.status()"
```

### 3. Pokretanje backend-a

Otvoriti folder:

```powershell
cd Backend/EsportApi/EsportApi
```

Restore paketa:

```powershell
dotnet restore
```

Pokretanje backend-a:

```powershell
dotnet run --launch-profile https
```

Backend adrese:

- `https://localhost:7109`
- `http://localhost:5002`

Swagger:

- [https://localhost:7109/swagger](https://localhost:7109/swagger)

Ako browser trazi potvrdu za lokalni HTTPS sertifikat, prihvatiti upozorenje.

### 4. Pokretanje frontend-a

Otvoriti novi terminal i uci u frontend:

```powershell
cd Frontend
```

Instalacija paketa:

```powershell
npm install
```

Pokretanje razvojnog servera:

```powershell
npm run dev
```

Frontend se podize na adresi slicnoj:

- `http://localhost:5173`

## Redosled pokretanja

Preporuceni redosled:

1. `docker compose up -d`
2. `rs.initiate(...)` za Mongo
3. `dotnet run --launch-profile https`
4. `npm install`
5. `npm run dev`

## Demo podaci

Pri pokretanju backend-a aplikacija automatski dodaje mali skup demo podataka ako oni ne postoje:

- 4 demo korisnika
- nekoliko prihvacenih prijateljstava
- 1 demo tim i 1 aktivan timski poziv
- shop artikle
- pocetni inventar i kupovine
- login istoriju
- ELO progres kroz vreme
- istoriju vise gotovih meceva sa potezima
- inicijalni leaderboard

### Demo nalozi

Lozinka za sve demo naloge:

```text
Demo123!
```

Nalozi:

- `paja@demo.local`
- `luka@demo.local`
- `strale@demo.local`
- `mika@demo.local`

## Kratki scenariji za testiranje

### Osnovni pregled aplikacije

1. Ulogovati se kao `paja@demo.local`
2. Otvoriti `Dashboard`
3. Proveriti leaderboard, ELO grafik, istoriju meceva i poslednje kupovine

### Drustvo

1. Ulogovati se kao `paja@demo.local`
2. Otvoriti `Drustvo`
3. Proveriti postojece prijatelje i online status
4. Poslati novi zahtev za prijateljstvo nekom od preostalih demo naloga

### Timovi

1. Ulogovati se kao `paja@demo.local`
2. Otvoriti `Timovi`
3. Proveriti demo tim `Night Falcons`
4. Poslati timski poziv prijatelju iz dropdown-a ili drugom igracu preko `username`

### Shop i inventar

1. Ulogovati se kao `paja@demo.local`
2. Otvoriti `Shop & Inventory`
3. Proveriti da korisnik vec ima nekoliko `coins`
4. Kupiti novi predmet ili prodati postojeci skin

### Obican matchmaking

1. Pokrenuti frontend u dva browser prozora ili na dva porta
2. Ulogovati se kao dva razlicita demo korisnika
3. Otvoriti `Matchmaking`
4. Uci u red sa oba korisnika
5. Proveriti automatski redirect na mec

### Turnir

1. Pokrenuti 4 odvojena login-a sa demo nalozima
2. `paja@demo.local`
3. `luka@demo.local`
4. `strale@demo.local`
5. `mika@demo.local`
6. Sa sva 4 korisnika otvoriti `Matchmaking`
7. Kliknuti `Prijavi se za turnirski red`
8. Proveriti automatsko formiranje turnira i redirect na meceve

## Korisne komande

### Zaustavljanje Docker servisa

```powershell
docker compose down
```

### Zaustavljanje i brisanje volumena

```powershell
docker compose down -v
```

Napomena:

Ova komanda brise lokalne podatke iz MongoDB i Cassandre.

### Ponovno podizanje samo backend-a

```powershell
cd Backend/EsportApi/EsportApi
dotnet run --launch-profile https
```

### Ponovno podizanje samo frontend-a

```powershell
cd Frontend
npm run dev
```

## Najcesci problemi

### Mongo replica set nije inicijalizovan

Simptomi:

- backend ne moze da se poveze na Mongo
- transakcije ne rade

Resenje:

Pokrenuti ponovo:

```powershell
docker exec -it esports_mongo mongosh --eval "rs.initiate({ _id: 'rs0', members: [{ _id: 0, host: 'localhost:27017' }] })"
```

### Frontend ne moze da pristupi backend-u

Proveriti:

- da backend radi na `https://localhost:7109`
- da je lokalni HTTPS sertifikat prihvacen u browser-u
- da je frontend podignut preko `npm run dev`

### Port je vec zauzet

Proveriti da li vec postoji pokrenuta instanca backend-a ili frontend-a i ugasiti je pre novog pokretanja.

## Struktura projekta

- [Backend](/C:/Users/pajce/Documents/Esport%20baze/E-sport/Backend)
- [Frontend](/C:/Users/pajce/Documents/Esport%20baze/E-sport/Frontend)
- [docker-compose.yml](/C:/Users/pajce/Documents/Esport%20baze/E-sport/docker-compose.yml)
