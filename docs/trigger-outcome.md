-----------------------------------------------------------------------------------------------------------------------------------------

 # Plan: Reusable subscription trigger-outcome error component (issue #6334)
 
 Adds a reusable component surfacing subscriptions whose most recent trigger
 outcome was an error (`Failure`) or user error (`UserError`):
 - An **error table** rendered above the existing grid.
 - An **alert icon** on the relevant rows.
 
 Used on both the **Subscriptions** and **Codeflows** pages. Delivered in two
 PRs (one per page) for easier review.
 
 ## Background / existing contracts
 - API `SubscriptionTriggerOutcomesController` (`GET /api/subscription-trigger-outcomes`)
   already returns outcomes filtered by `subscriptionId`/type, ordered by date desc.
 - `OutcomeType` already includes `Failure` and `UserError`
   (recorded by `SubscriptionUpdateOutcomeRecorder`).
 - The generated client already exposes `ListSubscriptionOutcomesAsync`.
 - `CodeflowController` builds the whole Codeflows page in one request;
   `CodeflowSubscriptionStatus` already wraps Subscription + PR + build info.
 - BarViz has NUnit helper/extension tests but **no bUnit component-render harness**.
 
 Shared helper: a single internal EF "latest-outcome-per-subscription-id" query
 should back both backend changes below (avoid duplication).
 
 ---
 
 ## PR 1 — Subscriptions page
 
 ### Backend
 - Add `GET /api/subscription-trigger-outcomes/latest` to
   `SubscriptionTriggerOutcomesController`: takes a set of subscription IDs,
   returns the latest outcome per id (EF: `SubscriptionId IN (...)`, group by
   `SubscriptionId`, max `Date`). Newtonsoft camelCase / ISO-8601-UTC.
 - ⚠️ **Regenerate `Microsoft.DotNet.ProductConstructionService.Client`**
   (`generate-client.cmd` → `dotnet msbuild /t:GenerateSwaggerCode`); commit `Generated/`.
 
 ### Front-end (BarViz)
 - `SubscriptionOutcomeAlertIcon.razor` (presentational): nullable
   `SubscriptionTriggerOutcome`; renders warning icon + tooltip (`Message`)
   only when `Type is Failure or UserError`.
 - `SubscriptionErrorBanner.razor` (presentational): takes subscriptions +
   `Dictionary<Guid, SubscriptionTriggerOutcome>` (errors only); renders nothing
   when empty, else a `GridViewTemplate` grid (repo, channel, `Type` badge,
   `Message`, `Date` via `ToTimeAgo()`, link to `SubscriptionDetailDialog`).
 - Static helper for filter/dedupe logic (unit-testable).
 - `Subscriptions.razor`: after `AllSubscriptions` loads, call `/latest` with
   their IDs (try/catch like existing `TryLoad...`), build errors dictionary,
   pass to banner + a new alert-icon column.
 
 ### Tests
 - `ProductConstructionService.Api.Tests`: `/latest` selection, ID filtering,
   empty input, null when no outcome exists.
 - `ProductConstructionService.BarViz.Tests`: filter helper
   (Failure/UserError retained, others/none excluded). **No render tests.**
 
 ---
 
 ## PR 2 — Codeflows page
 
 ### Backend
 - Add `[JsonProperty("latestOutcome")] SubscriptionTriggerOutcome LatestOutcome`
   to `CodeflowSubscriptionStatus` (`CodeflowStatus.cs`).
 - In `CodeflowController`, batch-load latest outcome per subscription id (reuse
   the PR 1 helper; follow the existing `CalculateNewestBuildInfo` /
   `GetInProgressPullRequestsAsync` pattern) and populate it in
   `CreateSubscriptionStatus`. No extra round-trip for the page.
 - ⚠️ **Regenerate the client** (contract change to `CodeflowSubscriptionStatus`).
 
 ### Front-end (BarViz)
 - Reuse `SubscriptionOutcomeAlertIcon` in the forward/back flow cells using
   `ForwardFlow.LatestOutcome` / `Backflow.LatestOutcome`.
 - Reuse `SubscriptionErrorBanner`: build the errors dictionary from
   `CodeFlowRows` (forward + back flow, deduped by subscription id).
 
 ### Tests
 - `ProductConstructionService.Api.Tests`: codeflow `LatestOutcome` populated
   correctly / null when absent.
 - `ProductConstructionService.BarViz.Tests`: codeflow dedupe helper.
 
 ---
 
 ## Affected projects
 | Project | PR 1 | PR 2 |
 |---|---|---|
 | `ProductConstructionService.Api` | `/latest` action | `LatestOutcome` on `CodeflowSubscriptionStatus` + populate |
 | `Microsoft.DotNet.ProductConstructionService.Client` | ⚠️ regenerate | ⚠️ regenerate |
 | `ProductConstructionService.BarViz` | icon + banner + `Subscriptions.razor` | reuse on `Codeflows.razor` |
 | `test/...Api.Tests` | `/latest` tests | codeflow outcome tests |
 | `test/...BarViz.Tests` | filter helper tests | dedupe helper tests |
 
 ## Hand-off notes
 - **Implementer:** Share one EF latest-outcome helper across both PRs. Keep BarViz
   components presentational (pages fetch, pass data in) to avoid N+1; put logic
   in static helpers. Regenerate + commit the client in each PR that touches the API.
 - **Test-specialist:** No bUnit harness exists — test helpers only, not rendering.
 - **Regen flag:** ⚠️ Both PRs require regenerating
   `Microsoft.DotNet.ProductConstructionService.Client`.

-----------------------------------------------------------------------------------------------------------------------------------------