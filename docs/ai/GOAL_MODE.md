# Autonomous Goal Mode

## Activation and precedence

These rules apply only while Codex has an active unfinished Goal that the user explicitly created or activated through the Goal mechanism. Merely mentioning a goal or objective in an ordinary task does not activate this mode.

While active, this document replaces only the repository-root `AGENTS.md` key approval rule and the `Звичайний режим роботи` section. Every other `AGENTS.md` rule remains in force. System, developer, product-permission, sandbox, and tool-safety requirements retain higher priority.

## Standing authorization

Creating or activating the Goal is advance authorization for Codex to take every ordinary action reasonably necessary to achieve that Goal without requesting intermediate approval or another `ок`.

Within the Goal's scope, Codex may autonomously:

- inspect the repository, documentation, retained evidence, binaries, logs, and relevant external sources;
- create, edit, move, rename, or delete goal-scoped repository files;
- run restores, generators, formatters, builds, tests, packaging, deployment, and focused runtime verification;
- change project-local or task-local configuration needed by the Goal;
- install test outputs to the client or dedicated-server destinations inherent to the Goal;
- launch, observe, and stop project-owned processes needed for validation;
- stage only goal-owned paths, create focused commits, and perform non-force pushes to the current upstream branch when publication is necessary to complete the Goal;
- correct and revalidate in-scope implementation or validation failures without asking the user again.

Codex should not pause for routine clarification when a safe, reasonable assumption can keep the work within the Goal. Material assumptions must be stated in progress updates or the final report.

## Boundaries and stopping conditions

- Stay within the explicit Goal and do not absorb unrelated findings.
- Preserve unrelated local changes, external state, and user data.
- Do not use force push, rewrite Git history, perform broad recursive deletion, change credentials, make financial commitments, or act toward third parties unless the Goal itself explicitly and specifically authorizes that action.
- Prefer bounded, reversible actions and retain exact cleanup responsibility for processes or temporary state created during the Goal.
- Continue until the Goal is complete, the user explicitly pauses or changes it, an explicit budget is exhausted, or progress is genuinely blocked by missing authority or an external condition.
- If completion requires a material scope expansion or an action not authorized by the Goal, stop at a safe point and report the exact blocker.
