---

# Document control

## Revision history

| Version | Date | Author | Changes |
| --- | --- | --- | --- |
| 1.0 | 2026-04-29 | Mike King (KingOfTheGeeks) | Initial issue. Covers all surfaces shipped to date: Identity / WebApi / BlazorServer / Migrator hosts; Master, per-tenant, and Identity DB tiers; full audit pipeline (per-entity tile + admin feeds); calibration in-portal pipeline; Heimdall licensing wizard; tenant member management; cross-tenant isolation. |
| 1.1 | 2026-05-06 | Mike King (KingOfTheGeeks) | Audit pass against `main` HEAD `c3b589a`. No procedural changes; verified every section against the deployed staging build at `https://dev.sdiamr.com/`. Cross-references to SOP-002 / SOP-003 / SOP-004 / SOP-005 confirmed current. |

## Change-control protocol

Updates to this SOP follow the standard procedure-change rules:

1. Every code change that alters a tested surface (page route added /
   removed, endpoint behaviour changed, lifecycle transition modified,
   etc.) **requires** a corresponding update to the relevant test
   row(s) in the same pull request.
2. Adding or removing a test section bumps the SOP minor version
   (1.0 → 1.1). Renumbering or renaming sections bumps the major
   version (1.x → 2.0).
3. Every SOP version is tagged in source control alongside the Enki
   release it covers — so the SOP that was used to validate a given
   release can be retrieved by checking out that release tag.

## Storage and distribution

The authoritative source of this SOP is the markdown set in the Enki
repository (`docs/test-plan.md` — body content; `docs/sop-cover.md`
and `docs/sop-back.md` — SOP framing). The compiled `.docx` artefact
(`docs/sop-enki-validation.docx`) is regenerated from those sources at
release time and distributed to QA / release managers / customer
acceptance teams as needed.

Print copies are uncontrolled. The source-of-truth is the version in
the repository tagged for the release under test.

## Approval

By signing below, the approver attests that this SOP correctly
describes the validation procedure for the Enki release identified by
the version and date on the cover page.

| Role | Name | Signature | Date |
| --- | --- | --- | --- |
| Document Owner | Mike King | _________________ | __________ |
| Engineering Lead | _________________ | _________________ | __________ |
| Release Manager | _________________ | _________________ | __________ |

---

*End of SOP.*
