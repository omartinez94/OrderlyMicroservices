# Quality Gate Enhancement — Implementation Plan

> Scope: Expand the `phase-guard.ps1` script from a basic build/test check to a comprehensive, multi-tiered quality gate enforcing C# formatting/analyzers, OWASP security scans, C# architectural boundaries (NodaTime/MediatR), Docker/OpenAPI contract schemas, and test coverage thresholds.

---

## Status

> **Plan version**: `v1.0` (2026-08-04) — `MINOR` increments per phase completion; `MAJOR` is reserved for breaking restructures of the plan itself.
> **Current state**: ⏸ Not started

| Phase | Name | Status |
|:-----:|---|:-----:|
| 1 | Formatting, Style & Secret Scanning | ⏸ Pending |
| 2 | Architecture, NodaTime & MediatR Conventions | ⏸ Pending |
| 3 | Static Analysis, Sonar & Security Checks | ⏸ Pending |
| 4 | Docker, Contract & Test Coverage validation | ⏸ Pending |

> **Legend**: ✅ Done · 🚧 In progress · ⏸ Pending · 🔒 Blocked

> **Commit messages**: Conventional Commits (`feat:`, `docs:`, `chore:`, `test:`, `fix:`). Short subject, ≤50 chars, imperative mood, no trailing period.

> **Update rule**: **on every phase completion, the plan MUST be updated in the same pair of commits as the phase work (a code commit + a plan commit — see [How to use this template](#how-to-use-this-template)).** The plan is the source of truth for what was decided and what shipped.

---

## 0. Skill & documentation conventions

### 0.1 Coding standards mandate
> **All implementation work on this plan MUST follow the project conventions defined in `AGENTS.md`** (repository root). All helper scripts and changes to `phase-guard.ps1` must be written in PowerShell Core (`pwsh`) and execute in under 30 seconds when the `-Quick` switch is active.

### 0.2 Code-quality guard rails
- **Quality Gate Integration**: The orchestrator script [scripts/phase-guard.ps1](file:///C:/Users/omar_/Source/Repos/kalaa/orderly/OrderlyMicroservices/scripts/phase-guard.ps1) must be executed at the end of every phase with the `-Quick` parameter to verify the build, unit tests, nullability, Dockerfiles, and dependencies.
- **AST and Grep Parsers**: For domain convention checks (NodaTime usage, MediatR folders), the script should rely on lightweight regex/grep or Roslyn analyzer tools rather than spawning heavy MSBuild tasks, keeping local execution fast.

---

## 1. Context

The existing quality gate script `phase-guard.ps1` has been updated to compile the correct solution, run tests with TRX logging, lint Dockerfiles using Hadolint (only failing on errors), and run NuGet vulnerability scans. However, as the codebase grows, we face key risks of structural drift:
1. **Formatting & Roslyn Drift**: Unformatted code and missing style constraints slip through.
2. **Domain/Conventions Violations**: Banned system APIs (e.g. `DateTime.Now`) bypass NodaTime mandates; MediatR handlers drift outside feature folder boundaries.
3. **Security Vulnerabilities**: Hardcoded secrets and package security issues could easily leak.
4. **Contract & Docker Inconsistencies**: Docker-Compose healthchecks could be missing; OpenAPI specifications can drift from the actual code contracts.

---

## 2. Goal

Expand `phase-guard.ps1` to orchestrate 18 quality checks:
1. **Formatting & Analyzers**: StyleCop.Analyzers integration, `dotnet format` enforcement.
2. **Security**: Regex secret scanner, static analysis via `DevSkim` or `dotnet-security-audit`, OWASP Dependency-Check.
3. **Architecture**: NodaTime checker, MediatR folder layout verifier, NuGet licensing compliance.
4. **Environment & Verification**: Docker-Compose healthcheck parsing, OpenAPI schema validation, test coverage threshold check, Actionlint workflow validation.

---

## 3. Out of scope

- Setting up or host-managing a SonarQube/SonarCloud server instance (the script will only run the client-side `dotnet-sonarscanner` CLI).
- Enforcing check gates on external database schema updates (restricted only to the C# application repository scope).

---

## 4. Tech decisions

| Decision | Choice | Reason |
| :--- | :--- | :--- |
| **NodaTime Check** | Custom AST Regex Parser | Scans C# files for forbidden types/members (`DateTime`, `DateTimeOffset`) in milliseconds without executing compilation. |
| **MediatR Convention Check** | Directory-to-Namespace Parser | Regex walker mapping C# handler namespaces directly to their folder hierarchies. |
| **Analyzers Enforcement** | StyleCop.Analyzers in Directory.Build.props | Automatically applies rules across all microservice projects uniformly. |

---

## 5. Folder layout

```
scripts/
├── phase-guard.ps1            # Main entry point (orchestrator)
└── quality-helpers/           # Helper scripts for specialized scans
    ├── check-nodatime.ps1     # NodaTime restriction validator
    ├── check-mediatr.ps1      # CQRS/MediatR layout validator
    ├── check-licensing.ps1    # NuGet license checker
    └── check-complexity.ps1   # Complexity analyzer
```

---

## 6. Specification

### 6.1 Formatting & Style Check
*   **Roslyn / StyleCop**: Add `StyleCop.Analyzers` to a shared `Directory.Build.props` at the root of `orderly-microservices/`. Any StyleCop warning will be treated as an error by passing `-warnaserror` during the quality gate build.
*   **Code-style / Formatting**: Execute `dotnet format --verify-no-changes` to enforce alignment with the repo's `.editorconfig`.

### 6.2 Security Scans
*   **Secret-leak Scan**: Search for high-entropy strings, base64 strings, private keys, and connection strings using a pattern regex helper `scripts/quality-helpers/find-secrets.ps1`.
*   **Security Static Scan**: Run `dotnet-security-audit` or `DevSkim` CLI on C# projects to find unsafe API usage (e.g. vulnerable encryption algorithms).
*   **OWASP Dependency-Check**: Integrate `dotnet list package --vulnerable` combined with OWASP Dependency-Check CLI (if available) to search for CVEs.

### 6.3 Architectural Rules
*   **NodaTime usage check**: Block raw usage of `System.DateTime`, `System.DateTimeOffset`, and `System.TimeZoneInfo` outside of third-party serialization configuration. Developers must use NodaTime's `Instant`, `LocalDate`, etc.
*   **MediatR / CQRS conventions**: Enforce that command/query request handlers are contained within the corresponding `Features/` directory structure and match namespace naming conventions (e.g., `Catalog.API.Features.Category.CreateCategory`).

### 6.4 Environment & Contract Checks
*   **Docker-Compose health-check validation**: Run a parser on `docker-compose.yml` to assert that every service contains a valid `healthcheck` block.
*   **OpenAPI contract validation**: Validate the output OpenAPI JSON specification files against OpenAPI schemas using a linting CLI tool (e.g. `vacuum` or `spectral`).
*   **Test coverage threshold**: Read coverage reports (Cobertura `.xml` generated by `dotnet test --collect:"XPlat Code Coverage"`) and fail if the overall line coverage is below a target threshold (e.g., `80%`).

---

## 7. Integration Points

*   **Git-hook sanity**: Verify that a pre-commit git hook points to `scripts/phase-guard.ps1` so developers cannot bypass checks locally.
*   **CI-pipeline config lint**: Lint `.github/workflows/*.yml` files using `actionlint` or a schema validator.

---

## 8. Security guardrails

> [!CAUTION]
> Private tokens, connection strings, or certificates must never be hardcoded in any file. The Secret-leak scan must block phase validation if any potential secret is found.

| Risk | Mitigation |
|---|---|
| Committing active connection strings | Secret-leak scan checks for database passwords and keys before commit |
| Unpatched NuGet packages | Dependency vulnerability scan runs on every phase |

---

## 9. Development Phases

### Phase overview

| Phase | Name | Tool groups delivered | Goal |
|:---:|---|---|---|
| **1** | Formatting, Style & Secret Scanning | Formatting, Roslyn, Secret scanner, Spell-checker | Enforce code layout, formatting rules, and search for hardcoded secrets. |
| **2** | Architecture, NodaTime & MediatR Conventions | NodaTime rules, CQRS layout, Licensing checker, Git-hooks | Enforce domain layout constraints and NodaTime types. |
| **3** | Static Analysis, Sonar & Security Checks | SonarQube, OWASP Dependency Check, Complexity gate | Run deep code audits using SonarQube and OWASP checkers. |
| **4** | Docker, Contract & Test Coverage validation | Docker healthchecks, OpenAPI schema, Coverage parsing, CI lint | Ensure system configurations, OpenAPI schema files, and test coverage thresholds are met. |

### Phase 1 — Formatting, Style & Secret Scanning
**Goal**: Enforce code layout, formatting rules, and search for hardcoded secrets.
**Status**: ⏸ Pending
**Deliverables**:
- [ ] Add `Directory.Build.props` configuration for StyleCop.Analyzers and Roslyn.
- [ ] Implement `dotnet format --verify-no-changes` step in `phase-guard.ps1`.
- [ ] Create a local secret scanner (`find-secrets.ps1`) targeting high-entropy strings and keys.
- [ ] Write a script comment spellchecker step (skipping code identifiers).

### Phase 2 — Architecture, NodaTime & MediatR Conventions
**Goal**: Enforce domain layout constraints and NodaTime types.
**Status**: ⏸ Pending
**Deliverables**:
- [ ] Add regex/AST scanner for `DateTime.Now`, `DateTimeOffset`, and `TimeZoneInfo` to enforce NodaTime usage.
- [ ] Write directory-to-namespace mapper for MediatR queries/commands to prevent CQRS code layout drift.
- [ ] Implement `check-licensing.ps1` to ensure all NuGet dependencies have permissible licenses (e.g. MIT, Apache-2.0).

### Phase 3 — Static Analysis, Sonar & Security Checks
**Goal**: Run deep code audits using SonarQube and OWASP checkers.
**Status**: ⏸ Pending
**Deliverables**:
- [ ] Add `SonarScanner` local analysis trigger (if scanner is installed) using `dotnet-sonarscanner`.
- [ ] Integrate local static security scanners (`DevSkim` or `dotnet-security-audit`).
- [ ] Integrate OWASP Dependency-Check CLI runner for deeper third-party package checks.
- [ ] Add a step to calculate Cyclomatic Complexity and reject code exceeding a complexity threshold (e.g. index < 70 or complexity > 15 per method).

### Phase 4 — Docker, Contract & Test Coverage validation
**Goal**: Ensure system configurations, OpenAPI schema files, and test coverage thresholds are met.
**Status**: ⏸ Pending
**Deliverables**:
- [ ] Parse `docker-compose.yml` to verify `healthcheck` commands and interval timings are present.
- [ ] Add OpenAPI contract schema linting step.
- [ ] Parse `coverage.cobertura.xml` files generated during tests and enforce an `80%` minimum code coverage gate.
- [ ] Validate GitHub Actions workflow files (`actionlint`).

---

## 10. Technical considerations

- **CI/CD Speed**: To prevent long pipeline wait times, all heavy checks (like SonarQube, deep OWASP dependency checks) should be skipped when the `-Quick` switch is active.
- **Analyzers Noise**: StyleCop/Roslyn rule sets must be tuned using a global `.editorconfig` to avoid formatting wars.

---

## How to use this template

1. **Copy** this file into `.agents/plan/<your-project>.md` (the `_` prefix on `_template.md` keeps it out of the plan list).
2. **Find-and-replace** the `{{...}}` placeholders. Most projects need Sections 0–9; Section 10 is optional but recommended.
3. **Bump the version** in the Status section to `v{{MAJOR}}.{{MINOR}}` and add a Changelog entry every time the plan changes — see [Plan versioning](#plan-versioning) below.
4. **For each phase**, copy the "Phase {{N}}" subsection before starting work. After completion, append a new "Phase {{N}} implementation notes ({{DATE}})" section using the same structure.
5. **Commit messages** convention goes in the Status section. The whole plan is the source of truth for what was decided — keep it current.
6. **Drift between the plan and the code is the bug class plans exist to prevent.** When implementation reveals the plan was wrong (schema different than expected, API behaves differently), update the plan *and* the code in the same commit.

### The phase-completion workflow

> [!IMPORTANT]
> **Quality Gate Constraint:** Before finalizing any phase or committing code, the agent MUST run the quality gate script from the repository root:
> `pwsh ./scripts/phase-guard.ps1 -PhaseName "Phase Name" -Quick`
> The agent MUST wait for this execution to finish and verify that the script successfully exits with code `0`. The phase is NOT complete unless the script passes.

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

### v1.0 (2026-08-04) — initial draft
- Created quality gate enhancement plan with 4 phases.
- Sections 0-9 drafted; Section 10 technical considerations review appended.
