// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

/// <summary>
/// Result of verifying that a forward-flow codeflow PR faithfully reflects the source repo's
/// commit diff (oldSha...newSha), accounting for expected divergences (path remap, excludes,
/// eng/common, version files, no-ops).
/// All paths in the collections are mapping-relative (e.g. <c>test/Foo.cs</c>) for readable reporting.
/// </summary>
public record SourceDiffVerificationResult
{
    /// <summary>
    /// Files present in both the source diff and the PR whose changed content matched.
    /// </summary>
    public IReadOnlyCollection<string> AppliedFiles { get; init; } = [];

    /// <summary>
    /// Files in the source diff but not in the PR where the VMR copy is already at the source's
    /// new state (a legitimate no-op).
    /// </summary>
    public IReadOnlyCollection<string> NoOpFiles { get; init; } = [];

    /// <summary>
    /// Files whose changes do not match between the source diff and the PR (the red flag).
    /// </summary>
    public IReadOnlyCollection<string> MismatchedFiles { get; init; } = [];

    /// <summary>
    /// Files changed in the PR (A\R) but absent from the source diff. This should be empty; a
    /// non-empty set means the codeflow introduced changes under src/&lt;mapping&gt;/ that do not
    /// trace back to the source diff (a correctness red flag).
    /// </summary>
    public IReadOnlyCollection<string> UnexpectedFiles { get; init; } = [];

    /// <summary>
    /// True when no mismatching files were found, i.e. the PR faithfully reflects the source diff.
    /// </summary>
    public bool Matches => MismatchedFiles.Count == 0 && UnexpectedFiles.Count == 0;
}
