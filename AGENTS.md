
## Data Models

When updating a MongoDB data model (like `DbEntry` subclasses), always remember to update the corresponding `GetUpdateDefinition` method in the respective Service (e.g., `StationsService.cs`) to include the new fields via `.Set()`. If this is not done, the new fields will not be saved to the database during updates.
