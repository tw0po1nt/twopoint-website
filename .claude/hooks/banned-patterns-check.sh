#!/usr/bin/env bash
# PreToolUse check: deny the call when its pending prose matches a pattern that
# `.banned-patterns` forbids. Two kinds of call carry prose:
#
#   - A Write or an Edit on a Markdown or F# file.
#   - A Bash command that publishes prose: `git commit`, a `gh` issue or pull
#     request create, edit, or comment, and a `gh api` call that sends a body.
#     The check reads the command text and each body file that the command
#     names. This stops the text before it is public. pr-text.yml checks the
#     text of a pull request in CI only after it is public.
#
# This is enforcement. The pattern list decides the answer with no judgment, so
# the agent learns at the moment of writing rather than in CI.
# scripts/check-banned-patterns.sh stays the complete check for files, because
# this hook cannot see a file that a Bash command writes. An Edit carries a
# fragment, so the F# stripper reads the fragment as if it starts outside a
# comment or a string. Extend the `case` below with a source-language extension
# the same way that script's LANGUAGE marker describes.
set -euo pipefail

input="$(cat)"
tool_name="$(jq -r '.tool_name // empty' <<<"$input")"

if [ "$tool_name" = "Bash" ]; then
  command="$(jq -r '.tool_input.command // empty' <<<"$input")"
  echo "$command" | grep -Eq 'git( +-[cC] +[^ ]+| +-[^ ]+)* +commit\b|gh +(issue|pr) +(create|edit|comment)\b|gh +api\b.*\bbody=' || exit 0

  cwd="$(jq -r '.cwd // empty' <<<"$input")"
  pending="$command"
  # A body file follows `--body-file`, `--file`, `-F`, or `body=@`. The name
  # `-` means stdin, and the command text already contains a heredoc.
  while IFS= read -r body_file; do
    body_file="${body_file#\"}"; body_file="${body_file%\"}"
    body_file="${body_file#\'}"; body_file="${body_file%\'}"
    [ "$body_file" = "-" ] && continue
    case "$body_file" in /*) ;; *) body_file="${cwd:-.}/$body_file" ;; esac
    [ -f "$body_file" ] && pending="$pending"$'\n'"$(cat "$body_file")"
  done < <(printf '%s\n' "$command" \
    | grep -oE '(--body-file|--file)[= ]+("[^"]*"|'"'"'[^'"'"']*'"'"'|[^ ]+)|-F +("[^"]*"|'"'"'[^'"'"']*'"'"'|[^ =]+( |$))|body=@("[^"]*"|[^ ]+)' \
    | sed -E 's/^(--body-file|--file)[= ]+//; s/^-F +//; s/^body=@//; s/ $//' || true)
  label="this command"
  kind="markdown"
else
  file_path="$(jq -r '.tool_input.file_path // empty' <<<"$input")"

  case "$file_path" in
    */node_modules/* | */target/* | */bin/* | */obj/* | *.banned-patterns) exit 0 ;;
    *.md | *.markdown) kind="markdown" ;;
    *.fs | *.fsi | *.fsx) kind="fsharp" ;;
    *) exit 0 ;;
  esac

  # Write carries the whole file, Edit carries the replacement text alone.
  pending="$(jq -r '[.tool_input.content, .tool_input.new_string] | map(select(. != null)) | join("\n")' <<<"$input")"
  label="$file_path"
fi

[ -n "$pending" ] || exit 0

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

case "$kind" in
  fsharp) stripped="$(printf '%s\n' "$pending" | awk "$strip_fsharp")" ;;
  *) stripped="$(printf '%s\n' "$pending" | awk "$strip_markdown")" ;;
esac

hits=""
for i in "${!patterns[@]}"; do
  match="$(printf '%s\n' "$stripped" | grep -ioE -- "${patterns[$i]}" | head -1 || true)"
  [ -n "$match" ] || continue
  hits="$hits"$'\n'"  \"$match\" -- ${messages[$i]}"
done

[ -n "$hits" ] || exit 0

jq -n --arg hits "$hits" --arg label "$label" '{
  hookSpecificOutput: {
    hookEventName: "PreToolUse",
    permissionDecision: "deny",
    permissionDecisionReason: ("This text matches a pattern that .banned-patterns forbids, in " + $label + ":" + $hits + "\n\nAGENTS.md states the rule, and scripts/check-banned-patterns.sh fails CI on the same hit. Rewrite the text and try again. To name banned text inside a rule that forbids it, put that text in backticks.")
  }
}'
