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
