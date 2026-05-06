---
title: "Enki — System Validation"
subtitle: "Standard Operating Procedure"
author: "SDI · KingOfTheGeeks"
date: "2026-05-06"
---

# Enki — System Validation

*Last audited: 2026-05-06 against `main` HEAD `c3b589a`.*

**Standard Operating Procedure**

| Field | Value |
| --- | --- |
| Document number | SDI-ENG-SOP-001 |
| Version | 1.1 |
| Effective date | 2026-04-29 (v1.0); 2026-05-06 (v1.1 audit pass) |
| Document owner | Mike King — KingOfTheGeeks |
| Issuing organization | SDI Engineering |
| Status | Active |
| Related repo | <https://github.com/KingOfTheGeeks/Enki> |
| Reviewed by | _________________ |
| Approved by | _________________ |

---

## A. Purpose

This Standard Operating Procedure defines the manual end-to-end validation
performed against the Enki platform — the multi-host .NET 10 web application
that replaces the legacy Athena and Artemis systems for AMR drilling-data
storage, processing, and visualisation.

The SOP is the authoritative reference for confirming that a candidate
build of Enki operates correctly across every advertised feature
surface, prior to release, customer acceptance, or post-deployment
smoke-test.

## B. Scope

**In scope:** the four runnable hosts of Enki — Identity, WebApi,
BlazorServer, Migrator — together with their three database tiers
(Master DB, per-tenant Active+Archive pair, Identity DB), the cross-tenant
isolation contract, and every page / controller surface called out in
the procedure that follows.

**Out of scope:**

- **Marduk** — the computation engine consumed via `<ProjectReference>`. Validated under a separate Marduk SOP.
- **Esagila** — the field-side desktop tool that consumes Enki-issued `.lic` files. Validated under a separate Esagila SOP.
- **Nabu** — the licensing-asset / tool-management pipeline. Validated under a separate Nabu SOP.

**Audience:** SDI engineering, QA, deployment operators.

## C. Responsibilities

| Role | Responsibility |
| --- | --- |
| **Test Operator** | Walk the procedure in order; record Pass / Fail per row; raise GitHub issues for failures using the test ID in the title. |
| **Engineering Lead** | Triage failures; produce a fix or document an accepted known-issue; confirm regression coverage in the next pass. |
| **Release Manager** | Verify SOP outcome against acceptance criteria below before signing off the release / deployment. |

## D. References

- `README.md` — Enki development setup + architecture overview.
- `docs/ArchDecisions.md` — architectural decisions explaining *why* the system is shaped this way.
- `docs/deploy.md` — production deployment, configuration matrix, audit-retention defaults.

## E. Definitions

Drilling, software, and Enki-specific terminology used throughout this
procedure is collected in **§99 Glossary** at the end of this document.

## F. Pre-conditions

Before beginning the procedure, the test operator must have:

1. A fresh build of the Enki branch under test, identified by `git rev-parse HEAD`.
2. The dev rig launched per **§4 Setup + sign-in** of the procedure (`scripts/start-dev.ps1 -Reset` is the canonical clean-state command).
3. All three hosts (Identity / WebApi / BlazorServer) reporting *Now listening on …* with no startup errors.
4. The three demo tenants (PERMIAN / NORTHSEA / BOREAL) auto-provisioned and visible at `/tenants` after sign-in.

If any pre-condition fails, halt the procedure and raise a build-blocking issue.

## G. Acceptance criteria

- **Smoke pass (§6) is mandatory.** Every smoke test must be Pass before the per-feature procedure begins. A smoke failure is a build-blocker.
- **Per-feature pass.** Every test in §7 through §22 must be Pass for release sign-off, *or* explicitly documented as an accepted known-issue with a tracked backlog item.
- **Cross-tenant isolation (§22) is the highest-stakes section.** Any failure in §22 is a release-blocker regardless of severity classification — a customer seeing another customer's data is the single most catastrophic Enki defect class.

---

# Procedure

The procedure follows. Each section is structured as:

- **What you're testing** — context for the test operator.
- A table of test rows, each with an ID, the test, and a Pass column.

Test rows use the convention:

- **☐** — not yet executed.
- **☑** — Pass.
- **☒** — Fail. Raise a GitHub issue using the test ID in the title (e.g. *"TEN-12: Submit valid tenant returns 500"*).

---

