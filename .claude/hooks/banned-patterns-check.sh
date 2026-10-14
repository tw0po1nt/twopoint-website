#!/usr/bin/env bash
# PreToolUse check for Write/Edit on a Markdown or F# file: deny the call when
# the pending text matches a pattern that `.banned-patterns` forbids.
#
# This is enforcement. The pattern list decides the answer with no judgment, so
# the agent learns at the moment of writing rather than in CI.
# scripts/check-banned-patterns.sh stays the complete check, because this hook
# cannot see a file that a Bash command writes. An Edit carries a fragment, so
# the F# stripper reads the fragment as if it starts outside a comment or a
# string. Extend the `case` below with a source-language extension the same
# way that script's LANGUAGE marker describes.
set -euo pipefail

input="$(cat)"
file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

case "$file_path" in
  *.md | *.markdown | *.fs | *.fsi | *.fsx) ;;
  *) exit 0 ;;
esac

case "$file_path" in
  */node_modules/* | */target/* | */bin/* | */obj/* | *.banned-patterns) exit 0 ;;
esac

root="${CLAUDE_PROJECT_DIR:-.}"
[ -f "$root/.banned-patterns" ] || exit 0

patterns=()
messages=()
while IFS=$'\t' read -r pattern message; do
  case "$pattern" in '' | '#'*) continue ;; esac
  patterns+=("$pattern")
  messages+=("${message:-Rewrite the text.}")
done < "$root/.banned-patterns"
[ ${#patterns[@]} -eq 0 ] && exit 0

# Write carries the whole file, Edit carries the replacement text alone.
pending="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
[ -n "$pending" ] || exit 0

# Match the stripping that scripts/check-banned-patterns.sh applies, so the hook
# and CI agree on what counts as prose.
strip_markdown='
  /^[[:space:]]*```/ { fence = !fence; print ""; next }
  fence { print ""; next }
  { gsub(/`[^`]*`/, ""); print }
'

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

case "$file_path" in
  *.fs | *.fsi | *.fsx) stripped="$(printf '%s\n' "$pending" | awk "$strip_fsharp")" ;;
  *) stripped="$(printf '%s\n' "$pending" | awk "$strip_markdown")" ;;
esac

hits=""
for i in "${!patterns[@]}"; do
  match="$(printf '%s\n' "$stripped" | grep -ioE -- "${patterns[$i]}" | head -1 || true)"
  [ -n "$match" ] || continue
  hits="$hits"$'\n'"  \"$match\" -- ${messages[$i]}"
done

[ -n "$hits" ] || exit 0

jq -n --arg hits "$hits" --arg path "$file_path" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "deny",
    permissionDecisionReason: ("This text matches a pattern that .banned-patterns forbids, in " + $path + ":" + $hits + "\n\nscripts/check-banned-patterns.sh fails CI on the same hit. Rewrite the text and try again. To name banned text inside a rule that forbids it, put that text in backticks.")
  }
}'
