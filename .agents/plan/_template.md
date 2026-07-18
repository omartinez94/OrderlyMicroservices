# {{PROJECT_TITLE}} — Implementation Plan

> Scope: {{ONE_LINE_PROJECT_DESCRIPTION — what this plan delivers and who consumes it.}}

---

## Status

> **Plan version**: `v{{MAJOR}}.{{MINOR}}` ({{DATE}}) — `{{MINOR}}` increments after each phase completion; `{{MAJOR}}` is reserved for breaking restructures of the plan itself.
> **Current state**: {{CURRENT_STATE — e.g. "⏸ Not started", "🚧 Phase 2 in progress", "✅ Done on {{DATE}}".}}

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | {{PHASE_1_NAME}} | ⏸ Pending |
| 2 | {{PHASE_2_NAME}} | 🔒 Blocked |
| 3 | {{PHASE_3_NAME}} | 🔒 Blocked |
| 4 | {{PHASE_4_NAME}} | 🔒 Blocked |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: {{COMMIT_CONVENTION — e.g. "Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period."}}

> **Update rule**: **on every phase completion, the plan MUST be updated in the same commit as the phase work.** The plan is the source of truth for what was decided and what shipped; a phase that ships without a plan update is a phase that drifted. See [How to use this template](#how-to-use-this-template) for the workflow.

---

## 0. Skill & documentation conventions

### 0.1 Skill mandate — {{SKILL_NAME}}
> **All implementation work on this plan MUST {{SKILL_CONSTRAINT — one-line directive pointing at the relevant skill.}}**

### 0.2 Code-quality guard rails
- {{GUARDRAIL_1 — e.g. "TypeScript mandatory: all source files `.ts`, compiled before running."}}
- **{{CONFIG_NAME}}**: the following compiler/format options are required:
  ```json
  {
    "{{OPTION_1}}": "{{VALUE_1}}",
    "{{OPTION_2}}": "{{VALUE_2}}"
  }
  ```
- {{GUARDRAIL_3 — e.g. "Local dev only: refuses to run in production."}}
- {{GUARDRAIL_4 — e.g. "Zod for schemas: every input validated as `z.ZodObject`."}}

---

## 1. Context

{{WHY_WE_ARE_DOING_THIS — the problem, what's missing today, the user need. Reference any upstream plans / RFCs / tickets by name.}}

---

## 2. Goal

{{WHAT_WE_ARE_BUILDING — concrete deliverables in user-visible terms. Bullet list is fine.}}

---

## 3. Out of scope

- {{EXCLUSION_1 — explicit "we will NOT do this" so readers know where the line is.}}
- {{EXCLUSION_2}}

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| {{WHAT}} | {{CHOICE}} | {{WHY — one sentence.}} |

---

## 5. Folder layout

```
{{DIR_TREE — at minimum: a top-level `src/` tree plus any resource/static directories. Annotate non-obvious files.}}
```

---

## 6. {{SPEC_SECTION_NAME}} Specification

> The most important section — describes *what gets built* at a level the implementer can act on. One subsection per group of related items (tools, APIs, components, etc.).

### 6.{{N}} {{SUBSECTION_TITLE}}

*   **`{{ITEM_NAME}}`** — {{DESCRIPTION — input shape, output shape, behavior, edge cases. Be precise; this is the contract.}}
*   **`{{ANOTHER_ITEM}}`** — {{DESCRIPTION}}

### 6.{{N+1}} {{NEXT_SUBSECTION}}

*   ...

---

## 7. {{SECTION_7_NAME — e.g. "Cross-Repository Communication" or "Integration Points"}}

{{IF_APPLICABLE — only include this section if the work spans multiple repos or has integration points with other systems.}}

---

## 8. Security guardrails

> [!CAUTION]
> {{CRITICAL_SECURITY_NOTE — one-sentence call-out for the most important rule.}}

| Risk | Mitigation |
|---|---|
| {{RISK_1}} | {{MITIGATION_1}} |
| {{RISK_2}} | {{MITIGATION_2}} |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **1** | {{PHASE_1_NAME}} | {{GROUPS}} | {{GOAL — one sentence.}} |
| **2** | {{PHASE_2_NAME}} | {{GROUPS}} | {{GOAL}} |
| **3** | {{PHASE_3_NAME}} | {{GROUPS}} | {{GOAL}} |
| **4** | {{PHASE_4_NAME}} | {{GROUPS}} | {{GOAL}} |

### Phase {{N}} — {{PHASE_NAME}}

**Goal**: {{PHASE_GOAL — what "done" looks like for this phase.}}

**Status**: ⏸ Pending (update to ✅ Done on completion, with date)

**Deliverables**:

- [ ] {{DELIVERABLE_1}}
- [ ] {{DELIVERABLE_2}}
- [ ] {{DELIVERABLE_3}}

**Exit criteria**: {{WHAT_DEMONSTRATES_PHASE_DONE — the single command or observation that proves the phase works.}}

---

### {{PHASE_NUMBER_TO_APPEND_NOTES_TO}} implementation notes ({{DATE}})

> Append a new "implementation notes" section after every phase is finished. The structure stays constant so readers can find the same information in every phase's notes.

**§{{SECTION_REF}} items — adopted in Phase {{N}}.**
- {{ITEM}} — `[{{STATUS — ✅ adopted, ⚠ deferred, ❌ rejected}}]` {{RESOLUTION_NOTE}}.

**Bugs found + fixed during implementation.**
- {{BUG_AND_FIX — one line per bug, named with the symptom not the root cause.}}.

**Deferred to a Phase {{N}} follow-up ({{SCOPE}}).**
- {{DEFERRED_ITEM — link to the follow-up doc / TODO file if it lives elsewhere.}}

**Phase {{N}} verification ({{WITHOUT/WITH}} {{DEPENDENCY}}).**
- {{VERIFICATION_STEP — the command + expected output.}}

**Files added.** {{LIST}}. **Files modified:** {{LIST}}.

---

## 10. Technical considerations

> Surfaced from a {{REVIEW_TYPE}} review of this plan. Each item points at a concrete risk and (where useful) to the relevant reference doc. {{PHASE_TO_ADOPT_CROSS_CUTTING}} should adopt the cross-cutting items before any feature code is written — they are far cheaper to retrofit then.

### 10.1 Cross-cutting

> **Phase {{N}} adoption ({{DATE}}):** items marked `[P{{N}} ✅]` were implemented in the {{SCAFFOLD/REVIEW}}. Items without that marker remain pending for the phase that introduces the corresponding code.

**{{ITEM}}** — `[{{STATUS}}]` {{DESCRIPTION + concrete recommendation.}}

### 10.{{N}} Phase {{PHASE}} — {{PHASE_NAME}}

- **[{{STATUS}}]** {{ITEM}} — {{RESOLUTION_NOTE — what actually happened in implementation.}}

---

## How to use this template

1. **Copy** this file into `.agents/plan/<your-project>.md` (the `_` prefix on `_template.md` keeps it out of the plan list).
2. **Find-and-replace** the `{{...}}` placeholders. Most projects need Sections 0–9; Section 10 is optional but recommended.
3. **Bump the version** in the Status section to `v{{MAJOR}}.{{MINOR}}` and add a Changelog entry every time the plan changes — see [Plan versioning](#plan-versioning) below.
4. **For each phase**, copy the "Phase {{N}}" subsection before starting work. After completion, append a new "Phase {{N}} implementation notes ({{DATE}})" section using the same structure.
5. **Commit messages** convention goes in the Status section. The whole plan is the source of truth for what was decided — keep it current.
6. **Drift between the plan and the code is the bug class plans exist to prevent.** When implementation reveals the plan was wrong (schema different than expected, API behaves differently), update the plan *and* the code in the same commit.

### The phase-completion workflow

> **Every phase completion is two commits, not one.**

1. **Code commit** — the work itself (`feat: ...`). Do NOT touch the plan in this commit.
2. **Plan commit** — the plan update only (`docs: mark Phase {{N}} complete in <plan-name>`):
   - Bump `Plan version` from `v{{MAJOR}}.{{N-1}}` → `v{{MAJOR}}.{{N}}` in the Status section.
   - Mark the phase's `[ ]` → `[x]` and update the table row.
   - Append a new `### Phase {{N}} implementation notes ({{DATE}})` section under Section 9.
   - Update §10's "Phase {{N}} adoption" subnote to reflect what was actually adopted vs deferred.
   - Add a Changelog entry at the bottom.
   - **If you skip the plan commit, the phase is not done** — even if the code shipped. The next person to read the plan will not know what state it's in.

> Two commits keeps the diff reviewable: the code commit is just code, the plan commit is just documentation. Mixing them makes both harder to review and easier to forget.

### Section-by-section guidance

| Section | When to include | When to skip |
|---|---|---|
| 0 Skill & conventions | Almost always — even a one-line note about which skill to use | Never (always specify *something*) |
| 1 Context | Always | — |
| 2 Goal | Always | — |
| 3 Out of scope | Always | — |
| 4 Tech decisions | When the choice is non-obvious (new framework, language) | Throwaway scripts, one-liner fixes |
| 5 Folder layout | When the project adds ≥3 new files in a new tree | Trivial changes |
| 6 Specification | Always — this is the heart of the plan | — |
| 7 Cross-repo / integration | When the work crosses repo boundaries | Single-repo work |
| 8 Security guardrails | When the work touches auth, secrets, production, or destructive ops | Read-only tools, internal scripts |
| 9 Phases | When the work spans >1 week or >1 contributor | Trivial changes |
| 10 Technical considerations | When a review surfaced non-obvious constraints | Greenfield, well-trodden patterns |

### Plan versioning

Plans follow `v{{MAJOR}}.{{MINOR}}` semantics. The version lives in the Status section as the first line so it is the first thing a reader sees.

| Bump | When |
|---|---|
| **Minor** (`v1.0` → `v1.1`) | After each phase completion. Always paired with a Changelog entry. |
| **Major** (`v1.x` → `v2.0`) | When the plan itself is restructured: phase boundaries change, new phases added, or the goal/scope shifts significantly. Reflects that readers who knew the old plan should re-read. |
| **No bump for typos** | Fixing a typo or wording error doesn't need a version bump. The Changelog is for *meaningful* changes, not every commit. |

The version's purpose is to make "is this plan current?" answerable at a glance. If `Plan version` is `v1.2` and the latest Changelog entry is from last week, you're caught up. If the version is `v1.0` but the code shows 4 phases shipped, the plan drifted.

---

## Changelog

> Append a new entry every time the plan's `Plan version` field is bumped. Entries are written *with* the plan commit, not retroactively. Group related changes under a single version bump (e.g. a single phase completion = one entry, even if it touched 5 sections).

### v1.0 ({{DATE}}) — initial draft
- Created plan with {{N}} phases.
- Sections 0–9 drafted; Section 10 review notes appended.

### v1.1 ({{DATE}}) — Phase 1 complete
- Phase 1 status → ✅ Done; `[ ]` → `[x]` on deliverables.
- Phase 1 implementation notes appended.
- §10.1 + §10.2 marked adopted / deferred.
- Files added / modified listed in implementation notes.

### v1.{{N}} ({{DATE}}) — Phase {{N}} complete
- Phase {{N}} status → ✅ Done; `[ ]` → `[x]` on deliverables.
- Phase {{N}} implementation notes appended.
- §10.{{N}} marked adopted / deferred.
- New deviations from the original plan logged with rationale.

