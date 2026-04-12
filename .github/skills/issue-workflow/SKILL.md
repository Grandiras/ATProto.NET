---
name: issue-workflow
description: "End-to-end issue implementation workflow: fetch, prioritize, implement, test, update changelog, commit, push, and close issues. Use when: working on issues, implementing features from issue tracker, closing issues, shipping features, issue triage and implementation, release workflow."
argument-hint: "Issue number(s) or 'prioritize' to triage open issues"
---

# Issue Implementation Workflow

Complete lifecycle for taking issues from the tracker through to shipped code.

## When to Use

- Implementing one or more issues from the Forgejo tracker
- Triaging and prioritizing open issues
- Any feature/bugfix work that should end with a closed issue

## Procedure

### Phase 1 — Issue Selection

1. **If specific issue numbers are provided**, fetch their details:
   ```
   fj issue view -R origin <number>
   ```
2. **If asked to prioritize**, fetch all open issues and rank by impact × feasibility:
   ```
   fj issue search -R origin --state open
   ```
3. Use the ask_questions tool to clarify unclear details and to confirm the selected issue(s) before implementation. Start with the implementation without yielding control back to the user, only use the ask_questions tool if you need more information or confirmation.

### Phase 2 — Research & Plan

1. **Identify the interface or extension point** — Find the interface, base class, or API surface being implemented or extended. Read its contract first.
2. **Study one existing implementation** — Find a sibling implementation (e.g., `FileAtProtoTokenStore` when building an EF Core store) and follow its patterns (constructor shape, error handling, logging, DI registration).
3. **Use the AT Protocol MCP** (`mcp_atproto-docs_search_at_proto_knowledge_sources`) when implementing anything related to AT Protocol specs (OAuth, scopes, permissions, identity, repo sync, Lexicons, etc.) to ensure the implementation follows the actual protocol specification.
4. Create a todo list for the implementation steps.

### Phase 3 — Implement

1. Write the implementation code following existing project patterns.
2. Write tests following existing test patterns (xUnit, same structure as nearby test files).
3. If creating a new project:
   - Add it to `ATProto.NET.slnx`
   - Add a `<ProjectReference>` in the test project's `.csproj`
4. Build to verify compilation:
   ```
   dotnet build <project> -p:EnableSourceControlManagerQueries=false
   ```

### Phase 4 — Test

1. Run targeted tests first:
   ```
   dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false --filter "FullyQualifiedName~<TestClass>"
   ```
2. Run the full test suite to catch regressions:
   ```
   dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false
   ```
3. All tests must pass (0 failures) before proceeding.

### Phase 5 — Release Notes and Documentation

1. Update the `## [Unreleased]` section in `CHANGELOG.md`.
2. Follow the existing format: `- **Bold title** — Description with details`.
3. Place new entries at the top of the `### Added` / `### Fixed` / `### Changed` section as appropriate.
4. Update the documentation to reflect the new features or changes, following existing patterns and style.
5. Expand the documentation if the changes introduce new concepts or usage patterns.

### Phase 6 — Commit & Push

**When working on multiple issues, create one commit per issue** so each has a clean history and `closes #N` reference.

1. Stage only the relevant files for this issue (do not stage untracked config/dotfiles):
   ```
   git add <changed and new files>
   ```
2. Verify with `git diff --cached --stat`.
3. Commit with a conventional commit message:
   ```
   git commit -m "feat: <concise summary> (closes #N)

   - <bullet per change>"
   ```
4. Repeat for each issue if working on multiple.
5. Push all commits to origin:
   ```
   git push origin main
   ```

### Phase 7 — Close Issues

Close each implemented issue with a summary comment:
```
fj issue close -R origin <number> -w "<summary of what was implemented>"
```

## Build Notes

- Always pass `-p:EnableSourceControlManagerQueries=false` to `dotnet build` and `dotnet test` (workaround for `.gitmodules` access error).
- `dotnet test` and `dotnet restore` require network access (unsandboxed execution).
- Target framework is `net10.0`.
- The Forgejo remote is `origin` (`git.grandiras.net`).

## Quality Checklist

- [ ] Implementation follows AT Protocol specs (verified via MCP when applicable)
- [ ] Tests written and passing (0 failures in full suite)
- [ ] New projects added to solution file and test project references
- [ ] CHANGELOG.md updated under `[Unreleased]`
- [ ] Commit message references issue numbers
- [ ] Pushed to origin
- [ ] Issues closed with summary comment via `fj`
