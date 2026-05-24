# EReader — Claude Context

## Stack

| Layer | Technology |
|---|---|
| Backend API | .NET 10 Web API |
| ORM | EF Core 10 |
| Database | PostgreSQL 16 (Docker) |
| Frontend | React Native Web (Expo) |
| Language | C# / TypeScript (strict) |

## Project Structure

```
ereader/
├── backend/
│   ├── EReader.Api/
│   │   └── Controllers/      # Thin HTTP controllers only — no business logic
│   ├── EReader.Core/
│   │   ├── Interfaces/       # Service interfaces (register DI by interface)
│   │   ├── Models/           # Domain models
│   │   └── Services/         # Business logic implementations
│   ├── EReader.Data/
│   │   ├── Migrations/       # EF Core migrations
│   │   └── Repositories/     # Data access — DB calls live here, not in services
│   └── EReader.Tests/        # xUnit tests
├── frontend/
│   └── src/
│       ├── components/       # Shared UI components
│       ├── hooks/            # Custom hooks — data fetching logic lives here
│       ├── screens/          # Screen-level components
│       ├── services/         # API client (axios instance, endpoint wrappers)
│       └── types/            # Shared TypeScript types (single-use types stay co-located)
├── docker/
│   ├── docker-compose.yml    # PostgreSQL + pgAdmin
│   └── postgres/init.sql
├── scripts/
│   └── download-test-books.sh
├── .env.example              # All required env vars documented here
└── best-practices.md         # Coding standards for this project — read this first
```

## Git Policy

**Claude is NEVER allowed to commit to this repository.**

Claude may stage files and draft a commit message, but must stop there. The human reviews the staged changes and runs `git commit` manually.

## Coding Standards

**Always read [best-practices.md](best-practices.md) before writing code for this project.**

Key rules enforced here:

- All API routes are prefixed `/api/v1/...`
- All error responses use `{ error: { code, message, details } }`
- Controllers are thin: validate input → call service → map to HTTP response
- All async methods are truly async (no `.Result` / `.Wait()`); `async void` is banned
- Dependencies are registered by interface, not concrete type
- No `any` in TypeScript — use `unknown` when shape is truly unknown
- Data fetching logic lives in `hooks/`, not inside component bodies
- Tests follow `Should_[Result]_When_[Condition]` naming and are fully self-contained

## Dev Setup

```bash
# Database
docker compose -f docker/docker-compose.yml up -d

# Backend
cd backend/EReader.Api && dotnet run

# Frontend
cd frontend && npx expo start --web
```

Copy `.env.example` → `.env` before running the backend.