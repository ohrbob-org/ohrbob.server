# Contributing to Ohrbob

Thanks for wanting to help! This project is licensed under the [GNU GPL v3 or later](LICENSE), and by contributing you agree to release your work under the same terms. Please also read our [Code of Conduct](CODE_OF_CONDUCT.md).

## Getting started

- **Run the stack:** see [Installation](readme.md#installation-local-with-docker-compose). `cp .env-example .env`, fill in the values, then `docker compose up --build -d`.
- **Without Docker:** install the .NET 10 SDK and point the services at a local MongoDB (see [Development](readme.md#development-without-docker)).

## Build and test

```bash
dotnet build ohrbob2.slnx
dotnet test                          # unit tests
docker compose up -d --build         # needed for the E2E tests
```

The E2E tests talk to the `testcasts` service from `docker-compose.yml`. It is pulled from Docker Hub (`kaiwegner/ohrbob.testcasts`) — its source lives outside this repository. Override `TESTCASTS_IMAGE` if you want to build it yourself, and run the tests with `OHRBOB_TESTCASTS_FEED_URL=http://testcasts:5004`.

## Project conventions

Read [`AGENTS.md`](AGENTS.md) first — it documents the data-model, UI, and testing rules the codebase follows. The short version:

- **Tests are expected.** New API routes get unit tests, new UI behaviour gets an E2E test.
- **Schema changes ship as a migration** in `Ohrbob.Api/Migrations/`, registered in `Program.cs`.
- **UI changes must not interrupt audio playback** and must survive browser back/forward navigation.
- **Never commit secrets.** Configuration belongs in `.env`; `.env-example` only ever holds placeholders.
- **JavaScript lives in `Ohrbob.Web/wwwroot/js`**, not in Razor views.
- Include the tests and fixtures your change needs — a PR that breaks existing tests is not ready.

## Commits and pull requests

Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/) with a scope where it helps, matching the existing history:

```
feat(web): show host pace badge on station surfaces
fix(api): prevent duplicate StreamToken when creating a profile
test(e2e): verify host pace for subscribers
```

A pull request should contain one logical change, a short description of *why*, and the tests that prove it works. Please keep unrelated refactors out of the same PR.

## Reporting bugs and ideas

Use the issue tracker of the repository you are working in. Include what you did, what you expected, and what happened, plus logs or a screenshot where useful. For security issues, follow [SECURITY.md](SECURITY.md) instead of opening a public issue.
