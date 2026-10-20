# Coding standards

House rules for this repo, beyond the rules that tooling already enforces. **Tooling owns
formatting.** A formatter and an editor config do that work; do not restate layout rules here, and
do not fix layout by hand.

Each rule is a convention. Cite it in review, and weigh it against the case.

## 1. Comments state what the code cannot, and they speak from where they stand

Write a comment only for what a competent reader cannot derive from the code itself: an external
constraint, a non-obvious invariant, or a workaround for a defect elsewhere. Keep it to one line,
and state the fact. That list is the whole permission. A comment that describes what the code does,
or that argues for your implementation choice, falls outside it, and so does a new comment on
existing code. Explain a decision you made in your reply to the user, where the commit message and
the pull request keep it.

A comment speaks from where it stands. It cites nothing that sends the reader off the page: no
spec path, no ADR number, no file path, no URL, and no issue or PR number such as `#38` or
`ticket #17`. A citation makes the reader open a second document to learn what the line in front of
them means, and the reader pays that cost on every pass. State the constraint itself, because the
constraint is the thing the reader needs. A tracker number has a further defect: the tracker can
renumber its items. `git blame`, the commit message, and the pull request keep the history, and a
test name states the behavior under test rather than the ticket that asked for it.

A **`TODO`** is the first exception, because it points at work that does not exist yet, so no fact on
the page can stand for it. A `TODO` gives the **full URL**, so the work stays one click away and
survives a move of the tracker:

```fsharp
// TODO(https://github.com/tw0po1nt/twopoint-website-staging/issues/42): bound the upload retry
```

The header of a generated file is the second exception. It names the generator and the source
by path, because you change the generator or the source, and the generator then writes the file:

```fsharp
// Generated from widgets.json by scripts/generate-widgets.fsx. Do not edit by hand.
```

This rule governs `///` XML doc comments as much as `//` comments. A doc comment that restates the
signature, such as `/// Gets the name` above `member Name`, adds no information. A reader must read
past it for nothing. Write a `///` comment only when it states something the signature does not,
such as a parameter's unit or valid range, a non-obvious exception, or a constraint on how to use
the member.

This rule is an overlay on the vendored `simplified-technical-english` skill, whose "Code comments
and software text" section says that a comment explains why the code exists. Apply STE to the
wording of a comment that this rule permits, and take the question of whether to write the comment
from here.

## 2. A record of closures needs strong justification

A record whose fields are function types is a smell. It can hide a cycle between two modules. It
can also hide state that a caller cannot see or test without a call to the closure. Consider these
alternatives, in this order, before you use one:

1. **Reorder the code to break the cycle.** A closure-record field often exists only to let module
   A call module B before B is fully defined. Move the shared code earlier, or split it into a third
   module that both modules open. The closure is then not necessary.
2. **For a narrow scope, use individual function parameters.** A function that needs a callback
   takes that callback as a parameter. The dependency stays visible at the call site. A test can
   supply the callback directly, with no record to construct first.
3. **To share state across a boundary, use an interface.** An interface names its members. A
   caller can see what the interface does without a read of a closure body. A test can supply a
   fake implementation, with no real state to set up.

Use a closure-record field only when none of these three alternatives fits. State the reason in
your reply to the user, and keep it out of a source comment (rule 1).

## 3. Open the project namespaces first

In an F# file, put the `open` lines for the namespaces of this repo first. Then put a blank line,
and then the `open` lines for the other namespaces. Keep each group in alphabetical order. The
first group then shows which parts of the repo the file uses.

```fsharp
open TwoPoint.Site.Dev
open TwoPoint.Site.Generator

open Expecto
open System.Text
```

---

*Add a rule only when a real review finding shows that an unwritten convention caused a problem.
Keep this file short and concrete.*
