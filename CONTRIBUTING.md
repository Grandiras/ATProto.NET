# Contributing to ATProto.NET

Thank you for your interest in contributing! This guide will help you get started.

## Code of Conduct

By participating in this project, you agree to maintain a respectful and inclusive environment.

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or later
- A text editor or IDE (VS Code, Rider, Visual Studio)
- [Podman](https://podman.io/) or Docker (for integration tests)

### Building

```bash
git clone https://git.grandiras.net/Grandiras/ATProto.NET.git
cd ATProto.NET
dotnet build
```

### Running Tests

**Unit tests** (no external dependencies):

```bash
dotnet test tests/ATProtoNet.Tests/
```

**Integration tests** (requires a local PDS):

```bash
# Start a local PDS
podman run -d --name atproto-pds \
  -p 2583:3000 \
  -e PDS_HOSTNAME=pds.test \
  -e PDS_DATA_DIRECTORY=/pds \
  -e PDS_JWT_SECRET=$(openssl rand -hex 16) \
  -e PDS_ADMIN_PASSWORD=admin-pass \
  -e PDS_PLC_ROTATION_KEY_K256_PRIVATE_KEY_HEX=$(openssl rand -hex 32) \
  -e PDS_DEV_MODE=true \
  -v pds-data:/pds \
  ghcr.io/bluesky-social/pds:latest

# Create a test account
curl -s -X POST http://localhost:2583/xrpc/com.atproto.server.createAccount \
  -H "Content-Type: application/json" \
  -H "Authorization: Basic $(echo -n 'admin:admin-pass' | base64)" \
  -d '{"handle":"testuser.pds.test","email":"test@test.com","password":"test-password"}'

# Run integration tests
ATPROTO_PDS_URL=http://localhost:2583 \
ATPROTO_TEST_HANDLE=testuser.pds.test \
ATPROTO_TEST_PASSWORD=test-password \
dotnet test tests/ATProtoNet.IntegrationTests/
```

## How to Contribute

### Reporting Bugs

1. Check existing issues to avoid duplicates
2. Use the **Bug Report** issue template
3. Include: .NET version, OS, steps to reproduce, expected vs actual behavior

### Suggesting Features

1. Open a discussion or issue using the **Feature Request** template
2. Describe the use case and proposed API surface
3. Consider backward compatibility

### Submitting Code

1. **Fork** the repository
2. **Create a branch** from `main`: `git checkout -b feature/my-feature`
3. **Write code** following the project conventions (see below)
4. **Add tests** — both unit and integration tests where applicable
5. **Run all tests** and ensure they pass
6. **Commit** with clear, descriptive messages
7. **Open a Pull Request** against `main`

### PR Guidelines

- Keep PRs focused — one feature or fix per PR
- Include a clear description of what changed and why
- Reference related issues with `Fixes #123` or `Closes #123`
- Ensure CI passes before requesting review
- Be responsive to review feedback

## Code Conventions

### General

- Target `net10.0` (latest LTS)
- Use C# latest language features where they improve clarity
- Enable nullable reference types everywhere
- XML-document all public APIs

### Naming

- Follow [.NET naming conventions](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/identifier-names)
- Use `Async` suffix for async methods
- Prefix interfaces with `I`
- Use `camelCase` for JSON serialization (matches AT Proto convention)

### Architecture

- **ATProtoNet** — Core SDK, zero ASP.NET dependency
- **ATProtoNet.Server** — ASP.NET Core integration (DI, auth handlers)
- **ATProtoNet.Blazor** — Blazor components and auth state

### Testing

- Unit tests go in `tests/ATProtoNet.Tests/`
- Integration tests go in `tests/ATProtoNet.IntegrationTests/`
- Use `[RequiresPdsFact]` for tests that need a live PDS
- Use `[RequiresBlueskyFact]` for tests that need Bluesky app view services
- Name tests: `MethodName_Scenario_ExpectedResult`

### Commits

- Use conventional commit format when possible:
  - `feat: add blob upload support`
  - `fix: handle null CID in record response`
  - `docs: update custom records guide`
  - `test: add integration tests for firehose`

## Project Structure

```
ATProto.NET/
├── src/
│   ├── ATProtoNet/              # Core SDK
│   │   ├── Http/                # XRPC client, HTTP helpers
│   │   ├── Identity/            # DID, Handle, AtUri, etc.
│   │   ├── Lexicon/             # AT Proto lexicon implementations
│   │   ├── Models/              # Shared model types
│   │   ├── Serialization/       # JSON converters
│   │   └── Streaming/           # Firehose / WebSocket
│   ├── ATProtoNet.Server/       # ASP.NET Core integration
│   └── ATProtoNet.Blazor/       # Blazor components
├── tests/
│   ├── ATProtoNet.Tests/        # Unit tests
│   └── ATProtoNet.IntegrationTests/  # Integration tests
├── docs/                        # Documentation
└── samples/                     # Example projects
```

## License

By contributing, you agree that your contributions will be licensed under the [MIT License](LICENSE).
