// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Threading;
using System.Threading.Tasks;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

public class AncestorCommitInclusionCheck : ICommitInclusionCheck
{
    public string Name => nameof(AncestorCommitInclusionCheck);

    public async Task<bool> IsIncludedAsync(CommitInclusionCheckContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await context.Repository.IsAncestorCommit(context.Commit, context.BuildCommit);
    }
}
