#!/usr/bin/env bash
# Fails when a banned pattern reaches the prose of this repo. The pattern list is
# `.banned-patterns` at the repo root.
#
# Each list line has an extended regular expression, a tab, and the message to
# report. The check reads Markdown files and F# source files. In F#, it reads
# comments and string literals. Add a case arm under the LANGUAGE marker below
# when this repo adopts another source language.
# It strips fenced code blocks and inline code spans first, so a rule can name
# the text it forbids by writing that text in backticks. A file that git
# ignores is skipped, and so is a blog post. A new file that git does not track
# yet is still read.
#
# `--text <label> <file>` checks one file of text as Markdown, and reports each
# hit under <label>. CI uses this mode for the text of a pull request: the
# title, the body, and the commit messages. No tracked file contains that text.
set -euo pipefail

if [ "${1:-}" = "--text" ]; then
  [ $# -eq 3 ] || { echo "Usage: $0 --text <label> <file>" >&2; exit 2; }
  text_label="$2"
  text_file="$(cd "$(dirname "$3")" && pwd)/$(basename "$3")"
fi

cd "$(git rev-parse --show-toplevel)"

patterns=()
messages=()
while IFS=$'\t' read -r pattern message; do
  case "$pattern" in '' | '#'*) continue ;; esac
  patterns+=("$pattern")
  messages+=("${message:-Rewrite the text.}")
done < .banned-patterns

if [ ${#patterns[@]} -eq 0 ]; then
  echo "check-banned-patterns: .banned-patterns has no pattern. Nothing to check."
  exit 0
fi

# A fenced block and an inline code span are not prose, which lets a rule name
# the text it forbids. Blanking a stripped line keeps the line numbers true.
strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'

# LANGUAGE: add one strip_<lang> awk program here per source language this
# repo adopts, matching strip_markdown's shape: a comment line is prose in
# full, and a code line contributes its string literals alone, because a
# shipped string is prose that a user reads. Register the extension below in
# the file-glob passed to `git ls-files`, and in the `case` that picks a
# stripper by extension.

# F#: keep the text of `//` and `(* *)` comments and of string literals. A
# block comment and a triple-quoted string can span lines, so the state
# continues from one line to the next. `(*)` is the multiplication operator.
strip_fsharp='
  BEGIN { q = sprintf("%c", 39); dq = sprintf("%c", 34) }
  {
    line = $0; n = length(line); out = ""; i = 1
    while (i <= n) {
      c = substr(line, i, 1); c2 = substr(line, i, 2); c3 = substr(line, i, 3)
      if (depth > 0) {
        if (c2 == "(*") { depth++; i += 2; continue }
        if (c2 == "*)") { depth--; out = out " "; i += 2; continue }
        out = out c; i++; continue
      }
      if (str == 3) {
        if (c3 == dq dq dq) { str = 0; out = out " "; i += 3; continue }
        out = out c; i++; continue
      }
      if (str == 2) {
        if (c2 == dq dq) { out = out dq; i += 2; continue }
        if (c == dq) { str = 0; out = out " "; i++; continue }
        out = out c; i++; continue
      }
      if (str == 1) {
        if (c == "\\") { out = out " "; i += 2; continue }
        if (c == dq) { str = 0; out = out " "; i++; continue }
        out = out c; i++; continue
      }
      if (c2 == "//") { out = out " " substr(line, i + 2); break }
      if (c3 == "(*)") { i += 3; continue }
      if (c2 == "(*") { depth = 1; i += 2; continue }
      if (c3 == dq dq dq) { str = 3; i += 3; continue }
      if (c2 == "@" dq) { str = 2; i += 2; continue }
      if (c == dq) { str = 1; i++; continue }
      if (c == q && substr(line, i + 2, 1) == q) { i += 3; continue }
      if (c == q && substr(line, i + 1, 1) == "\\") {
        j = index(substr(line, i + 2), q)
        if (j > 0 && j <= 6) { i += j + 2; continue }
      }
      i++
    }
    gsub(/`[^`]*`/, "", out)
    print out
  }
'

found=0

# Prints each hit in the stripped text of one source. $1 is the label for a hit.
report_hits() {
  local label="$1" stripped="$2" i hit
  for i in "${!patterns[@]}"; do
    while IFS= read -r hit; do
      [ -n "$hit" ] || continue
      echo "$label:${hit%%:*}: \"${hit#*:}\" -- ${messages[$i]}"
      found=1
    done < <(printf '%s\n' "$stripped" | grep -inoE -- "${patterns[$i]}" || true)
  done
}

if [ -n "${text_file:-}" ]; then
  report_hits "$text_label" "$(awk "$strip_markdown" "$text_file")"
else
  while IFS= read -r file; do
    case "$file" in
      node_modules/* | */target/* | */node_modules/* | */bin/* | */obj/* | frontend/Generator.Tests/posts/* | .banned-patterns | scripts/check-banned-patterns.sh) continue ;;
    esac

    case "$file" in
      *.md) stripped="$(awk "$strip_markdown" "$file")" ;;
      *.fs | *.fsi | *.fsx) stripped="$(awk "$strip_fsharp" "$file")" ;;
      *) continue ;;
    esac

    report_hits "$file" "$stripped"
  done < <(git ls-files --cached --others --exclude-standard -- '*.md' '*.fs' '*.fsi' '*.fsx')
fi

if [ "$found" -eq 1 ]; then
  echo
  echo "Each hit above matches a pattern that .banned-patterns forbids."
  echo "Rewrite the text. To name banned text inside a rule that forbids it, put"
  echo "that text in backticks."
  exit 1
fi

echo "check-banned-patterns: clean."
