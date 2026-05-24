---
description: Review a PR against the EReader project's best-practices.md standards
---

You are reviewing a pull request for the EReader project. Your job is to check the diff against the project's established standards and call out violations, risks, and non-obvious decisions.

## What to review

**Step 1 — Get the diff**

If a PR number was passed as an argument, fetch it:
```
gh pr diff <number>
gh pr view <number>
```

If no argument was given, diff the current branch against master:
```
git diff master...HEAD
git log master...HEAD --oneline
```

**Step 2 — Check against each standard below**

Go through every changed file. For each file, apply the relevant checklist sections. Do not comment on things that are fine — only surface problems.

---

## Standards checklist

### API Design
- All routes must be prefixed `/api/v1/...`
- Error responses must use `{ error: { code, message, details } }` — no bare strings or custom shapes
- HTTP status codes must be semantically correct (201 for creates, 204 for no-content, 409 for conflicts, etc.)
- Resource URLs use plural nouns, no verbs in paths (`GET /books` not `GET /getBooks`)
- List endpoints must be paginated — unbounded collection returns are a bug

### C# / .NET Backend
- No `.Result` or `.Wait()` — all async must be truly async end-to-end
- `async void` is forbidden (except event handlers)
- Dependencies registered by interface, not concrete type
- DI lifetime correctness: scoped for DB-touching services, transient for stateless helpers, singleton only for genuinely shared thread-safe state
- No broad `catch (Exception)` that swallows errors — let unhandled exceptions bubble to global middleware
- Controllers must be thin: no business logic, no direct DB calls
- All async controller actions must accept a `CancellationToken` and pass it downstream

### TypeScript / React Frontend
- No `any` — use `unknown` when shape is truly unknown
- Data fetching and mutation logic lives in custom hooks, not inside component bodies
- No prop drilling beyond 2 levels — use Context for cross-cutting concerns
- Context must not hold server state — that belongs in the data-fetching layer
- One component per file

### Testing
- Test names follow `Should_[ExpectedResult]_When_[Condition]`
- Tests are fully self-contained — no shared mutable state between tests
- DB tests roll back or use isolated transactions
- Tests cover behavior, not implementation details
- Unit tests use fakes/stubs for external dependencies — integration tests own DB interaction

---

## Output format

Group findings by file. For each problem, state:
1. The file and line (if identifiable)
2. Which rule it violates
3. Why it matters (one sentence — skip if obvious from the rule)
4. A concrete fix suggestion

If a decision is non-obvious but arguably correct, flag it as a question rather than a violation.

End with a one-paragraph summary: overall verdict, the most important issue to fix before merge, and anything that looks risky but wasn't a clear rule violation.