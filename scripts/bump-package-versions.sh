#!/usr/bin/env bash
#
# bump-package-versions.sh
#
# Bumps the NuGet package version on the six SIPSorcery library projects
# in this monorepo and prepends a TODO release-notes line to each.
#
# What it edits in every csproj:
#   <Version>OLD</Version>                  -> <Version>NEW</Version>
#   <AssemblyVersion>OLD</AssemblyVersion>  -> <AssemblyVersion>NEW</AssemblyVersion>
#   <FileVersion>OLD</FileVersion>          -> <FileVersion>NEW</FileVersion>
#   <PackageReleaseNotes>-vOLD: ...         -> <PackageReleaseNotes>-vNEW: TODO
#                                                -vOLD: ...
#
# After running, search the repo for "TODO" inside a PackageReleaseNotes
# block and replace each with the actual release-note text for the new
# version before tagging the release.
#
# The script targets WSL / any Linux + bash + awk + GNU coreutils
# environment. Tested with bash 5.x and gawk 5.x (the WSL defaults).
#
# Author: Aaron Clauson + Claude Opus 4.7
# License: BSD-3-Clause (matches the rest of the repo).

set -euo pipefail

# ---------- helpers ----------

PROGNAME="$(basename "$0")"

err() { printf '%s: error: %s\n' "$PROGNAME" "$*" >&2; }

usage() {
  cat <<EOF
Usage: $PROGNAME [--dry-run] <new-version>

Arguments:
  <new-version>    The version to bump every NuGet library project to.
                   Must be a SemVer-shaped string, e.g. 10.0.7,
                   10.1.0-pre, 11.0.0-rc.1.

Options:
  --dry-run        Show the diff each file would receive without
                   writing anything.
  -h, --help       Show this help and exit.

The six packages this script targets are:
  src/SIPSorcery
  src/SIPSorcery.OpenAI.Realtime
  src/SIPSorcery.VP8
  src/SIPSorceryMedia.Abstractions
  src/SIPSorceryMedia.FFmpeg
  src/SIPSorceryMedia.Windows

Each project's Version, AssemblyVersion and FileVersion fields are set
to <new-version>, and a new line "-v<new-version>: TODO" is prepended
to PackageReleaseNotes. After the script runs, edit each TODO with the
actual release note text before tagging.

Examples:
  $PROGNAME 10.0.7              # bump all six to 10.0.7
  $PROGNAME --dry-run 10.0.7    # preview the changes
EOF
}

# ---------- parse args ----------

DRY_RUN=0
NEW_VERSION=""

while [ $# -gt 0 ]; do
  case "$1" in
    -h|--help) usage; exit 0 ;;
    --dry-run) DRY_RUN=1; shift ;;
    -*)
      err "unknown option: $1"
      usage >&2
      exit 2
      ;;
    *)
      if [ -n "$NEW_VERSION" ]; then
        err "expected one positional argument, got more"
        usage >&2
        exit 2
      fi
      NEW_VERSION="$1"
      shift
      ;;
  esac
done

if [ -z "$NEW_VERSION" ]; then
  err "missing required <new-version> argument"
  usage >&2
  exit 2
fi

# Permissive SemVer validation: MAJOR.MINOR.PATCH with optional
# pre-release tail. Reject anything else so a typo doesn't get
# committed across all six projects.
if ! printf '%s' "$NEW_VERSION" \
   | grep -Eq '^[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.-]+)?$'; then
  err "version '$NEW_VERSION' is not in MAJOR.MINOR.PATCH[-prerelease] form"
  exit 2
fi

# ---------- locate repo root ----------

# The script is expected to live in <repo>/scripts/. Walking up from the
# script's own directory makes it work regardless of the caller's cwd
# (e.g. when invoked from a sibling tooling directory or via an absolute
# path from another location on disk).
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

if [ ! -f "$REPO_ROOT/SIPSorcery.slnx" ] && [ ! -f "$REPO_ROOT/SIPSorcery.slnf" ]; then
  err "couldn't confirm repo root from $REPO_ROOT (no SIPSorcery.slnx / .slnf found)"
  err "is this script still in <repo>/scripts/?"
  exit 1
fi

# ---------- target projects ----------

PROJECTS=(
  "src/SIPSorcery/SIPSorcery.csproj"
  "src/SIPSorcery.OpenAI.Realtime/SIPSorcery.OpenAI.Realtime.csproj"
  "src/SIPSorcery.VP8/SIPSorcery.VP8.csproj"
  "src/SIPSorceryMedia.Abstractions/SIPSorceryMedia.Abstractions.csproj"
  "src/SIPSorceryMedia.FFmpeg/SIPSorceryMedia.FFmpeg.csproj"
  "src/SIPSorceryMedia.Windows/SIPSorceryMedia.Windows.csproj"
)

# ---------- the awk transform ----------
#
# Runs once per file. Reads stdin, writes a transformed copy to stdout.
# The input version is supplied as -v ver=...
#
# Transform rules:
#   - <Version>X</Version>, <AssemblyVersion>X</AssemblyVersion>,
#     <FileVersion>X</FileVersion>: replace X with the new version.
#   - The line containing <PackageReleaseNotes>: split at the opening
#     tag, emit the prefix-up-to-and-including-the-tag, append a fresh
#     "-v<ver>: TODO" line, then re-emit the original suffix (which
#     contains the previous first version note) on its own line.
#
# Idempotency: refuses to re-run on a file whose top release-notes line
# already references this version. The caller sets
# "guard_already_at=1" via -v, and the awk script flips
# already_at=1 when that condition holds. We detect the condition
# outside awk because it's simpler.

read -r -d '' AWK_TRANSFORM <<'AWK_EOF' || true
{
  line = $0
  # Replace value-only versions.
  if (line ~ /<Version>[^<]+<\/Version>/) {
    sub(/<Version>[^<]+<\/Version>/, "<Version>" ver "</Version>", line)
    print line; next
  }
  if (line ~ /<AssemblyVersion>[^<]+<\/AssemblyVersion>/) {
    sub(/<AssemblyVersion>[^<]+<\/AssemblyVersion>/, "<AssemblyVersion>" ver "</AssemblyVersion>", line)
    print line; next
  }
  if (line ~ /<FileVersion>[^<]+<\/FileVersion>/) {
    sub(/<FileVersion>[^<]+<\/FileVersion>/, "<FileVersion>" ver "</FileVersion>", line)
    print line; next
  }

  # Prepend a TODO line to <PackageReleaseNotes>.
  # Existing format: indent + "<PackageReleaseNotes>-vOLD: ..." on one line.
  # We split at the closing ">" of <PackageReleaseNotes>; emit the tag
  # plus our new "-v<ver>: TODO" line, then re-emit any text that was
  # on the rest of the original line on its own line below.
  if (line ~ /<PackageReleaseNotes>/ && line !~ /<\/PackageReleaseNotes>/) {
    tag = "<PackageReleaseNotes>"
    pos = index(line, tag)
    if (pos > 0) {
      head = substr(line, 1, pos + length(tag) - 1)
      tail = substr(line, pos + length(tag))
      printf "%s-v%s: TODO\n", head, ver
      if (length(tail) > 0) {
        print tail
      }
      next
    }
  }

  print line
}
AWK_EOF

# ---------- main loop ----------

failed=()
already_at=()
updated=()

for rel in "${PROJECTS[@]}"; do
  csproj="$REPO_ROOT/$rel"
  if [ ! -f "$csproj" ]; then
    err "missing project file: $rel"
    failed+=("$rel")
    continue
  fi

  # Detect "already at this version" by checking the first version note
  # on the existing PackageReleaseNotes line. If the script has already
  # been run once today (or the new version was already there for some
  # other reason), skip without prepending a duplicate TODO line.
  if grep -q "<PackageReleaseNotes>-v$NEW_VERSION:" "$csproj"; then
    printf 'skip   %-50s (release notes already start with -v%s)\n' "$rel" "$NEW_VERSION"
    already_at+=("$rel")
    continue
  fi

  tmp="$(mktemp)"
  trap 'rm -f "$tmp"' EXIT

  awk -v ver="$NEW_VERSION" "$AWK_TRANSFORM" "$csproj" > "$tmp"

  if ! cmp -s "$csproj" "$tmp"; then
    if [ "$DRY_RUN" -eq 1 ]; then
      printf 'would update %s -- diff:\n' "$rel"
      diff -u "$csproj" "$tmp" || true
      printf '\n'
    else
      cp "$tmp" "$csproj"
      printf 'update %-50s -> %s\n' "$rel" "$NEW_VERSION"
    fi
    updated+=("$rel")
  else
    printf 'noop   %-50s (no matching tags found)\n' "$rel"
  fi

  rm -f "$tmp"
  trap - EXIT
done

# ---------- summary ----------

echo
if [ "$DRY_RUN" -eq 1 ]; then
  echo "Dry run complete. ${#updated[@]} file(s) would be modified."
  exit 0
fi

echo "${#updated[@]} of ${#PROJECTS[@]} project file(s) updated to v$NEW_VERSION."
if [ ${#already_at[@]} -gt 0 ]; then
  echo "${#already_at[@]} file(s) already at -v$NEW_VERSION (no changes)."
fi
if [ ${#failed[@]} -gt 0 ]; then
  echo "${#failed[@]} file(s) failed -- see errors above."
  exit 1
fi

cat <<'TIP'

Next steps:
  1. Edit each PackageReleaseNotes block and replace "TODO" with the
     real release notes for this version. Search for the marker:

       grep -RIn 'TODO' src/*/

  2. Diff to confirm only the intended changes:

       git diff -- src/

  3. Build and pack to confirm everything still produces .nupkgs:

       dotnet pack SIPSorcery.slnf --configuration Release

  4. Commit and tag.
TIP
