#!/usr/bin/env bash
# Replaces the vendored Lexicon snapshot under lexicons/ (see README.md).
#
#   refresh.sh <commit>   a bluesky-social/atproto commit, usually the current main
#
# Needs curl, tar and jq. Prints what README.md records; update the README by hand.
set -euo pipefail

commit="${1:?usage: refresh.sh <bluesky-social/atproto commit>}"
here="$(cd "$(dirname "$0")" && pwd)"
out="$here/lexicons"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# 1. bluesky-social/atproto: the four namespaces the SDK implements.
curl -fsSL "https://codeload.github.com/bluesky-social/atproto/tar.gz/$commit" \
  | tar xz -C "$tmp" --wildcards '*/lexicons/*'
src="$(echo "$tmp"/atproto-*/lexicons)"

rm -rf "$out"
for ns in app/bsky chat/bsky com/atproto tools/ozone; do
  mkdir -p "$out/$ns"
  cp -R "$src/$ns/." "$out/$ns/"
done

echo "atproto $(curl -fsSL "https://api.github.com/repos/bluesky-social/atproto/commits/$commit" \
  | jq -r '.sha + " " + .commit.committer.date + " " + (.commit.message | split("\n")[0])')"

# 2. site.standard.*: the schema records its Lexicon authority publishes. They include the
#    permission sets, which the atproto repository does not carry.
did="$(curl -fsSL -H 'accept: application/dns-json' \
  'https://cloudflare-dns.com/dns-query?name=_lexicon.standard.site&type=TXT' \
  | jq -r '.Answer[].data | gsub("\""; "") | select(startswith("did=")) | ltrimstr("did=")')"
pds="$(curl -fsSL "https://plc.directory/$did" \
  | jq -r '.service[] | select(.id == "#atproto_pds") | .serviceEndpoint')"
echo "site.standard authority $did on $pds"

curl -fsSL "$pds/xrpc/com.atproto.repo.listRecords?repo=$did&collection=com.atproto.lexicon.schema&limit=100" \
  | jq -c '.records[]' \
  | while read -r record; do
      nsid="$(jq -r '.value.id' <<<"$record")"
      case "$nsid" in site.standard.*) ;; *) continue ;; esac
      path="$out/$(tr . / <<<"$nsid").json"
      mkdir -p "$(dirname "$path")"
      jq '.value | del(."$type") | {lexicon, id} + .' <<<"$record" > "$path"
      echo "  $nsid $(jq -r '.cid' <<<"$record")"
    done
