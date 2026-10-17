# Coding standards

House rules for this repo, beyond the rules that tooling already enforces. **Tooling owns
formatting.** A formatter and an editor config do that work; do not restate layout rules here, and
do not fix layout by hand.

Each rule is a convention. Cite it in review, and weigh it against the case.

1. A code comment cites nothing off the page: no task, fix, issue number, or caller. See
   `docs/standards/technical-prose.md`.
2. A comment states what the code cannot: a rule, a constraint, a unit, an effect on the outside
   world, or an example of a format. Do not comment a type, a union case, a field, or a function
   when the comment only repeats its name and its signature.

Add a rule only when a real review finding shows that an
unwritten convention caused a problem. A rule keeps whatever number it had at its point of origin
when it travels between repos, so a citation stays stable and gaps in the numbering are normal.

---

*Seeded by the `bootstrap` skill. Keep this file short and concrete.*
