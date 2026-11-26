# Plan: `darc get-build-commit-inclusion`

## Goal
A darc command that reports whether given commits are included in a Maestro (BAR) build.

The core logic lives in DarcLib and depends only on abstractions that both darc and PCS already register. That way it can later run in the service (e.g. from a work item) without changes.

The work is computed on every run. There is no result caching, Redis, work item or API endpoint. Clones are reused through the existing clone managers.

## Command

```
darc get-build-commit-inclusion --build <buildId> <repoUrl>@<sha> [<repoUrl2>@<sha2> ...]
```

- `--build` (required, int): BAR build id. It is registered with `Add<int>("--build", ..., required: true)`, the same way other verbs do it (e.g. `TriggerSubscriptionsCommandLineOptions`).
- Positional commits (required, one or more), registered with `AddValueList(...)`.
  - Each value is split on the **last** `@`. Legacy AzDO URLs can contain user info (`https://dnceng@dnceng.visualstudio.com/...`), so a split on the first `@` would break them. Shas never contain `@`.
  - A value without `@`, or with an empty repository or sha, is a usage error.
- `--output-format`: `text` (default) and `json`.
  - Logging follows existing darc behavior: diagnostic messages go to stdout, so verbose output or warnings may precede the JSON.
- Exit code: `0` when the evaluation completes, whatever the inclusion results. It is non-zero only for invalid input or errors, following the operation conventions. Not-included commits and an incoherent graph are normal results.

Example:
```
darc get-build-commit-inclusion --build 284512 \
  https://dev.azure.com/dnceng/internal/_git/dotnet-runtime@4f1c2e9 \
  https://dev.azure.com/dnceng/internal/_git/dotnet-runtime@9ab01c3
```

## Architecture

### DarcLib (core, host-agnostic)
New folder `src/Microsoft.DotNet.Darc/DarcLib/BuildCommitInclusion/` containing:

- `IBuildCommitInclusionChecker` / `BuildCommitInclusionChecker`:
  - `Task<BuildCommitInclusionResult> CheckAsync(int buildId, IReadOnlyCollection<RepositoryCommit> commits, CancellationToken ct)`
  - Dependencies: `IBasicBarClient`, `IVmrCloneManager`, `IRepositoryCloneManager`, the ordered `IEnumerable<ICommitInclusionCheck>` (see below) and `ILogger<BuildCommitInclusionChecker>`.
  - It doesn't use `BuildAssetRegistryContext` or anything PCS-specific.
  - It handles input, graph resolution and clones, then hands each commit to the check pipeline. It knows nothing about any specific check.
- **Commit inclusion check pipeline**, built so new checks can be added without changing the checker:
  - `ICommitInclusionCheck`:
    ```csharp
    public interface ICommitInclusionCheck
    {
        string Name { get; }
        Task<bool> IsIncludedAsync(CommitInclusionCheckContext context, CancellationToken cancellationToken);
    }
    ```
  - `CommitInclusionCheckContext` is an immutable record: `ILocalGitRepo Repository`, `string Commit` (the requested sha) and `string BuildCommit`. New checks that need more data extend the record instead of changing the interface signature.
  - **Contract:**
    - A check returns `true` when it proves the commit's content is in the build, and `false` otherwise.
    - Each check must leave the clone checked out at `BuildCommit` with a clean index and working tree, even when it fails, e.g. by resetting in a `finally` block.
    - Patch-creation, cleanup, and reset errors propagate. A failed reverse `git apply` is a normal `false` result.
  - **Pipeline:** checks run in registration order and stop at the first `true`, which gives `Included`. If every check returns `false`, the result is `NotIncluded`.
    - The existence check (`CommitNotFound`) is a precondition, not a pipeline check, because it produces its own status and no check can run without the commit.
  - Initial checks, in order:
    1. `AncestorCommitInclusionCheck` (cheap)
    2. `PatchReverseApplyCommitInclusionCheck` (expensive; depends on `IVmrPatchHandler` and `IVmrInfo`)
  - Adding a check means writing one class and one registration line. The checker, models and output don't change.
  - The checker logs which check matched, at Information level, for diagnostics. The result model doesn't change and the status is still `Included`.
- `BuildCommitInclusionInput`: commit normalization and validation (pure, static).
- `BuildGraphRepositoryResolver`: finds each repository's build in the graph (pure, static, works on the client `Build` model).
- `RepositoryUrlComparer`: AzDO URL normalization and comparison.
- Result models: `BuildCommitInclusionResult`, `RepositoryInclusionResult`, `CommitInclusionResult`, `BuildCommitConflict`, and the `RepositoryInclusionStatus` / `CommitInclusionStatus` enums.
- Invalid input throws the existing `DarcException` (`DarcLib/DarcException.cs`), with all validation errors in its message. No new exception type is added.
- DI: a new `AddBuildCommitInclusion()` extension in `BuildCommitInclusion/` registers the checker (`TryAddTransient`) and the checks. The checks are registered in pipeline order with `services.AddTransient<ICommitInclusionCheck, ...>()`. `IEnumerable<T>` resolves in registration order, so this one method is the only place the order is defined.
  - The checker depends on services that `AddCodeflow` registers (clone managers, `IVmrPatchHandler`, `IVmrInfo`). The host calls `AddCodeflow(...)` first, then `AddBuildCommitInclusion()`.

### BAR client: build graph
There is no graph method on `IBasicBarClient` today. Add:

- `Task<IReadOnlyCollection<Build>> GetBuildGraphAsync(int buildId)` on `IBasicBarClient`.
- `BarApiClient` (darc) implements it with `_barClient.Builds.GetBuildGraphAsync(buildId)`, the existing generated client for `GET /api/builds/{id}/graph`, and returns `BuildGraph.Builds.Values`.
- `SqlBarClient` (PCS) implements it with `_context.GetBuildGraphAsync(buildId)` and `ToClientModelBuild`. The dependency mapping must use `DependentBuildId`, not the containing build's `BuildId`, so the SQL and API graphs have the same edges.
- Every other implementation of `IBasicBarClient`, including test fakes, must implement the new method. Find them all when implementing.

### Darc (thin host)
- `GetBuildCommitInclusionCommandLineOptions : CommandLineOptions<GetBuildCommitInclusionOperation>`, with verb `get-build-commit-inclusion`.
  - Override `IsOutputFormatSupported` to allow `json`.
  - Override `RegisterServices` to call `services.AddCodeflow(tmpPath, vmrPath: null, GitLocation)`, then `services.AddBuildCommitInclusion()`, then **`return base.RegisterServices(services)`**. The base call registers darc's AzDO/GitHub token providers and `IBasicBarClient`, which cloning internal AzDO repositories needs. Follow `GetRepoVersionCommandLineOptions.RegisterServices`.
  - `tmpPath` follows the same resolution as `VmrCommandLineOptionsBase.RegisterVmrServices`: `DARC_TMP_DIR`, else `%TEMP%/<Constants.DefaultDarcClonesDirectoryName>`, created with `Directory.CreateDirectory`. It can't be reused directly because it passes `VmrPath`, so copy the logic or extract a shared static helper.
  - **The options must not derive from `VmrCommandLineOptionsBase`.** That base defaults `VmrPath` to the current directory, and `VmrCloneManager.GetClonePath` returns `IVmrInfo.VmrPath` when it is set. The command would then fetch and check out inside whatever repository the user runs it from.
  - With `vmrPath: null`, the VMR is cloned to `<tmp>/vmrs/<hash>`. Repository clones go to `<tmp>/<hash of repo URI>`, the same places codeflow commands use.
  - Clones are reused across runs. The existing clone managers reset reused clones and may delete and re-clone an unusable clone. There is no `--vmr` option for now.
- `GetBuildCommitInclusionOperation`:
  - Parses the positional values.
  - Calls `IBuildCommitInclusionChecker.CheckAsync`.
  - Prints the result as text or JSON (Newtonsoft, as other darc operations do).
  - Catches `DarcException`, logs its message and returns a non-zero exit code, as `ResetSubmoduleOperation` does.
- Darc wiring: add the options instance to `Program.GetOptions()`; the current CLI registers verbs explicitly.
- `docs/Darc.md`: add the verb to the Index and the Command Reference, following `.github/instructions/darc-docs.instructions.md`.

## Core behavior

This section is the full spec. An earlier PCS-endpoint prototype was removed and there is no existing code to port, so everything is written from scratch.

### Input validation
Invalid input throws `DarcException`, with all errors in its message. The checks:
- The commits list is empty.
- A repository is not an AzDO URL (`GitRepoUrlUtils.ParseTypeFromUri` must return `AzureDevOps`). **Only AzDO is supported**; GitHub URLs are rejected.
- A sha doesn't match `^[0-9a-f]{7,40}$` after trimming and lowercasing. This also prevents values that git would read as options.
- The build doesn't exist (`SqlBarClient.GetBuildAsync` returns null; the API client reports HTTP 404).
- The build has no `AzureDevOpsRepository`.
- The build is a VMR build and a requested repository is not the VMR.
- The build is a released non-VMR build. Released builds are excluded from the graph walk, so the root would otherwise silently be reported as "not in build".

Normalization: shas are lowercased, repository URLs normalized (see below), and duplicate pairs removed.

### Repository URL matching
- Both AzDO URL forms (`dev.azure.com/dnceng/...` and `dnceng.visualstudio.com/...`, with or without user info) normalize to the `dev.azure.com` form via `AzureDevOpsClient.NormalizeUrl`. A trailing `/` and `.git` are then trimmed, and URLs are compared case-insensitively.
- A BAR build matches a repository only by its `AzureDevOpsRepository`. Builds without one never match.
- `Build.GetRepository()` is not used, because it returns `GitHubRepository` first.

### Case A: VMR build
- A build is a VMR build when its `AzureDevOpsRepository` is the VMR AzDO repository, `https://dev.azure.com/dnceng/internal/_git/dotnet-dotnet`, defined as a constant.
- The VMR is prepared with `IVmrCloneManager.PrepareVmrAsync([build.AzureDevOpsRepository], [build.Commit], build.Commit, resetToRemote: false, ct)`.
- Each requested sha is checked against `build.Commit`.

### Case B: non-VMR build (walk the build graph)
- The graph comes from `IBasicBarClient.GetBuildGraphAsync(build.Id)`, the same data BarViz shows. The existing graph query stops after 11 dependency edges.
- The walk starts at the root and follows product dependency edges only (`BuildRef.IsProduct`). Released builds are skipped together with their subtrees, matching BarViz's `BuildGraphData`.
- The whole non-released graph is always walked; the walk doesn't stop at the first match. A deeper build of the same repository can have a different commit.

For each requested repository, the builds of that repository found in the graph decide the result:

| Builds of the target repository | Result |
|---|---|
| none | `RepositoryNotInBuild` |
| one, or several with the same commit | `Resolved` to the lowest build id with that commit |
| several with different commits | `IncoherentGraph`, listing the conflicting `(buildId, commit)` pairs |

- If any requested repository is incoherent, no inclusion checks run. Every repository's status is returned with `IsCoherent = false`, and `Conflicts` is set only for the incoherent repositories.
- Otherwise, each resolved repository is prepared with `IRepositoryCloneManager.PrepareCloneAsync(resolvedBuild.AzureDevOpsRepository, resolvedBuild.Commit, resetToRemote: false, ct)`. Its shas are checked against `resolvedBuild.Commit`.

### Commit check
The same flow runs for VMR builds (on the VMR clone) and non-VMR builds (on the resolved repository clone).

The existence check runs first, as a precondition. Then the [check pipeline](#darclib-core-host-agnostic) runs `AncestorCommitInclusionCheck`, followed by `PatchReverseApplyCommitInclusionCheck`.

1. **Existence (precondition).** `GetObjectTypeAsync(sha)` on the returned `ILocalGitRepo` runs first, because `IsAncestorCommit` throws on unknown objects. If the object is not a commit, the result is `CommitNotFound` and the pipeline doesn't run.
   - Clones fetch only with `git remote update`, so a sha reachable only from a PR ref is reported as `CommitNotFound`.
   - The existing helper reports Git failures as `Unknown`, so an operational failure here can also result in `CommitNotFound`.
2. **`AncestorCommitInclusionCheck`.** Returns `IsAncestorCommit(ancestor: sha, descendant: buildCommit)`. Equal commits count as included.
3. **`PatchReverseApplyCommitInclusionCheck`.** Runs only if the ancestry check returned `false`. A commit can still be in the build after a rebase, squash or cherry-pick, where its content is there but the sha isn't. What matters is whether the content is in the build.
   - Create one patch with `IVmrPatchHandler.CreateSinglePatchAsync(patchPath, sha1: $"{sha}^", sha2: sha, workingDir: clone.Path, ct)`. This reuses the existing `git diff --binary <sha>^..<sha>` implementation without splitting. For merge commits, `^` is the first parent.
   - Patches of 1 GB or larger fail explicitly. The existing splitter enumerates the build checkout and can omit changed paths that are absent there.
   - The patch file goes under `IVmrInfo.TmpPath` with a unique name and is deleted in a `finally` block, including for empty patches and failed applications.
   - If the patch is empty, the commit has no content to check and the check returns `true`. `ApplyPatch` already treats an empty patch as a no-op success.
   - Reverse-apply with `IVmrPatchHandler.ApplyPatch(patch, clone.Path, removePatchAfter: true, keepConflicts: false, reverseApply: true, applyToIndex: true, ct)`.
     - If it succeeds, every change in the commit is present at the build commit, so the check returns `true` (`Included`).
     - If it throws `PatchApplicationFailedException`, including for an operational `git apply` failure, the check returns `false`. Patch-creation, cleanup, and reset errors propagate.
   - **Clone state:** the clone must be checked out at the build commit. `PrepareCloneAsync` and `PrepareVmrAsync` already check out `buildCommit`.
     - `applyToIndex: true` uses `git apply --cached`, so the patch is applied against the index, which is the build commit.
     - `ApplyPatch` then runs `git checkout .`, which leaves the index modified.
     - So after every attempt, whether it succeeds or fails, the check runs `git reset --hard <buildCommit>` through `ILocalGitRepo.ExecuteGitCommand`, in a `finally` block, as the pipeline contract requires.
   - Each commit is checked separately (one patch per commit). A range of commits isn't combined.
   - **Root commit (no parent):** detect it with `git rev-list --parents -n 1 <sha>`, which prints only the sha itself for a root commit. `<sha>^` doesn't exist in that case. Use git's empty tree (`4b825dc642cb6eb9a060e54bf8d69288fbee4904`) as `sha1` instead. `git diff <emptyTree>..<sha>` was verified to work and produces the whole commit.
   - `ApplyPatch` also sets `git config apply.ignoreWhitespace change` in the clone. This is harmless and persists in the reused clone.
   - **Known limitations:**
     - A reverse-apply can also succeed when the same change reached the build through a different commit. This is intended, because we only care about the content.
     - It can fail when the content is in the build but later commits changed the same lines. That is reported as `NotIncluded`.
     - `--ignore-space-change` is always on in `ApplyPatch`, so whitespace-only differences are ignored.

### Errors
- Git, network and BAR errors propagate except for the existence and reverse-apply limitations noted above. darc reports propagated errors as command failures.
- A missing build is reported as a validation error whether the BAR client returns null or HTTP 404.

### Result
Models (immutable records):
- `RepositoryCommit(string Repository, string Sha)`: the input pair.
- `BuildCommitInclusionResult(int BuildId, bool IsVmrBuild, bool IsCoherent, IReadOnlyList<RepositoryInclusionResult> Repositories)`.
- `RepositoryInclusionResult(string Repository, RepositoryInclusionStatus Status, int? ResolvedBuildId, string? ResolvedCommit, IReadOnlyList<BuildCommitConflict>? Conflicts, IReadOnlyList<CommitInclusionResult> Commits)`.
- `BuildCommitConflict(int BuildId, string Commit)`.
- `CommitInclusionResult(string Sha, CommitInclusionStatus Status)`.
- `RepositoryInclusionStatus`: `Resolved`, `RepositoryNotInBuild`, `IncoherentGraph`.
- `CommitInclusionStatus`: `Included`, `NotIncluded`, `CommitNotFound`, `NotChecked`.
  - `NotChecked` is used for every commit of a repository that wasn't checked: the repository is `RepositoryNotInBuild`, or the graph is incoherent.
- JSON enums are serialized as strings.

Example:
```json
{
  "buildId": 284512,
  "isVmrBuild": false,
  "isCoherent": true,
  "repositories": [
    { "repository": "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime", "status": "Resolved",
      "resolvedBuildId": 284390, "resolvedCommit": "9ab01c3",
      "commits": [ { "sha": "4f1c2e9", "status": "Included" } ] }
  ]
}
```
For a VMR build, `resolvedBuildId` is the build itself.

The text output lists, per repository, the status and resolved build/commit, then one line per commit with its status. For an incoherent repository it lists the conflicting builds instead.

## Tests
- `test/Darc/Microsoft.DotNet.DarcLib.Tests`:
  - `BuildGraphRepositoryResolverTests`:
    - single match
    - the root is the target repository
    - both AzDO URL forms match
    - builds without `AzureDevOpsRepository` never match
    - released builds and their subtrees are skipped
    - toolset edges are ignored
    - same commit from several builds
    - incoherent graph
    - repository not in the build
  - `BuildCommitInclusionInputTests`:
    - normalization and duplicate removal
    - GitHub URLs and invalid shas are rejected
    - released non-VMR build is rejected
    - VMR build with a non-VMR repository is rejected
  - `BuildCommitInclusionCheckerTests`, with `IBasicBarClient`, the clone managers, `ILocalGitRepo` and the `ICommitInclusionCheck`s mocked. The graph is mocked through `IBasicBarClient`, so no virtual DbContext method is needed.
    - VMR path
    - non-VMR path, using the resolved build's commit and repository
    - `CommitNotFound`: no pipeline check runs
    - the pipeline stops at the first check that returns `true`; later checks aren't called
    - all checks return `false` → `NotIncluded`
    - checks run in registration order
    - a repository not in the build is never cloned, and its commits are `NotChecked`
    - an incoherent graph skips all clones and checks, and every commit is `NotChecked`
    - a missing build throws `DarcException`
  - `AncestorCommitInclusionCheckTests`: ancestor → `true`; not an ancestor → `false`.
  - `PatchReverseApplyCommitInclusionCheckTests`, with `IVmrPatchHandler` and `ILocalGitRepo` mocked:
    - reverse-apply succeeds → `true`
    - `PatchApplicationFailedException` → `false`
    - empty patch → `true`
    - root commit uses the empty tree as the patch base
    - `git reset --hard <buildCommit>` runs after both success and failure
    - patches at or above the size limit fail without splitting
- `test/Darc/Microsoft.DotNet.Darc.Tests`: parsing of `<repoUrl>@<sha>`, including URLs with user info (split on the last `@`) and malformed values.
- `BarApiClient` / `SqlBarClient.GetBuildGraphAsync` are thin mappings. Add tests only if a matching test fixture exists.

## Verification
- Build and test with `scripts/verify.ps1`. If a direct build is needed, the local environment may fail NuGet restore of BarViz with NU1603. Pass `"/p:WarningsNotAsErrors=NU1603"`. Don't use `/p:NoWarn`, because it overrides the repo's NoWarn list.
- If `.dotnet` lacks the SDK from `global.json`, run `.\eng\common\Build.ps1 -restore` once.
- `TreatWarningsAsErrors` is on, so unused usings break the build.
- Manual smoke test (read-only, safe): `darc get-build-commit-inclusion --build <real build id> <repo>@<sha>` against a known build. Don't use mutating darc verbs for testing.

## Out of scope
- PCS endpoint, work item, Redis caching and async polling.
- GitHub repositories.
- Regenerating the PCS client: the graph endpoint already exists in it.
