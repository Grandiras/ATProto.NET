---
name: issue-workflow
description: "End-to-end issue workflow for the 0.9 rewrite on `next`: fetch the issue, branch, implement against the specs, test, changelog, commit, push, open a PR into `next`, wait for review and CI, squash-merge, clean up. Use when implementing tracker issues or a rewrite phase's work items."
argument-hint: "Issue number(s), or 'prioritize' to triage open issues"
---

# Issue workflow (`next`)

Work never goes straight to `next` or `main`: every issue lands through its own branch and pull
request, merged only once the review of its current head approves and CI is green.

## 1. Select

```
fj issue view -R origin <number>
fj issue search -R origin --state open     # to prioritize: impact × feasibility
```

A rewrite work item belongs to a phase: read that phase in `design/05-rewrite-plan.md`, its specs
in `design/specs/`, `design/03-api-guidelines.md` and the decisions it relies on. Those are the
instructions; 0.8 code on `main` is reference material.

## 2. Branch

```
git fetch origin
git switch -c issue-<number>-<short-slug> origin/next
```

## 3. Implement

1. Port the tests first: the spec's "Conformance and tests" section and requirement IDs name the
   scenarios. Spec-backed tests carry `[Trait("Spec", "ID")]`.
2. Implement until they pass, then run the phase gate checklist (`design/05-rewrite-plan.md#phase-gates`).
   Review against the shape rules before behaviour.
3. Apply each open point's default, settle the points left to the phase, follow upstream where a
   point waits on it, and record every outcome in the spec, in the same PR.
4. A new project goes into `ATProto.NET.slnx`; a new namespace into the architecture tests'
   `Layers.cs`.

## 4. Test

```
./build.sh build && ./build.sh test
```

0 warnings, 0 failures. Run `./build.sh lexicons` / `./build.sh aot` when you touched Lexicons or
anything the AOT smoke app uses.

## 5. Changelog and docs

- `CHANGELOG.md`, in the same commit, following [CONTRIBUTING.md](../../../CONTRIBUTING.md#changelog).
- Docs describe the code as it is: update the pages that name a changed API (the doc-snippet
  build finds the samples), and list new pages in `docs/index.md`.

## 6. Commit, push, open the PR

```
git add <the files of this issue>
git commit -m "feat: <summary> (closes #N)"
git push -u origin issue-<number>-<short-slug>
fj pr create --base next --head issue-<number>-<short-slug> --body-file <description.md> "feat: <summary>"
```

The description starts with `Closes #N.` and lists the requirement IDs the PR implements.

## 7. Review and CI

```
fj pr status <pr> --wait
fj pr review <pr> list
```

Fix requested changes (or real issues raised in a comment review), push, wait for the re-review.
A finding that is genuinely wrong gets a PR comment saying why, never a silent merge.

## 8. Merge and clean up

```
fj pr merge <pr> --method squash --delete
git switch next && git pull --ff-only && git branch -D issue-<number>-<short-slug>
```

Remove any worktree made for it. Close the issue with a summary if the merge did not.
