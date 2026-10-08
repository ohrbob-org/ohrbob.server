
## Data Models

- **Update Definitions:** When updating a MongoDB data model (like `DbEntry` subclasses), always remember to update the corresponding `GetUpdateDefinition` method in the respective Service (e.g., `StationsService.cs`) to include the new fields via `.Set()`. If this is not done, the new fields will not be saved to the database during updates.
- **Deletions and References:** When an entry in the database (model) is deleted, it is generally marked as deleted by setting `DeletedAt` (since they all derive from `DbEntry`). When an object in the database references another object and it gets deleted, the references need to be updated. For example: if a `Station` is deleted, then `Profile`s referencing that station need to remove the station reference.

## UI and Frontend (Ohrbob.Web)

- **Immediate Persistence:** When a user interacts with a UI button (like delete, edit, or add), the change should be immediately sent to the API and persisted. If the request succeeds, the UI must be updated accordingly to reflect the change.
- **Uninterrupted Playback:** During any activity or navigation in the UI, the playback in the `audio-widget` should never be interrupted. (e.g., use HTMX or AJAX to prevent full page reloads).
- **Navigating Back:** Users use the navigate back function in browsers frequently. when we enter a view we should be able to navigate back via the browser and the ui should not break. 
- **Background Media Session API:** For any audio playback functionality, ensure integration with the browser's Media Session API (`navigator.mediaSession`). This allows users to control playback (play, pause, seek, skip) and view track metadata (title, artist, artwork) through system-level background media controls, lock screens, and hardware media keys.

## Testing

- **API Routes:** When implementing a new API route, always write unit tests for coverage.
- **UI Functionality:** When implementing UI functionality, write an E2E test. For E2E tests, you can use `docker-compose up -d --build` to build and spin up the full environment to test against.
- **Docker:** Use Docker and `docker compose` in general to build and test the projects. The environment variables required are already present in the top-level `.env` file. When debugging E2E tests, it's highly recommended to run `docker compose up -d --build` and test against those containers, as it closely matches the production deployment.