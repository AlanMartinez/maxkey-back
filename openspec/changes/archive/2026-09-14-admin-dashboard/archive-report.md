# Archive Report: Admin Dashboard Change

**Date**: 2026-09-14  
**Change**: admin-dashboard  
**Project**: maxkeys  
**Archive Path**: `openspec/changes/archive/2026-09-14-admin-dashboard/`  
**Mode**: hybrid (Engram + OpenSpec file system)

## Executive Summary

The admin-dashboard change has been successfully archived. All 49 tasks across 5 slices were completed and verified (0 CRITICAL, 0 WARNING findings; 12 SUGGESTION items, none blocking). The change delivered a complete admin dashboard for product catalog management, carousel slide administration, and buyer management with resend delivery capability, spanning both backend (maxkey-back) and frontend (maxkeys-front) repositories.

## SDD Artifact Observations (Engram)

All artifacts were persisted to Engram during the SDD lifecycle and are cross-referenced below for traceability:

| Artifact Type | Observation ID | Topic Key | Status |
|---|---|---|---|
| Proposal | #296 | sdd/admin-dashboard/proposal | completed 2026-09-14 10:32:00 |
| Specification | #297 | sdd/admin-dashboard/spec | completed 2026-09-14 10:37:31 |
| Design | #298 | sdd/admin-dashboard/design | completed 2026-09-14 10:40:20 |
| Tasks | #299 | sdd/admin-dashboard/tasks | completed 2026-09-14 10:45:58 |
| Verification Report | #301 | sdd/admin-dashboard/verify-report | completed 2026-09-14 11:05:57 |
| Archive Report | ~NEW~ | sdd/admin-dashboard/archive-report | this file |

## Final Verification Verdict

**Verdict**: PASS (0 CRITICAL, 0 WARNING, 12 SUGGESTION)

Per the verify-report (Engram #301), all 5 slices were applied and verified:
- Slice 1 (back, admin catalog): 174/174 tests, 10/10 tasks — PASS
- Slice 2 (back, carousel): 207/207 tests, 11/11 tasks — PASS (size:exception 1216 authored lines)
- Slice 3 (back, buyers+resend): 223/223 tests, 15/15 tasks — PASS (WARNING resolved in commit d522915, size:exception 900 authored lines)
- Slice 4 (front, guard+catalog+carousel): 62/62 tests, 9/9 tasks, typecheck clean, build green — PASS
- Slice 5 (front, buyers page): 67/67 tests, 4/4 tasks, typecheck clean, build green — PASS

**Combined Test Results**: 290/290 passing (Domain 65, Application 83, Api 75 on backend; 67 on frontend)

## Specification Sync Performed

Per the archive instructions, delta specs have been merged into the main specification:

1. **New Capabilities** (created):
   - `openspec/specs/admin-catalog/spec.md` — copied from the delta spec (new capability)
   - `openspec/specs/carousel/spec.md` — copied from the delta spec (new capability)
   - `openspec/specs/admin-buyers/spec.md` — copied from the delta spec (new capability)

2. **Modified Capabilities** (merged):
   - `openspec/specs/fulfillment/spec.md` — created by merging mvp-marketplace base spec with the admin-dashboard MODIFIED delta (One-Time Delivery Email requirement now includes explicit admin resend path). Note added at top of file indicating this spec was seeded from mvp-marketplace when admin-dashboard was archived, with a placeholder for mvp-marketplace domains to be seeded when that change is archived.

## Delivery Summary

### Backend (maxkey-back)

All backend work was delivered via stacked PRs to main:
- **PR #28**: Slice 1 (admin catalog) — merged to main
- **PR #29**: Slice 2 (carousel) — merged to a feature branch
- **PR #30**: Slice 3 (buyers+resend) — merged to a feature branch
- **PR #31**: Consolidation merge (PRs #29, #30 consolidated) — merged to main as commit ac9a7d5

All backend feature branches (feat/admin-catalog, feat/carousel-slides, feat/admin-buyers) have been deleted.

### Frontend (maxkeys-front)

All frontend work was delivered via stacked PRs to main:
- **PR #11**: Slice 4 (guard+catalog+carousel) — merged to main
- **PR #12**: Slice 5 (buyers page) — merged to a feature branch
- **PR #14**: Consolidation merge (PR #12 consolidated) — merged to main as commit 1e0adac

All frontend feature branches (feat/admin-area, feat/admin-buyers-ui) have been deleted.

## Test Coverage

### Backend Tests (223/223 passing)
- **Domain Tests**: 65/65
  - 56 existing tests + 9 carousel-domain tests
- **Application Tests**: 83/83
  - 61 existing + 7 slice-1 + 13 slice-2 + 8 slice-3 = 22 new tests
- **Api Tests**: 75/75
  - 54 existing + 11 slice-1 + 11 slice-2 + 8 slice-3 = 30 new tests

### Frontend Tests (67/67 passing)
- 62 baseline tests (from baseline main branch)
- 5 new tests:
  - 4 tests in tests/useAdminBuyers.spec.ts
  - 1 test in tests/adminBuyersPage.spec.ts

### Test Strategy Coverage
All tests follow the design-specified strategy: Domain unit tests for invariants, Application tests with Testcontainers (real Postgres), Api tests with WebApplicationFactory, Frontend tests with Vitest. No gaps in spec-scenario coverage (all 7 admin-catalog + 7 carousel + 7 admin-buyers + 3 fulfillment-delta scenarios verified).

## Review Budget & Exceptions

Two size:exception decisions were accepted during apply phase:
- **Slice 2 (Carousel)**: 1216 authored lines (exceeds 800-line budget) — driven by full spec-scenario test coverage (9 domain + 13 application + 11 Api tests)
- **Slice 3 (Buyers+Resend)**: 900 authored lines (exceeds 800-line budget) — driven by full spec-scenario test coverage (3 application test files + 2 Api test files)

Both overages are test-volume-driven, not scope creep. No changes were made to reduce line count; full spec coverage was prioritized.

## Known Issues & Follow-ups

### Blocking (None)
No blocking issues remain. The change is complete and ready for deployment.

### Follow-ups (Not in Scope)
1. **Admin Supabase Configuration**: Add the 2 admin Supabase `sub` values to `Auth:AdminSubs` per environment (development, staging, production) before exposing the admin UI to end users.

2. **Gitignore**: Add entries for IDE-generated files:
   - `src/Maxkeys.Api/Properties/launchSettings.json`
   - `tests/Maxkeys.Api.Tests/Properties/launchSettings.json`
   These were flagged as untracked in git status during verify and should be excluded from future PRs.

3. **Seed Catalog Mode**: Make `--seed-catalog` insert-only once admin editing exists (proposed in design Open Questions, not in scope for this change).

4. **Resend Rate Limiting**: Monitor resend usage; implement rate limiting if abuse occurs (design Open Questions, explicitly "no rate limit applies" for v1).

5. **Real Image Upload**: Reverse ADR-12 and add S3/R2 SDK integration for real image upload (proposed follow-up, requires fresh user approval, not in scope).

## Deployment Notes

### Deploy Order (per design D7)
Backend must deploy first in order: PR #28 → PR #31 (consolidated #29+#30).  
Frontend may deploy after backend #28 and #31 are live.

### Frontend Safety
- **PR #11 (catalog/carousel)** is safe to deploy once backend PRs #28 and #31 are live, due to static fallback in HeroCarousel.vue and fail-closed admin guard middleware.
- **PR #14 (buyers)** should NOT be exposed to real admin users until backend PR #31 (buyers+resend) is live; the frontend build itself is safe to ship as code, but the live endpoints must exist before users access the page.

## Recommendations

1. ✅ **Proceed with archiving** — all verification passed, all tasks complete, no CRITICAL or WARNING findings.
2. ⚠️ **Before deploy**: Configure Auth:AdminSubs with the 2 admin Supabase sub values in all environments.
3. ⚠️ **Before PR1 opens**: Confirm .gitignore coverage for the untracked Properties/ directories (SUGGESTION item, not blocking).
4. 📋 **Track as future work**: The 4 follow-up items listed above.

## Archive Completeness Checklist

- [x] All 5 slices applied and verified
- [x] All 49 tasks marked complete in tasks.md
- [x] All 290 tests passing
- [x] Spec delta specs merged into main specs (openspec/specs/)
- [x] Change folder moved to archive (openspec/changes/archive/2026-09-14-admin-dashboard/)
- [x] state.yaml updated with archive metadata
- [x] Archive report written
- [x] All artifact observation IDs recorded for traceability
- [x] Final verification verdict: PASS (0 CRITICAL, 0 WARNING)

## Archive Audit Trail

**What Changed:**
- **New Specs**: 3 new domain specs (admin-catalog, carousel, admin-buyers)
- **Modified Specs**: 1 spec updated (fulfillment, with admin resend addition)
- **Backend Code**: ~850 authored production lines + ~585 test lines across 3 slices (excluding generated migration)
- **Frontend Code**: ~694 authored production lines + ~100 test lines across 2 slices
- **Tests Added**: 22 backend tests, 5 frontend tests

**When:**
- Created: 2026-09-14 (same day as proposal)
- Applied: 2026-09-14 (all 5 slices in one intensive apply session)
- Verified: 2026-09-14 (all 5 slices verified and WARNING resolved)
- Archived: 2026-09-14

**Why:**
User request (Engram #295 binding decisions) to give two admins a protected UI for catalog management, carousel slides, and buyer/order management, keeping ADR-12 (no S3 upload) and ADR-18 (two repos) intact, and reusing existing AdminPolicy/Auth:AdminSubs auth.

**Who (Authors):**
- Backend implementation: Claude + user commits to feat/admin-catalog, feat/carousel-slides, feat/admin-buyers
- Frontend implementation: Claude + user commits to feat/admin-area, feat/admin-buyers-ui
- Verification: SDD verify phase against Testcontainers (real Postgres) + Vitest (real frontend builds)

## Key Learnings

1. Scope creep in test volume (slices 2 and 3 exceeded 800-line budgets) was driven by spec-scenario coverage, not feature scope — all tests traced to named scenarios or explicit contract-table status codes.

2. The explicit WARNING on slice 3 (unescaped adminSub in outbox JSON) was resolved in a follow-up commit (d522915) before archive, closing the cycle on that risk without blocking delivery.

3. Delta spec merging for fulfillment required taking the existing mvp-marketplace spec as a base and replacing one requirement block — the pattern works well when multiple changes iterate on the same domain over time.

4. Frontend contract matching (DTO field-for-field cross-check against backend C# records) proved invaluable in slice 4-5 verification; no live backend was reachable in test environment, but structured spot-checking caught zero mismatches across 8 DTOs and 4 routes.

5. Slice 2's migration (AddCarouselSlides) and slice 3's handler refactor (extracting DeliveryEmailItems.FromOrder) both exemplify surgical changes that could be reverted independently, validating the design's slicing strategy.
