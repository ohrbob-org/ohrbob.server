# Ohrbob

Ohrbob is a highly scalable, multi-profile podcast station manager and streaming control plane. It allows users to curate their own "radio stations" built from various podcast feeds and stream them across different devices, tracking their listen state.

## Motivation

I have been using open-source software for so long, I want to give something back.

When Maus Radio by WDR was switched to live radio in the morning hours, its purpose of waking my children was no longer working — maybe it was their tiredness after the summer holidays, but I think it is the radio programming. So I decided to take it into my own hands: build a system that I can use to program my own radio stations.

And why not open that up to others.

## Terminology

*   **Feed:** A standard RSS/Atom podcast feed.
*   **Station:** A curated group of feeds combined with a specific playback strategy.
*   **Playback Strategy:** Determines how episodes from a station's feeds are queued:
    *   *Random:* Plays episodes from any feed in the station at random.
    *   *Chronological:* Plays all episodes in the station strictly based on their publish date.
    *   *Chronological Round-Robin:* Plays one episode per feed in chronological order, cycling through the feeds.
*   **Profile:** An Account can have multiple profiles (e.g., for a parent and a child). Each profile has its own assigned Stations (`AllowedStationIds`), an independent listen state (playhead position per episode), last-active station/episode pointers (`LastActiveStationId`, `LastActiveEpisodeId`), and a unique `StreamToken` used for playback authentication.
*   **Account:** The root identity of a user. Accounts are managed exclusively via external providers (Nextcloud and Google via OIDC, GitHub via OAuth). We do not store passwords.

## Architecture

Ohrbob is built using a microservice architecture with ASP.NET Core (.NET 10) and MongoDB.

*   **Ohrbob.Web:** The user-facing web portal. Built with ASP.NET Core MVC, Razor Pages, and Bootstrap 5. Users log in here via OIDC to manage their account, profiles, stations, and feeds. It also proxies PodcastIndex search and talks to `Ohrbob.Api` server-side.
*   **Ohrbob.Api (Admin API):** A REST API providing management endpoints for end-users and administrators. It handles CRUD operations for the core data models and executes background tasks (like polling feeds for new episodes).
*   **Ohrbob.Player:** A highly scalable, headless REST API that acts as the control plane for audio playback. Client applications (like mobile apps) use this API to fetch episode audio URLs and sync their playhead position. The `/stream/*` endpoints authenticate via the profile `StreamToken` (as `Authorization: Bearer <token>` or `X-Stream-Token` header); the `/api/playback/*` endpoints authenticate via OIDC Bearer token plus the `X-Stream-Token` header to identify the active profile.
*   **Ohrbob.Server.Shared:** A class library containing shared data models and DTOs used across the backend services.
*   **MongoDB:** The single source of truth database.

## Installation (local, with Docker Compose)

The recommended way to run Ohrbob locally is via Docker Compose. It starts MongoDB plus the three services (Api, Player, Web) with a single command. No .NET SDK needed for this path.

### Prerequisites
*   Docker & Docker Compose (Docker Desktop, or Docker Engine + Compose plugin)
*   Git

### 1. Clone the repository
```bash
git clone <your-repo-url> ohrbob2
cd ohrbob2
```

### 2. Create your environment file
All configuration lives in one top-level `.env` file. A commented template is provided as `.env-example`:

```bash
cp .env-example .env
```

Docker Compose loads `.env` automatically and distributes the values to each container (see `env_file` / `environment` in `docker-compose.yml`).

### 3. Adjust the values in `.env`
At minimum, review these entries (everything else works with the defaults):

| Variable | What to do |
|---|---|
| `ApiKeys__0__Key` / `OhrbobApi__ApiKey` | Set both to the same long random string (the Web portal uses it to call the Api). Generate one e.g. with `openssl rand -hex 32`. |
| `Oidc__Google__*`, `Oidc__Nextcloud__*`, `Oidc__GitHub__*` | Fill in the login provider(s) you want. Unused providers can stay empty — that login button is then disabled. For GitHub, create an OAuth App and set the callback URL to `https://<your-host>/signin-github`. |
| `Legal__*` | Set `Legal__Show=true` plus your name/address/email if you need the legal notice (Impressum) page. |
| `PodcastIndex__ApiKey` / `PodcastIndex__ApiSecret` | Required for podcast search/discovery in the Web UI. |
| `*_PORT`, `MongoDb*` | Only change if the defaults clash with ports on your machine. |

### 4. Build and start the stack
```bash
docker compose up --build -d
docker compose logs -f   # optional: follow startup logs
```

### 5. Open the app
Navigate to `http://localhost:5003` in your browser to access the Web UI.

### Exposed Services
When running via Docker Compose, the following services are exposed to your host machine:
*   **MongoDB:** `localhost:27019` (mapped to container `27017`)
*   **Ohrbob.Api:** `localhost:5001`
*   **Ohrbob.Player:** `localhost:5002`
*   **Ohrbob.Web:** `localhost:5003`

### Updating / stopping
```bash
docker compose pull && docker compose up --build -d   # update to latest images/code
docker compose down        # stop (keeps data in the mongodb_data volume)
docker compose down -v     # stop AND delete all data
```

## Development (without Docker)

You only need this if you want to hack on the C# code directly.

*   Install the .NET 10 SDK.
*   Start a MongoDB you can reach from the host, then export the `localhost` equivalents documented at the top of `.env-example` (e.g. `MongoDbData__ConnectionString=mongodb://localhost:27017/OhrbobData`, `OhrbobApi__BaseUrl=http://localhost:5001/api`, `OhrbobApi__PlayerApiUrl=http://localhost:5002`) — the Docker-internal hostnames (`mongodb`, `ohrbob.api`, `ohrbob.player`) only resolve inside the Compose network.
*   Run services with `dotnet run` per project, or run `dotnet test` for the xUnit suites.
*   Populate the appsettings.json files with the content you would put into .env 