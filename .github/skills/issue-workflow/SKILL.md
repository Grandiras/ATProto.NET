---
name: issue-workflow
description: "End-to-end issue implementation workflow: fetch, prioritize, implement on a branch, test, update changelog and docs, commit, push the branch, open a pull request, wait for review and CI, squash-merge, and close issues. Use when: working on issues, implementing features from issue tracker, closing issues, shipping features, issue triage and implementation, release workflow."
argument-hint: "Issue number(s) or 'prioritize' to triage open issues"
---

# Issue Implementation Workflow

Complete lifecycle for taking issues from the tracker through to merged code. Work never goes
straight to `main`: every issue lands through its own branch and pull request, which is merged only
once the review approves it and CI is green.

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

### Phase 2 — Branch

Create one branch per issue from an up-to-date `main`:
```
git fetch origin
git switch -c issue-<number>-<short-slug> origin/main
```

### Phase 3 — Research & Plan

1. **Identify the interface or extension point** — Find the interface, base class, or API surface being implemented or extended. Read its contract first.
2. **Study one existing implementation** — Find a sibling implementation (e.g., `FileAtProtoSessionStore` when building a new session store) and follow its patterns (constructor shape, error handling, logging, DI registration).
3. **Use the AT Protocol MCP** (`mcp_atproto-docs_search_at_proto_knowledge_sources`) when implementing anything related to AT Protocol specs (OAuth, scopes, permissions, identity, repo sync, Lexicons, etc.) to ensure the implementation follows the actual protocol specification.
4. Create a todo list for the implementation steps.

### Phase 4 — Implement

1. Write the implementation code following existing project patterns (see `CLAUDE.md` and `CONTRIBUTING.md`).
2. Write tests following existing test patterns (xUnit, same structure as nearby test files).
3. If creating a new project:
   - Add it to `ATProto.NET.slnx`
   - Add a `<ProjectReference>` in the test project's `.csproj`
4. Build the whole solution, which also compiles every C# sample in the docs:
   ```
   dotnet build -p:EnableSourceControlManagerQueries=false
   ```
   It must finish with 0 warnings and 0 errors.

### Phase 5 — Test

1. Run targeted tests first:
   ```
   dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false --filter "FullyQualifiedName~<TestClass>"
   ```
2. Run the full test suite to catch regressions:
   ```
   dotnet test tests/ATProtoNet.Tests/ -p:EnableSourceControlManagerQueries=false
   ```
3. All tests must pass (0 failures) before proceeding.

### Phase 6 — Release Notes and Documentation

1. Add a bullet to the `## [Unreleased]` section of `CHANGELOG.md`, under `Breaking changes` / `Added` / `Changed` / `Fixed` / `Removed` / `Security` as appropriate.
2. Follow the style in `CLAUDE.md`: a **bold title** plus 1–3 sentences of user-visible effect, at most ~300 characters, ending with `(#N)`; a breaking change adds a one-line `Migration: …` and an entry in the release's migration guide.
3. Update every page of `docs/` and `README.md` that names an API you changed or removed; the doc-snippet build points at the samples. Expand the documentation if the change introduces new concepts or usage patterns, and list a new page in `docs/index.md`.

### Phase 7 — Commit, Push & Open the Pull Request

**When working on multiple issues, use one branch and one pull request per issue**, so each has a clean history and its own review.

1. Stage only the relevant files for this issue (do not stage untracked config/dotfiles):
   ```
   git add <changed and new files>
   ```
2. Verify with `git diff --cached --stat`.
3. Commit with a conventional commit message, CHANGELOG included in the same commit:
   ```
   git commit -m "feat: <concise summary> (closes #N)

   - <bullet per change>"
   ```
4. Push the branch (never `main`):
   ```
   git push -u origin issue-<number>-<short-slug>
   ```
5. Open the pull request against `main`, its description starting with `Closes #N.`:
   ```
   fj pr create --base main --head issue-<number>-<short-slug> --body-file <description.md> "feat: <concise summary>"
   ```

### Phase 8 — Review & CI

1. Wait for CI and for the automatic review of the current head; both land a few minutes after the push:
   ```
   fj pr status <pr> --wait
   fj pr review <pr> list
   ```
2. On requested changes, or a comment review raising real issues, fix them on the branch, push, and wait for the re-review. A finding that is genuinely wrong gets a PR comment explaining why, not a silent merge.
3. The pull request is ready only when the review of the current head approves and CI is green.

### Phase 9 — Merge & Clean Up

1. Squash-merge and delete the branch on the forge:
   ```
   fj pr merge <pr> --method squash --delete
   ```
2. Delete the local branch (and any worktree made for it), and update `main`:
   ```
   git switch main && git pull --ff-only
   git branch -D issue-<number>-<short-slug>
   ```
3. The `Closes #N` in the description closes the issue on merge. If it stayed open, close it with a summary:
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
- [ ] Solution builds with 0 warnings, doc samples included
- [ ] New projects added to solution file and test project references
- [ ] CHANGELOG.md updated under `[Unreleased]`, in the same commit
- [ ] Docs updated for every changed or removed API
- [ ] Commit message references the issue number
- [ ] Branch pushed and pull request opened (never pushed to `main`)
- [ ] Review approved the current head and CI is green
- [ ] Squash-merged, branch deleted, issue closed
