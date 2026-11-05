# Ohrbob

Ohrbob is a highly scalable, multi-profile podcast station manager and streaming control plane. It allows users to curate their own "radio stations" built from various podcast feeds and stream them across different devices, tracking their listen state.

## Current status

Currently the service on https://ohrbob.org is in beta - it is a hobby project, it might be we have to drop the database, but I am trying my (almost) best to keep you going ;-). 

For now, I am trying to make the current implementation resilient and fix any bugs and basic UX issues. Further implementation is going to happen in the meantime.  

## 🚀 Roadmap & Todo List

I'm working on bringing cross-platform apps and direct network streaming to the project. Here is what's coming:

### 1. Multi-Platform Apps (Avalonia UI)
Unifying mobile and desktop apps into a single codebase.

* [ ] **Core Setup:** Cross-platform solution (Android, iOS, Windows, macOS, Linux) with shared C# MVVM logic.
* [ ] **Adaptive UI:** Responsive touch and desktop interfaces with native platform integrations.
* [ ] **CI/CD:** Automated builds for mobile and desktop binaries.

> **Why Avalonia?** Maximum C# code reuse with near-native performance across all platforms.

### 2. Streaming to Wi-Fi Radios & Clients
Broadcasting streams directly to external hardware receivers.

* [ ] **FFmpeg Engine:** Live audio transcoding and direct broadcasting pipeline.
* [ ] **Scalable Backend:** Infrastructure to handle multi-session FFmpeg streaming.
* [ ] **Web Control Interface:** Built-in web interface to control and manage radio streams from any browser.
* [ ] **Device Discovery:** Auto-detect network radios + remote playback control.

> **Why FFmpeg & Web Interface?** Ensures broad stream compatibility across hardware receivers while allowing convenient playback management from any web browser.

### 3. Premium Tier (Infrastructure Costs)
Sustaining cloud compute costs for live transcoding.

* [ ] **Free Tier:** Core app features, local playback, capped feeds & stations.
* [ ] **Premium Tier:** Unlimited Stations, unlimited Feeds, and hosted FFmpeg live transcoding.
* [ ] **Self-Hosting:** Complete open-source self-hosting options remain fully available.

> **Why Premium?** Live FFmpeg transcoding generates ongoing cloud compute costs. This tier keeps the infrastructure self-sustaining while keeping the core open source.

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

*   **Ohrbob.Web:** The user-facing web portal. Built with ASP.NET Core MVC, Razor Pages, Bootstrap 5, and HTMX (for seamless, single-page-like navigation and uninterrupted audio playback). Users log in here via OIDC to manage their account, profiles, stations, and feeds. It also proxies PodcastIndex search and talks to `Ohrbob.Api` server-side.
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
git clone https://github.com/ohrbob-org/ohrbob.server ohrbob2
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
| `Oidc__Google__*`, `Oidc__Nextcloud__*`, `Oidc__GitHub__*` | Fill in the login provider(s) you want — at least one is required. Unused providers can stay empty: their login button is hidden. For GitHub, create an OAuth App and set the callback URL to `https://<your-host>/signin-github`. |
| `Legal__*` | Set `Legal__Show=true` plus your name/address/email if you need the legal notice (Impressum) page. |
| `PodcastIndex__ApiKey` / `PodcastIndex__ApiSecret` | Required for podcast search/discovery in the Web UI. |
| `Discover__LaneCategories` | Comma-separated Discover recommendation lanes (default: `Comedy,News,True Crime,Business,Health & Fitness`). |
| `*_PORT`, `MongoDb*` | Only change if the defaults clash with ports on your machine. |

### 4. Build and start the stack
```bash
docker compose up --build -d
docker compose logs -f   # optional: follow startup logs
```

### 5. Open the app
Navigate to `http://localhost:5003` in your browser to access the Web UI.

### Prebuilt Docker images

If you don't want to build from source, released images are published on [Docker Hub](https://hub.docker.com/u/kaiwegner):

| Image | Purpose |
|---|---|
| `kaiwegner/ohrbob.web` | Web portal (the UI) |
| `kaiwegner/ohrbob.api` | Admin/management REST API |
| `kaiwegner/ohrbob.player` | Playback control plane and `/stream/*` audio endpoints |
| `kaiwegner/ohrbob.stream` | Streaming component |
| `kaiwegner/ohrbob.testcasts` | Throwaway fake podcast host, used only by the E2E playback tests |

Tags follow the release versions (`0.2.1`, `0.2.0`, …), and `latest` tracks the newest build. To run the published images instead of building locally, use the example compose file with a tag:

```bash
RELEASE_TAG=0.2.1 docker compose -f docker-compose-example.yml up -d
```

To update an existing deployment: `docker compose -f docker-compose-example.yml pull && docker compose -f docker-compose-example.yml up -d`.

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


## 🤝 How to Contribute
Pull requests and feedback are welcome! Report bugs and request features in the [issue tracker](https://github.com/ohrbob-org/ohrbob.server/issues), or pick up an open [`help-wanted` issue](https://github.com/ohrbob-org/ohrbob.server/issues?q=is%3Aissue+is%3Aopen+label%3Ahelp-wanted) if you'd like to jump in.

## 📄 License
Ohrbob is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License as published by the Free Software Foundation, either version 3 of the License, or (at your option) any later version.

See [LICENSE](LICENSE) for the full text.

Copyright (C) 2026 Kai Wegner
