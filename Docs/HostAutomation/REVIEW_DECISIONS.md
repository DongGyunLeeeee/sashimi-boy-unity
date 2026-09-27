# Review decisions and return-to-development loops

The Host separates review disposition from severity. Reviewer result schema 2
requires `category`, `basis`, `requirement`, `location`, `expected`, `actual`,
`reproduction`, and `recommendation`, in addition to severity/title/evidence.
Developer result schema remains 1. Legacy Reviewer results are refused rather
than interpreted as authorization to change Project state.

| Evidence | Host behavior after required validation |
| --- | --- |
| Demonstrated Defect, Blocker/Major | Publish all blocking findings, one current handoff, then Review to In Progress |
| HumanCheck, including visual/audio/feel/persistence | Add to Owner checklist; Review to Verification if automated checks pass |
| Infrastructure or Unverified, without a confirmed blocking defect | Preserve Review, retain diagnostic evidence, no ReviewFix and no PASS |
| Minor defect | Informational; a demonstrated violation of an explicit acceptance/merge gate must instead be classified at least Major |

A CodePath finding is reasoned evidence, not a claim that a test was executed.
Reproduction means an actually observed run. A demonstrated visual defect is
still a defect: human-only verification is not a blanket exemption for broken
art or UX. Subjective polish, missing manual screenshots, and unavailable
licensing/permissions are not game defects. Required Host checks remain gates.

`ReviewDecision.json` retains the complete classification. All confirmed
blocking findings appear together in `ReviewFinding.md`, which the existing
machine-readable handoff references. This prevents dropping findings after
the first item and discovering them serially in later repair cycles.

## Unity 6000.4 default serialization

The old Host validator always rejected even the already approved default-only
ProjectSettings drift. The Reviewer now supplies its exact owned run ID.
Before and after Unity, the validator checks the run marker and three clean
protected worktrees. It permits only the same six field replacements documented
in the legacy wrapper, in one unstaged ProjectSettings file with no mode/type
change. It compares the entire resulting file against the initial bytes, not
just matching diff lines. No file is restored to manufacture cleanliness.

The diff and SHA-256 remain under Artifacts outside Repository. The Reviewer
then checks the exact final file hash, every other source byte, and unchanged
Git control state. Developer validation has no exception and cannot deliver
these defaults. Wrong owner/run/root/version, dirty protected worktrees, extra
files/fields, altered values, staged changes, and missing newlines are refused.

The Host root/marker contract is the Issue #52 standalone-clone counterpart of
the legacy `%TEMP%/SashimiBoyAutomation` integration contract. The legacy
wrapper is unchanged. This changes neither gameplay settings nor source assets.

## Repository placeholders

The metadata scan no longer requires `.meta` files for leaf `.gitkeep` files.
The actual clean import contained 11 such Git placeholders and no Unity-created
metadata for them. Before this correction, the Host called all 11 missing
assets. The exclusion happens after the reparse check, records every ignored
placeholder, and does not exempt folders, other asset names, GUID collisions,
missing scripts, or broken references. No source placeholder or asset is edited.

## Rollout

Every Comment, Transition, and Draft PR creation rechecks authentication,
Issue content, exact linked PR and conversation, Project contract/status, and
the validated main SHA at the shared write boundary. Late changes suppress
the write. GitHub's complete snapshot does not support an atomic conditional
comment/Project write; these reads do not claim such a lock.

Authoritative generators run in batch mode with graphics available, because
`Camera.Render` is not compatible with the null graphics device. The existing
two-run comparison is still an idempotence check; independent-baseline
reproducibility remains an open item in the remediation matrix.

Keep old Desktop schedules paused. The infrastructure PR must still receive an
independent review and Owner merge before installation. Fixture PASS is not a
claim that a live Codex/Unity/GitHub scheduled cycle passed. Consult the current
remediation matrix for other findings and live-only verification still pending.
