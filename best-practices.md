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