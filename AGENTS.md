
## Data Models

When updating a MongoDB data model (like `DbEntry` subclasses), always remember to update the corresponding `GetUpdateDefinition` method in the respective Service (e.g., `StationsService.cs`) to include the new fields via `.Set()`. If this is not done, the new fields will not be saved to the database during updates.

## Testing

- **API Routes:** When implementing a new API route, always write unit tests for coverage.
- **UI Functionality:** When implementing UI functionality, write an E2E test. For E2E tests, you can use `docker-compose up -d --build` to build and spin up the full environment to test against.
