#!/usr/bin/env bash
# Prints a release's "What's changed" section (Markdown) for release-desktop.yml, or nothing when no
# change in it matters to users.
#
#   packaging/release-changes.sh <version> <commit>     e.g. 3.0.11 or 3.0.11-beta.2, and the commit built
#
# Compared with the release before it: a release (3.0.11) with the previous release (v3.0.10), skipping
# that patch's betas; a beta with the beta before it (3.0.11-beta.2 with v3.0.11-beta.1), and the first
# beta of a patch with the previous release. Needs the full history (checkout with fetch-depth: 0), and
# GH_TOKEN and GITHUB_REPOSITORY for looking up each commit's pull request.
#
# One line per change: a pull request merged into develop as its title and author, a commit pushed
# straight to develop as its subject (also when it only reached master inside a develop -> master pull
# request, which isn't listed itself). Left out, as nothing a user would notice: Dependabot's updates,
# changes only to CI, tests or docs, and pull requests labelled skip-changelog.
set -euo pipefail

version="${1#v}"
commit="$2"
repo="${GITHUB_REPOSITORY:?GITHUB_REPOSITORY not set}"

# The newest release (vX.Y.Z, no betas) before version $1.
release_before() {
  { git tag --list 'v*' | grep -E '^v[0-9]+\.[0-9]+\.[0-9]+$' | sed 's/^v//'; echo "$1"; } |
    sort -uV | grep -B1 -xF "$1" | grep -vxF "$1" | sed 's/^/v/' || true
}

if [[ "$version" =~ ^([0-9]+\.[0-9]+\.[0-9]+)-beta\.([0-9]+)$ ]]; then
  patch="${BASH_REMATCH[1]}"
  beta="${BASH_REMATCH[2]}"
  previous=$(git tag --list "v$patch-beta.*" | sed "s|^v$patch-beta\.||" | grep -E '^[0-9]+$' |
    awk -v n="$beta" '$1 < n' | sort -n | tail -1 || true)
  if [ -n "$previous" ]; then
    previous="v$patch-beta.$previous"
  else
    previous=$(release_before "$patch")
  fi
else
  previous=$(release_before "$version")
fi

if [ -z "$previous" ]; then
  echo "No earlier release to compare v$version with - no changes listed" >&2
  exit 0
fi
echo "Changes in v$version: $previous..$commit" >&2

# Only these paths changed: nothing a user would notice.
not_for_users='^(\.github/|[^/]*\.Tests/|packaging/release-changes\.sh$|\.editorconfig$|\.gitignore$|.*\.md$)'

declare -A seen_prs=()
lines=()
while read -r sha; do
  if ! git diff-tree --no-commit-id --name-only -r "$sha" | grep -qvE "$not_for_users"; then
    continue
  fi
  author=$(git log -1 --format='%an' "$sha")
  [[ "$author" == dependabot* ]] && continue

  # The pull request it came in, if any: one into develop, not a develop -> master roll-up.
  pr=$(gh api "repos/$repo/commits/$sha/pulls" \
    --jq '[.[] | select(.head.ref != "develop")][0] // empty | [.number, .title, .user.login, .user.type, ([.labels[].name] | join(","))] | @tsv' 2>/dev/null || true)
  if [ -n "$pr" ]; then
    IFS=$'\t' read -r number title login type labels <<< "$pr"
    [ -n "${seen_prs[$number]:-}" ] && continue
    seen_prs[$number]=1
    [ "$type" = "Bot" ] && continue
    [[ ",$labels," == *",skip-changelog,"* ]] && continue
    lines+=("- $title (#$number, by @$login)")
  else
    # A squash-merged pull request's own commit names it as "(#87)": the lookup above finds those, so
    # this is a commit pushed straight to develop.
    subject=$(git log -1 --format='%s' "$sha" | sed -E 's/ \(#[0-9]+\)$//')
    lines+=("- $subject")
  fi
done < <(git rev-list --no-merges --reverse "$previous..$commit")

[ ${#lines[@]} -gt 0 ] || exit 0

echo "## What's changed"
echo
printf '%s\n' "${lines[@]}"
echo
echo "**Full changelog**: https://github.com/$repo/compare/$previous...v$version"
echo
