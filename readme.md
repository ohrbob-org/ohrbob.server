# Ohrbob

Ohrbob is a highly scalable, multi-profile podcast station manager and streaming control plane. It allows users to curate their own "radio stations" built from various podcast feeds and stream them across different devices, tracking their listen state.

## Terminology

*   **Feed:** A standard RSS/Atom podcast feed.
*   **Station:** A curated group of feeds combined with a specific playback strategy.
*   **Playback Strategy:** Determines how episodes from a station's feeds are queued:
    *   *Random:* Plays episodes from any feed in the station at random.
    *   *Chronological:* Plays all episodes in the station strictly based on their publish date.
    *   *Chronological Round-Robin:* Plays one episode per feed in chronological order, cycling through the feeds.
*   **Profile:** An Account can have multiple profiles (e.g., for a parent and a child). Each profile has its own assigned Stations, an independent listen state (playhead position), and a unique `StreamToken` used for playback authentication.
*   **Account:** The root identity of a user. Accounts are managed exclusively via OIDC (Nextcloud, Google, Apple ID). We do not store passwords.

## Architecture

Ohrbob is built using a microservice architecture with ASP.NET Core (.NET 10) and MongoDB.

*   **Ohrbob.Web:** The user-facing web portal. Built with ASP.NET Core MVC, Razor Pages, and Bootstrap 5. Users log in here via OIDC to manage their account, profiles, stations, and feeds.
*   **Ohrbob.Api (Admin API):** A REST API providing management endpoints for end-users and administrators. It handles CRUD operations for the core data models and executes background tasks (like polling feeds for new episodes).
*   **Ohrbob.Player:** A highly scalable, headless REST API that acts as the control plane for audio playback. Client applications (like mobile apps) use this API to fetch episode audio URLs and sync their playhead position. It authenticates via OIDC Bearer tokens and uses the `X-Stream-Token` header to identify the active profile.
*   **Ohrbob.Server.Shared:** A class library containing shared data models and DTOs used across the backend services.
*   **MongoDB:** The single source of truth database.

## Development Setup

The project includes a `docker-compose.yml` file to easily spin up the entire stack, including a local MongoDB instance.

### Prerequisites
*   Docker & Docker Compose
*   .NET 10 SDK
*   An IDE (Rider, Visual Studio, or VS Code)

### Running Locally

1. **Clone the repository.**
2. **Configure OIDC (Optional for basic API testing, required for Web Login):** Update the `appsettings.Development.json` in `Ohrbob.Web` and `Ohrbob.Player` with your OIDC provider details (e.g., Nextcloud authority and client secrets).
3. **Start the stack:**
   ```bash
   docker compose up --build
   ```
   *Alternatively, if using JetBrains Rider, you can simply run the `docker-compose.yml` run configuration directly from the IDE.*

### Exposed Services
When running via Docker Compose, the following services are exposed to your host machine:
*   **MongoDB:** `localhost:27017`
*   **Ohrbob.Api:** `localhost:5001`
*   **Ohrbob.Player:** `localhost:5002`
*   **Ohrbob.Web:** `localhost:5003`

You can navigate to `http://localhost:5003` in your browser to access the Web UI.

### Testing
The solution includes xUnit test projects for the various components. You can run them using the .NET CLI:
```bash
dotnet test
```
