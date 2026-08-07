# Contributing

Thanks for taking a look. This is a personal, self-hosted project maintained by
one person, but the build and tests are reproducible and pull requests are welcome.

**Open an issue before starting anything substantial.** For a bug fix or a small
improvement, just send the pull request. For a new feature or anything that changes
architecture, please describe it in an issue first — it may not fit the direction of
the project, and it's better to find that out before you write the code.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and
[Node.js 22+](https://nodejs.org). Docker isn't required for development, only for
running the full stack.

Run the API:

```bash
cd backend/MarkdownHub.Api && dotnet run
```

Run the frontend dev server (proxies `/api` and `/health` to `http://localhost:8080`,
so start the API first):

```bash
cd frontend && npm install && npm run dev
```

## Tests

Both suites must pass before a pull request is merged.

Backend — xUnit, EF Core InMemory provider, no database or Docker needed:

```bash
cd backend/MarkdownHub.Api.Tests && dotnet test
```

Frontend — Vitest and Testing Library in a jsdom environment:

```bash
cd frontend && npm test
```

Please add tests for whatever you change. Services and pure logic (parsing, diffing,
permissions, path safety) are the places where tests matter most.

## Code style

Coding standards for this repository are written down in [CLAUDE.md](CLAUDE.md).
The short version:

- One class, struct, or interface per file. Records and enums may be grouped.
- Always use curly braces, even for one-line statements and guard clauses.
- Define complete, explicit routes on every controller action — no controller-level
  `[Route]` prefixes.
- Keep filesystem access behind `HubPathService` and database access in the service
  layer.
- Comments explain *why*, not *what*. Skip documentation that just restates the
  method name.

`backend/.editorconfig` covers formatting. Don't add dependencies without a reason
to, and keep unrelated refactoring out of a pull request.

## Security

Don't report security problems in a public issue or pull request — see
[SECURITY.md](SECURITY.md).

## Licensing

This project is licensed under the GNU AGPL v3. By contributing, you agree that your
contributions are licensed under the same terms.
