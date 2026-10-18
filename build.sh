#!/usr/bin/env bash
# The CI of ATProto.NET: every job is a function here, and .forgejo/workflows/*.yml only call
# them, so the same checks run locally (design/04-testing.md, "CI shape").
#
#   ./build.sh <job> [args]     jobs: build test lexicons integration-pds integration-spaces
#                               api aot aspire changelog nightly release <version>
set -euo pipefail
cd "$(dirname "$0")"

CONFIG="${CONFIGURATION:-Release}"
RESULTS="artifacts/test-results"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# The unit, conformance and architecture test projects (everything but integration tests,
# benchmarks and the AOT smoke app).
unit_test_projects() {
  find tests -name '*.csproj' \
    | grep -E '/(ATProtoNet\.Tests|ATProtoNet\.[A-Za-z]+\.Tests|ATProtoNet\.Conformance\.[A-Za-z]+)\.csproj$' \
    | sort
}

# Runs the integration tests that carry one of the given Requirement traits, refusing to skip
# (ATPROTO_REQUIRE_INTEGRATION) or to match nothing (TreatNoTestsAsError).
integration() {
  local filter="$1" project=tests/ATProtoNet.IntegrationTests
  if [[ ! -d $project ]]; then echo "No integration tests yet ($project arrives in phase 5)."; return; fi
  ATPROTO_REQUIRE_INTEGRATION=1 dotnet test "$project" -c "$CONFIG" --filter "$filter" \
    --logger trx --results-directory "$RESULTS" -- RunConfiguration.TreatNoTestsAsError=true
}

job_build() {
  dotnet restore
  dotnet build --no-restore -c "$CONFIG" -warnaserror
}

job_test() {
  local project
  for project in $(unit_test_projects); do
    dotnet test "$project" --no-build -c "$CONFIG" --logger trx --results-directory "$RESULTS" \
      --collect "XPlat Code Coverage" --blame-hang-timeout 5m -- RunConfiguration.TreatNoTestsAsError=true
  done
}

# Phase 0: the vendored files must match lexicons.json. Phase 3 adds regenerating the
# first-party code with `git diff --exit-code` and compiling every vendored Lexicon.
job_lexicons() {
  python3 scripts/vendor-lexicons.py
  git diff --exit-code -- lexicons.json || { echo "lexicons.json does not match lexicons/: run scripts/vendor-lexicons.py"; exit 1; }
}

job_integration-pds() { integration "Requirement=Pds|Requirement=PdsAdmin"; }

job_integration-spaces() { integration "Requirement=Spaces"; }

# The baseline itself is enforced by the build (RS0016/RS0017). From 0.9.0 on, this job also
# validates the packages against the last release.
job_api() {
  local project
  for project in src/*/; do
    [[ -f $project/PublicAPI.Shipped.txt && -f $project/PublicAPI.Unshipped.txt ]] || { echo "$project has no public API baseline"; exit 1; }
  done
  echo "Public API baselines present; package validation starts with 0.9.0."
}

job_aot() {
  local out=artifacts/aot
  dotnet publish tests/AotSmoke -c "$CONFIG" -o "$out" -warnaserror
  "$out/AotSmoke"
}

job_aspire() { echo "ATProtoNet.Aspire.Hosting is ported in phase 11; nothing to check yet."; }

# A pull request adds a bullet under ## [Unreleased], unless it carries the no-changelog label.
job_changelog() {
  local base="${BASE_REF:?BASE_REF names the target branch}"
  [[ ",${PR_LABELS:-}," == *",no-changelog,"* ]] && { echo "Labelled no-changelog."; return; }
  git fetch --depth=1 origin "$base"
  local before after
  before=$(git show FETCH_HEAD:CHANGELOG.md | unreleased_bullets)
  after=$(unreleased_bullets < CHANGELOG.md)
  (( after > before )) || { echo "CHANGELOG.md: add a bullet under ## [Unreleased], or label the PR no-changelog."; exit 1; }
}

unreleased_bullets() { awk '/^## \[Unreleased\]/{on=1; next} /^## \[/{on=0} on && /^- /{n++} END{print n+0}'; }

job_nightly() {
  integration "Requirement=Jetstream|Requirement=JetstreamArchive|Requirement=Bluesky|Requirement=Tranquil"
  echo "Benchmarks against the 0.8 baseline start with phase 1 (tests/ATProtoNet.Benchmarks/README.md)."
}

# Builds, tests and packs <version>; pushes the packages and creates the Forgejo release when
# NUGET_API_KEY (Forgejo feed and API) and NUGET_ORG_API_KEY are set.
job_release() {
  local version="${1:?usage: build.sh release <version>}" notes=artifacts/release-notes.md
  awk -v v="$version" '$0 ~ "^## \\[" v "\\]" {on=1; next} /^## \[/{if (on) exit} on' CHANGELOG.md > "$notes"
  grep -q '[^[:space:]]' "$notes" || { echo "CHANGELOG.md has no section for $version"; exit 1; }
  dotnet build -c "$CONFIG" -warnaserror -p:Version="$version"
  job_test
  dotnet pack --no-build -c "$CONFIG" -p:Version="$version" -o artifacts/packages
  [[ -n ${NUGET_API_KEY:-} ]] || { echo "No NUGET_API_KEY: packed, not published."; return; }
  dotnet nuget push 'artifacts/packages/*.nupkg' --source https://git.grandiras.net/api/packages/Grandiras/nuget --api-key "$NUGET_API_KEY" --skip-duplicate
  dotnet nuget push 'artifacts/packages/*.nupkg' --source https://api.nuget.org/v3/index.json --api-key "$NUGET_ORG_API_KEY" --skip-duplicate
  local prerelease=false; [[ $version == *-* ]] && prerelease=true
  curl -fsS -X POST https://git.grandiras.net/api/v1/repos/Grandiras/ATProto.NET/releases \
    -H "Authorization: token $NUGET_API_KEY" -H "Content-Type: application/json" \
    -d "$(jq -n --arg tag "v$version" --arg body "$(cat "$notes")" --argjson pre "$prerelease" \
      '{tag_name: $tag, name: $tag, body: $body, draft: false, prerelease: $pre}')"
}

mkdir -p artifacts
job="${1:?usage: build.sh <job> [args]}"; shift
declare -F "job_$job" > /dev/null || { echo "unknown job: $job"; exit 2; }
"job_$job" "$@"
