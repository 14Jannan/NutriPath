# NutriPath

AI-assisted, real-world-data-backed nutrition tracker. React Native (Expo)
frontend, ASP.NET Core backend, PostgreSQL, real nutrition data from USDA
FoodData Central and Open Food Facts plus a curated Sri Lankan food set.

## Structure

- `frontend/` — Expo/React Native (TypeScript) app
- `backend/` — ASP.NET Core Web API (.NET 10) + EF Core + PostgreSQL
- `tests/` — xUnit unit and integration tests
- `data/ingestion/` — food data sync scripts and seed files
- `docs/` — architecture notes, ERD, phase-by-phase build log
- `infrastructure/docker/` — Dockerfiles and docker-compose

## Status

Under active phase-by-phase development. See `docs/phase-0-architecture.md`.