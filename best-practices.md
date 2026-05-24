# Best Practices

## API Design

### Versioning
Use consistent, versioned URL prefixes for all endpoints (e.g. `/api/v1/...`).

### Error Shape
Always return a consistent JSON error shape for all 4xx and 5xx responses:
```json
{
  "error": {
    "code": "RESOURCE_NOT_FOUND",
    "message": "The requested book was not found.",
    "details": {}
  }
}
```

### Status Codes
Use HTTP status codes semantically:

| Code | Meaning |
|------|---------|
| 200  | OK |
| 201  | Created |
| 204  | No Content |
| 400  | Bad Request |
| 401  | Unauthorized |
| 403  | Forbidden |
| 404  | Not Found |
| 409  | Conflict |
| 422  | Unprocessable Entity |
| 500  | Internal Server Error |

### URL Naming
Use plural nouns for resource URLs. Avoid verbs in paths.
- Correct: `GET /books`, `DELETE /books/{id}`
- Wrong: `GET /getBooks`, `POST /deleteBook`

### Pagination
Paginate all list endpoints. Prefer cursor-based pagination; offset/limit is acceptable. Never return unbounded collections.

---

## C# / .NET Backend

### Async
All async methods must be truly async end-to-end. Never block with `.Result` or `.Wait()`. `async void` is forbidden except for event handlers.

### Dependency Injection / Lifetimes
Register dependencies by interface, not concrete type. Use lifetimes intentionally:
- **Scoped** — services that touch the DB
- **Transient** — stateless helpers
- **Singleton** — thread-safe, truly shared state only

### Exception Handling
Never catch `Exception` broadly to swallow errors. Let unhandled exceptions bubble to global middleware. Only catch specific exceptions when you intend to handle them or rethrow with added context.

### Controller Design
Keep controllers thin. No business logic, no direct DB access. Controllers validate input, delegate to the service layer, and map results to HTTP responses.

### Cancellation
All async controller actions must accept a `CancellationToken` and pass it through to all downstream service and repository calls.

---

## TypeScript / React Frontend

### TypeScript Strictness
No `any`. Every function parameter, return type, and state variable must be explicitly typed or correctly inferred. Use `unknown` over `any` when the shape is truly unknown.

### Component Structure
Components are functions, not classes. One component per file. Co-locate the component's types and helpers in the same file unless they're shared — then extract to a shared types file.

### Data Fetching
Fetch and mutation logic lives in custom hooks or a data-fetching layer (e.g. React Query), not inside component bodies. Components consume data; they don't orchestrate fetches.

### State / Context
Avoid prop drilling beyond 2 levels. Use React Context for cross-cutting concerns (auth, theme). Do not use Context for server state — that belongs in the data-fetching layer.

---

## Testing

### Scope
Unit tests cover pure business logic and the service layer. They use fakes/stubs for external dependencies — not mocks of the DB layer itself. Integration tests own DB interaction.

### Naming
Test names follow the pattern: `Should_[ExpectedResult]_When_[Condition]`. Each test has a single logical assertion (multiple `Assert` calls are fine if they test one concept).

### Philosophy
Never test implementation details. Tests should break when behavior changes, not when internal structure is refactored.

### Isolation
Each test is fully self-contained: it arranges its own state, does not share mutable state with other tests, and cleans up after itself. DB tests roll back or use isolated transactions.

---

## Testing Frameworks

### Backend — xUnit (C#)

**Stack:** xUnit · FluentAssertions · Moq · EF Core InMemory · coverlet

Project: `backend/EReader.Tests/`

```bash
# Run all tests
cd backend && dotnet test

# Run with coverage report
cd backend && dotnet test --collect:"XPlat Code Coverage"

# Run a specific test project
cd backend && dotnet test EReader.Tests/EReader.Tests.csproj

# Run tests matching a filter
cd backend && dotnet test --filter "FullyQualifiedName~ServiceName"

# Watch mode (requires dotnet-watch)
cd backend && dotnet watch test
```

Use `Moq` for mocking service dependencies. Use `EF Core InMemory` provider for repository-level tests that need a DB context without spinning up Postgres.

---

### Frontend — Jest + React Native Testing Library

**Stack:** jest-expo · @testing-library/react-native · @testing-library/jest-native

Test files: `src/**/__tests__/*.{ts,tsx}` or `src/**/*.test.{ts,tsx}`

```bash
cd frontend

# Run all unit/component tests
npm test

# Watch mode (re-runs on file change)
npm run test:watch

# With coverage report
npm run test:coverage
```

Query by role, text, or label — never by test ID or internal prop. Data fetching lives in hooks; test hooks with `renderHook` from RNTL.

---

### Frontend — Playwright (Web E2E)

**Stack:** @playwright/test · Chromium

Test files: `frontend/e2e/web/**/*.spec.ts`

Playwright auto-starts the Expo web server (`npx expo start --web`) before running tests.

```bash
cd frontend

# Run all web E2E tests (headless)
npm run test:e2e:web

# Run with interactive UI (debug mode)
npm run test:e2e:web:ui
```

---

### Frontend — Detox (iOS / Android E2E)

**Stack:** detox · jest

Test files: `frontend/e2e/native/**/*.test.ts`

> **Not active yet — requires native builds.** Run `npx expo prebuild` first to generate `ios/` and `android/` directories before using any Detox command.

```bash
cd frontend

# Build the native app for testing (one-time per config change)
npm run test:e2e:ios:build
npm run test:e2e:android:build

# Run E2E tests
npm run test:e2e:ios
npm run test:e2e:android
```