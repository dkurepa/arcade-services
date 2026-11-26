// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

public record RepositoryCommit(string Repository, string Sha);

public record BuildCommitInclusionResult(
    int BuildId,
    bool IsVmrBuild,
    bool IsCoherent,
    IReadOnlyList<RepositoryInclusionResult> Repositories);

public record RepositoryInclusionResult(
    string Repository,
    RepositoryInclusionStatus Status,
    int? ResolvedBuildId,
    string? ResolvedCommit,
    IReadOnlyList<BuildCommitConflict>? Conflicts,
    IReadOnlyList<CommitInclusionResult> Commits);

public record BuildCommitConflict(int BuildId, string Commit);

public record CommitInclusionResult(string Sha, CommitInclusionStatus Status);

public enum RepositoryInclusionStatus
{
    Resolved,
    RepositoryNotInBuild,
    IncoherentGraph,
}

public enum CommitInclusionStatus
{
    Included,
    NotIncluded,
    CommitNotFound,
    NotChecked,
}
