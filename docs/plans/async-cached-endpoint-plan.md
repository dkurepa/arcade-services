# Plan: Async cached-result API endpoint

## Goal
A new PCS API endpoint that returns an already calculated result from Redis.
If the result isn't cached, it is recalculated asynchronously by a work item, and the caller is told to retry later.

## Endpoint behavior

A normal request reads the `outcome` key once (`GET`):

| `outcome` value | Response |
|---|---|
| `succeeded` + result | `200 OK` + result |
| `failed` + error | Error response (problem details) with the failure reason |
| missing | `SET NX` on `pending`; if acquired, enqueue the work item. `202 Accepted` + `Retry-After` either way |

No HTTP caching headers. The result is returned straight from the cache.

### Force recalculation flag
An optional query flag (e.g. `?forceRecalculation=true`) ignores `outcome` and requests a new work item:

| `pending` state | Response with flag |
|---|---|
| present | `202 Accepted` + `Retry-After` (no new work item; one is already running) |
| missing | Acquire `pending` (`SET NX`), delete `outcome`, enqueue work item, `202 Accepted` + `Retry-After` |

- Deleting `outcome` means no caller is served a result that is being recalculated. Until the new result is written, requests without the flag get `202` as well.
- Trade-off: if the recalculation fails, there is no result until a later recalculation succeeds.
- `outcome` is deleted only after `pending` is acquired. Otherwise a request without the flag could see an empty cache and queue a second work item.

## Redis keys (per request key `{k}`)
- `{prefix}:outcome:{k}`: `{ "status": "succeeded", "result": ... }` or `{ "status": "failed", "error": ..., "failedAt": ... }`. Both expire after **1 day**. If missing (never calculated, expired, or evicted), it is recalculated.
- `{prefix}:pending:{k}`: marker that a work item is queued or running.
- `{k}` is a hash of all inputs that determine the result.
- `pending` is a separate key because `SET NX` needs a key that is empty exactly when no work is running.

## 1. Deduplication
- On a miss (or forced request): `SET {prefix}:pending:{k} 1 NX EX <pendingTtl>`.
- Enqueue the work item only if the `SET NX` succeeded; otherwise just return `202`.
- `pendingTtl` must exceed the worst-case total processing time across all retries
  (`MaxWorkItemRetries` attempts + queue visibility delays), so a crashed worker can't leave callers waiting forever.

## 2. Failure handling
- PCS already retries work items: `WorkItemConsumer` re-processes until `DequeueCount >= MaxWorkItemRetries` (default 3).
- Intermediate failures: `pending` stays, so callers keep getting `202`. This absorbs transient errors.
- Final attempt failure: write a `failed` outcome (error, failedAt) with a 1-day TTL, then delete `pending`.
- While the `failed` outcome exists: return the failure; do **not** re-enqueue on each poll (avoids retry storms).
- After it expires: the next request is a normal miss and triggers a deduplicated re-enqueue.

## 3. Result storage
- On success: write a `succeeded` outcome (1-day TTL, overwriting any previous value), then delete `pending`.
- `outcome` is written before `pending` is deleted, so a request in between never sees both missing and queues a duplicate.
## 4. Work item: commit inclusion in a build

The work item decides whether each requested commit is included in a given Maestro (BAR) build.

### Required behavior
- Accept one Maestro build id and a list of repository/SHA pairs.
- **AzDO only (for now):** all repositories, inputs and clones are Azure DevOps. GitHub URLs are not supported.
- For a VMR build, accept only the VMR's AzDO repository as input, and check the requested commits against the build's VMR commit.
- For a non-VMR build, use the same build graph as BarViz to find the build commit for each requested repository, ignoring released builds.
- If a target repository resolves to multiple distinct commits, stop evaluation and report an incoherent graph with the conflicting builds and commits; do not choose one arbitrarily or return partial inclusion results.
- Use codeflow's existing VMR/repository preparation classes and the existing local git abstractions, rather than introducing separate clone management or invoking git directly.
- Process the work item on the codeflow queue because it operates on repositories on disk.
- The concrete types, helper names, and API calls below are implementation proposals to verify against the existing code before implementation.

### Input
- One BAR build id plus a list of `(repository, sha)` pairs.
- Request key: `{k}` = SHA-256 hash of the normalized inputs:
  1. Normalize: lowercase shas, normalize repository URLs the same way as "Repository URL matching", sort the `(repository, sha)` pairs, and remove duplicates.
  2. Hash the build id plus the normalized list.
- Repository URLs must be valid AzDO repository URLs (`GitRepoUrlUtils.ParseTypeFromUri` returns `AzureDevOps`). GitHub or other URLs return `400`.
- If any input is invalid, the API returns `400` and enqueues nothing.

```json
{ "buildId": 284512, "commits": [ { "repository": "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime", "sha": "4f1c2e9" } ] }
```

### Queue and types
- Work item: `BuildCommitInclusionWorkItem : WorkItem { required int BuildId; required string RequestKey; required IReadOnlyList<RepositoryCommit> Commits; }` in `ProductConstructionService.DependencyFlow/WorkItems/`.
- Processor: `BuildCommitInclusionProcessor` in `ProductConstructionService.DependencyFlow/WorkItemProcessors/`, registered in `DependencyFlowConfiguration.cs`.
- Enqueued with `IWorkItemProducerFactory.CreateProducer<BuildCommitInclusionWorkItem>(IsSpecialQueue: true)`. That sends it to the codeflow queue (`pcs-codeflow-workitems`), because the work item uses clones on local disk.
- The codeflow queue has one consumer per replica, so the work item never runs at the same time as codeflow on the same clone folders. No synchronization key is needed; Section 1 deduplicates requests.
- The build is loaded from `BuildAssetRegistryContext`. A missing build throws `NonRetriableException`.

### Repository URL matching
- AzDO URLs are compared after trimming a trailing `/` and `.git`, case-insensitively. Both AzDO URL forms (`dev.azure.com/dnceng/...` and `dnceng.visualstudio.com/...`) are normalized to the `dev.azure.com` form.
- A BAR build matches a repository only by its `AzureDevOpsRepository`. Builds without one never match.
- Don't use `Build.GetRepository()`: it returns `GitHubRepository` first.
- New helper: `RepositoryUrlComparer` in `ProductConstructionService.DependencyFlow`.

### Case A: VMR build
- The build is a VMR build when its `AzureDevOpsRepository` is the VMR's AzDO repository (`dnceng/internal/_git/dotnet-dotnet`). A root build without `AzureDevOpsRepository` throws `NonRetriableException`.
- Every requested repository must also be the VMR. The API rejects other repositories with `400` before enqueueing, and the processor repeats the check and throws `NonRetriableException`.
- Prepare the VMR the same way codeflow does: `IVmrCloneManager.PrepareVmrAsync([build.AzureDevOpsRepository], [build.Commit], build.Commit, resetToRemote: false, ct)`.
  - Passing only the AzDO remote keeps a single remote on the clone.
- Check each requested sha with the returned `ILocalGitRepo` (see "Commit check").

### Case B: non-VMR build (walk the build graph)
- Load the graph BarViz shows: `BuildAssetRegistryContext.GetBuildGraphAsync(build.Id)`, which backs `GET /api/builds/{id}/graph`.
- Walk the graph from the root along product dependency edges only, matching BarViz's default view.
- Ignore released builds (`Build.Released`). Verify BarViz's traversal behavior before implementation, including whether it skips their subtrees. Do not silently exempt a released root build from this rule.
- For each requested repository, collect the non-released builds of that repository in the graph (using "Repository URL matching"). The root counts if it is the same repository.
- The whole non-released graph is always walked; the walk never stops at the first matching build. A deeper build of the same repository (e.g. runtime under aspnetcore) can have a different commit, and only a full walk finds it.

| Builds of the target repository | Result for that repository |
|---|---|
| none | `RepositoryNotInBuild` |
| one, or several with the same `Commit` | resolved to that build and its commit |
| several with different commits | Stop evaluation and return `IncoherentGraph`, listing the conflicting `(buildId, commit)` pairs; do not perform inclusion checks |

- Example: the SDK depends on runtime directly and on aspnetcore, which also depends on runtime. If the runtime under aspnetcore has a different commit than the SDK's direct runtime, the graph is incoherent for runtime.
- The same commit from different build ids is coherent.
- Coherency is checked only for the requested repositories; mismatches in other repositories don't affect the result.

- Resolve all requested repositories before starting git inclusion checks, so an incoherent graph does not produce partial inclusion results.
- Graph resolution is a pure class, `BuildGraphRepositoryResolver`, so its cases can be unit tested without git.
- Prepare each resolved repository the same way forward flow does: `IRepositoryCloneManager.PrepareCloneAsync(resolvedBuild.AzureDevOpsRepository, resolvedBuild.Commit, resetToRemote: false, ct)`.
- Check each requested sha of that repository against `resolvedBuild.Commit`.

### Commit check (both cases)
- Use the `ILocalGitRepo` returned by the clone manager.
- `GetObjectTypeAsync(sha)` is called first, because `IsAncestorCommit` throws on unknown objects. If the type is not `Commit`, the result is `CommitNotFound`.
- Otherwise `IsAncestorCommit(repoPath, ancestor: sha, descendant: buildCommit)` (`git merge-base --is-ancestor <sha> <buildCommit>`) returns `Included` or `NotIncluded`. Equal commits count as `Included`. A build contains a commit only when that commit is in the history of the build's commit, so the requested sha must be the ancestor; the reverse check would report commits newer than the build as included.
- Clones only fetch with `git remote update`. A sha that is reachable only from a PR ref, or only on the other remote, is reported as `CommitNotFound`.

### Result (stored in `{prefix}:outcome:{k}` as a `succeeded` outcome)
```json
{
  "buildId": 284512,
  "isVmrBuild": false,
  "repositories": [
    { "repository": "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime", "status": "Resolved",
      "resolvedBuildId": 284390, "resolvedCommit": "9ab01c3",
      "commits": [ { "sha": "4f1c2e9", "status": "Included" } ] }
  ]
}
```
- For a VMR build, `resolvedBuildId` is the build itself.
- `RepositoryNotInBuild` and `IncoherentGraph` are valid, cached results, not failures.
- An incoherent-graph response reports the conflicts instead of inclusion results.

### Failures
- Git and network errors are thrown so the work item is retried. A catch-all `return false`, as in `BackflowStatusCalculationProcessor`, would drop the message without a retry.
- On `IsFinalAttempt()` or a `NonRetriableException`, write a `failed` outcome and delete `pending` (Section 2), then rethrow.

### Tests
- `BuildGraphRepositoryResolverTests`:
  - single match;
  - the root is the target repository;
  - both AzDO URL forms match; GitHub URLs are rejected;
  - builds without `AzureDevOpsRepository` never match;
  - released builds and their subtrees are skipped;
  - several matching builds with the same commit;
  - incoherent graph;
  - an incoherent target stops inclusion checks for the entire request;
  - repository not in the build;
  - toolset edges are ignored.
- `BuildCommitInclusionProcessorTests` (patterned on `BackflowStatusCalculationProcessorTests`, mocking the clone managers and `ILocalGitRepo`):
  - VMR path;
  - non-VMR path;
  - `CommitNotFound`;
  - a non-VMR repository with a VMR build is non-retriable;
  - a missing build;
  - a final-attempt failure writes a `failed` outcome.
