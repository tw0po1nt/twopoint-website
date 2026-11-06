# UI copy

UI copy is each string that a person sees in the admin, on the site, in an email, or in a push
notification. A label, a hint, a status line, an error, a dialog, and an empty state are UI copy.
The text of a blog post is not UI copy.

UI copy follows this document. The `simplified-technical-english` skill does not apply to UI copy.
STE gives text such as `The rebuild starts.`, and a person does not talk like that. Code comments,
docs, commits, issues, and pull requests still follow `technical-prose.md`.

`scripts/check-banned-patterns.sh` still reads each F# string literal, so the banned patterns also
apply to UI copy.

## Write like a person

- Write as a person speaks. Contractions are correct: `Couldn't`, `isn't`, `won't`.
- Name the effect for the reader. Write `unpublished changes` and `live`. Do not name a blob, a
  container, or a table, unless the author must know it to fix the problem.
- Do not make the admin or the site the subject of a sentence. Write `This deletes the post`, and
  do not write `The admin deletes the post`.
- Do not describe a state change in the simple present. Write `Starting the rebuild…`, and do not
  write `The rebuild starts.`
- Keep the terminal voice of the public site: `[post comment]`, `cat README`, `name>`.

## Patterns

| Situation | Pattern | Example |
| --- | --- | --- |
| Work in progress | `<Verb>ing…` | `Saving…`, `Checking for links to this post…` |
| Failure title, with the reason below it | `Couldn't <verb> the <object>.` | `Couldn't load the posts.` |
| Empty list | `No <items> yet.` | `No comments yet.` |
| Empty filter | `No <filter> <items>.` | `No pending edits.` |
| Empty-state subtitle | `<Items> show up here.` | `Newsletter subscribers show up here.` |
| Hint on a disabled menu item | A short fragment | `Only for published posts.` |
| A button or menu item in text | `Click '<label>'` | `Click 'Rebuild site' on the dashboard.` |
| Button, menu item, or dialog title | No article | `Change date`, `Publish changes`, `Save series` |
| Confirm dialog title | `<Verb> this <object>?` | `Delete this post?` |
| Dismiss button of a dialog | `Cancel` | `Cancel` |
| Permanent action | End the body with the undo line | `This can't be undone.` |
| Partial success title | `<Done>, with problems` | `Published, with problems` |
| Partial success line | The effect first, the cause in parentheses | `Comments are still open on this post (its post-table row wasn't deleted).` |
| Edit from another tab | Cause, then the fix | `This post changed in another tab. Reload the page to get the latest version.` |
| Invalid field | `'<field>' must be <form>.` | `'series' must be text.` |
| Invalid input on a form | `Enter a valid <thing>.` | `Enter a valid email address.` |

## Before you add a string

1. Find the pattern for the situation in the table above.
2. Use the same words as the strings near it.
3. Read the string aloud. If it does not sound like a person, write it again.
4. State only facts that the code makes true. For example, a trusted commenter skips review, so
   write `comments may be reviewed`.
