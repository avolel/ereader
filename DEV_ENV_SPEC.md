# Dev Environment Setup Spec — EPUB Ebook Reader

**Phase 0: Environment & Test Content**
*Derived from BRD v1.0 — Coding Challenge #109*

---

## 1. Overview

This spec covers everything needed to stand up a local development environment for the ebook reader project. The target stack is:

| Layer | Technology | Version |
|---|---|---|
| Backend API | .NET Core | 10 |
| ORM | EF Core | 10 |
| Database | PostgreSQL | 16+ |
| Frontend | React Native Web | Latest stable |
| Runtime | Node.js | 22 LTS |
| Package manager | npm / yarn | Latest |
| Container runtime | Docker + Docker Compose | Latest stable |

---

## 2. Prerequisites

### 2.1 Required Tools

Install all tools before proceeding. Minimum versions are enforced where noted.

| Tool | Minimum Version | Install |
|---|---|---|
| .NET SDK | 10.0 | https://dotnet.microsoft.com/download |
| Node.js | 22 LTS | https://nodejs.org (or `nvm`) |
| Docker Desktop | 4.x | https://www.docker.com/products/docker-desktop |
| Docker Compose | v2 (bundled with Docker Desktop) | — |
| Git | 2.40+ | https://git-scm.com |
| PostgreSQL client (`psql`) | 16+ | via OS package manager or bundled with Postgres |

### 2.2 Recommended IDE / Editor

- **VS Code** with the extensions listed in Section 7, or
- **JetBrains Rider** (backend) + **WebStorm** (frontend)

### 2.3 Platform Notes

| OS | Notes |
|---|---|
| macOS | Use Homebrew for `node`, `dotnet`, `postgresql` client |
| Linux (Ubuntu/Debian) | Use `apt` for postgres client; install .NET via Microsoft feed |
| Windows | Use WSL 2 + Docker Desktop with WSL 2 backend |

---

## 3. Repository Structure

```
ereader/
├── backend/                  # .NET Core 10 Web API
│   ├── EReader.Api/          # API project
│   ├── EReader.Core/         # Domain logic, EPUB parsing
│   ├── EReader.Data/         # EF Core, migrations, repositories
│   └── EReader.Tests/        # Unit + integration tests
├── frontend/                 # React Native Web app
│   ├── src/
│   │   ├── screens/          # Library, Reader, Settings screens
│   │   ├── components/       # Shared UI components
│   │   ├── hooks/            # Custom hooks
│   │   └── services/         # API client
│   ├── web/                  # Web-specific entry point
│   └── package.json
├── docker/
│   ├── docker-compose.yml    # PostgreSQL + pgAdmin
│   └── postgres/
│       └── init.sql          # DB init script
├── test-books/               # EPUB test files (Phase 0 content)
├── .env.example              # Template for local env vars
└── DEV_ENV_SPEC.md           # This file
```

---

## 4. Database Setup

PostgreSQL runs in Docker to keep the host clean.

### 4.1 docker-compose.yml

```yaml
services:
  postgres:
    image: postgres:16-alpine
    container_name: ereader_postgres
    restart: unless-stopped
    environment:
      POSTGRES_USER: ereader
      POSTGRES_PASSWORD: ereader_dev
      POSTGRES_DB: ereader
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./docker/postgres/init.sql:/docker-entrypoint-initdb.d/init.sql

  pgadmin:
    image: dpage/pgadmin4
    container_name: ereader_pgadmin
    restart: unless-stopped
    environment:
      PGADMIN_DEFAULT_EMAIL: dev@ereader.local
      PGADMIN_DEFAULT_PASSWORD: dev
    ports:
      - "5050:80"
    depends_on:
      - postgres

volumes:
  postgres_data:
```

### 4.2 Start the database

```bash
docker compose -f docker/docker-compose.yml up -d
```

Verify:

```bash
psql -h localhost -U ereader -d ereader -c "\conninfo"
```

### 4.3 Environment Variables

Copy `.env.example` to `.env` and set values for your machine:

```
# .env.example
DATABASE_URL=Host=localhost;Port=5432;Database=ereader;Username=ereader;Password=ereader_dev
ASPNETCORE_ENVIRONMENT=Development
ASPNETCORE_URLS=http://localhost:5000
WIKIPEDIA_API_BASE=https://en.wikipedia.org/api/rest_v1
```

The `.env` file is git-ignored. Never commit real credentials.

---

## 5. Backend Setup

### 5.1 Create the solution

```bash
mkdir backend && cd backend

dotnet new sln -n EReader
dotnet new webapi -n EReader.Api --framework net10.0
dotnet new classlib -n EReader.Core --framework net10.0
dotnet new classlib -n EReader.Data --framework net10.0
dotnet new xunit -n EReader.Tests --framework net10.0

dotnet sln add EReader.Api/EReader.Api.csproj
dotnet sln add EReader.Core/EReader.Core.csproj
dotnet sln add EReader.Data/EReader.Data.csproj
dotnet sln add EReader.Tests/EReader.Tests.csproj
```

### 5.2 Add NuGet Packages

```bash
# Data layer
cd EReader.Data
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# API layer
cd ../EReader.Api
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Swashbuckle.AspNetCore          # OpenAPI / Swagger UI
dotnet add package Microsoft.AspNetCore.OpenApi

# EPUB parsing (ZIP + XML)
# .NET BCL handles ZIP (System.IO.Compression) and XML natively.
# No third-party EPUB library is required for the initial phase.

# Tests
cd ../EReader.Tests
dotnet add package Microsoft.EntityFrameworkCore.InMemory
dotnet add package FluentAssertions
dotnet add package Moq
```

### 5.3 EF Core Migrations

Once the `DbContext` and entity models are defined in `EReader.Data`:

```bash
cd backend

# Add initial migration
dotnet ef migrations add InitialCreate \
  --project EReader.Data \
  --startup-project EReader.Api

# Apply to the running PostgreSQL instance
dotnet ef database update \
  --project EReader.Data \
  --startup-project EReader.Api
```

### 5.4 Run the API

```bash
cd backend/EReader.Api
dotnet run
```

Expected output:
```
Now listening on: http://localhost:5000
```

Swagger UI available at: `http://localhost:5000/swagger`

### 5.5 Run Tests

```bash
cd backend
dotnet test
```

---

## 6. Frontend Setup

### 6.1 Initialise the React Native Web project

```bash
npx create-expo-app frontend --template blank-typescript
cd frontend
```

### 6.2 Add React Native Web and web bundler

```bash
npx expo install react-native-web react-dom @expo/metro-runtime
npx expo install expo-router
```

### 6.3 Additional dependencies

```bash
# Navigation
npx expo install expo-router react-native-safe-area-context react-native-screens

# HTTP client for API calls
npm install axios

# Async storage (reading position, settings cache)
npx expo install @react-native-async-storage/async-storage

# File picker (EPUB import)
npx expo install expo-document-picker

# Rich text / WebView for EPUB HTML rendering
npx expo install react-native-webview
```

### 6.4 Start the dev server

```bash
# Web target
npx expo start --web

# Mobile (Expo Go on device / simulator)
npx expo start
```

The web app runs at `http://localhost:8081` by default.

---

## 7. VS Code Extensions

Add to `.vscode/extensions.json` so VS Code prompts team members on first open:

```json
{
  "recommendations": [
    "ms-dotnettools.csdevkit",
    "ms-dotnettools.csharp",
    "esbenp.prettier-vscode",
    "dbaeumer.vscode-eslint",
    "ms-azuretools.vscode-docker",
    "ckolkman.vscode-postgres",
    "bradlc.vscode-tailwindcss",
    "ms-playwright.playwright"
  ]
}
```

---

## 8. Test Content (Phase 0 Books)

Source EPUB files from Project Gutenberg and Standard Ebooks. The goal is variety: short and long books, different language structures, EPUB 2 and EPUB 3 samples.

| Book | Source | Why |
|---|---|---|
| *The Time Machine* — H.G. Wells | Standard Ebooks | Short; clean EPUB 3; good baseline |
| *Pride and Prejudice* — Jane Austen | Standard Ebooks | Medium length; multiple chapters; rich TOC |
| *Moby-Dick* — Herman Melville | Standard Ebooks | Long; large chapter count; stress-tests search |
| *Alice's Adventures in Wonderland* — Lewis Carroll | Project Gutenberg | Inline images; tests image rendering |
| A non-English book (e.g. *Don Quijote*) | Project Gutenberg | Tests language/locale handling |

Place downloaded files in `test-books/` at the repo root. This directory is git-ignored (add `test-books/` to `.gitignore`); each developer downloads their own copies.

**Download helper script:**

```bash
#!/usr/bin/env bash
# scripts/download-test-books.sh
mkdir -p test-books
curl -L "https://standardebooks.org/ebooks/h-g-wells/the-time-machine/downloads/h-g-wells_the-time-machine.epub" \
     -o "test-books/the-time-machine.epub"
curl -L "https://standardebooks.org/ebooks/jane-austen/pride-and-prejudice/downloads/jane-austen_pride-and-prejudice.epub" \
     -o "test-books/pride-and-prejudice.epub"
# Add remaining books as URLs are confirmed
```

---

## 9. Offline Dictionary Data

The BRD requires offline dictionary lookups (FR-25, NFR-9). Set up the data source before Phase 5 work begins, but provision the data asset now.

**Recommended source:** WordNet or the StarDict/DictD format converted to a flat SQLite file.

A simple approach for development:

```bash
# Download a pre-built SQLite word definition database
# (exact URL TBD — confirm with team which dictionary dataset to license/use)
mkdir -p backend/EReader.Api/Data
# Place dictionary.sqlite here; it will be read-only at runtime
```

The API will expose a `/api/dictionary/{word}` endpoint backed by this file. The file is not committed to git (add to `.gitignore`); include download instructions in the README.

---

## 10. Local Environment Verification Checklist

Run through this list after completing setup to confirm everything works end-to-end.

```
[ ] Docker containers start without error (postgres, pgadmin)
[ ] psql connects to the ereader database on localhost:5432
[ ] dotnet build succeeds with no errors across all projects
[ ] dotnet ef database update applies migrations without error
[ ] dotnet run starts the API; Swagger UI loads at http://localhost:5000/swagger
[ ] dotnet test passes all tests (expect a minimal suite at Phase 0)
[ ] npx expo start --web opens the app shell in the browser at localhost:8081
[ ] At least one EPUB file is present in test-books/
[ ] .env file is present (not committed) and contains valid connection strings
```

---

## 11. Git Configuration

### 11.1 .gitignore additions

```
# Environment
.env
.env.local

# Test content (developers download their own)
test-books/

# Dictionary data
backend/EReader.Api/Data/*.sqlite

# Build artefacts
backend/**/bin/
backend/**/obj/
frontend/.expo/
frontend/node_modules/

# OS
.DS_Store
Thumbs.db
```

### 11.2 Branching convention

| Branch | Purpose |
|---|---|
| `main` | Stable, phase-complete code only |
| `phase/N-description` | Phase branch, e.g. `phase/1-epub-render` |
| `feat/short-description` | Feature work branched from the phase branch |
| `fix/short-description` | Bug fixes |

---

## 12. Phase 0 Definition of Done

Phase 0 is complete when every item below is true:

1. All prerequisites are installed and version-verified on each developer machine.
2. `docker compose up -d` starts PostgreSQL and pgAdmin without errors.
3. EF Core migrations run cleanly and create the initial schema.
4. The .NET API starts and serves the Swagger UI.
5. The React Native Web app starts and renders a placeholder screen in the browser.
6. At least three diverse EPUB test files are present and accessible locally.
7. The `.env.example` file documents every required variable.
8. The `.gitignore` excludes all items listed in Section 11.1.
9. A `scripts/download-test-books.sh` script (or equivalent) is committed so any developer can reproduce the test library.
