// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

public class PatchReverseApplyCommitInclusionCheck(
    IVmrPatchHandler patchHandler,
    IVmrInfo vmrInfo,
    IFileSystem fileSystem,
    ILogger<PatchReverseApplyCommitInclusionCheck> logger) : ICommitInclusionCheck
{
    public string Name => nameof(PatchReverseApplyCommitInclusionCheck);

    public async Task<bool> IsIncludedAsync(CommitInclusionCheckContext context, CancellationToken cancellationToken)
    {
        string patchPath = vmrInfo.TmpPath / $"commit-inclusion-{Guid.NewGuid():N}.patch";
        try
        {
            var parents = await context.Repository.ExecuteGitCommand(
                ["rev-list", "--parents", "-n", "1", context.Commit],
                cancellationToken);
            parents.ThrowIfFailed($"Failed to find the parents of commit {context.Commit}");

            var fields = parents.StandardOutput.Split(
                [' ', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length == 0)
            {
                throw new DarcException($"Git returned no parent information for commit {context.Commit}.");
            }

            string parent = fields.Length == 1 ? Constants.EmptyGitObject : $"{context.Commit}^";
            VmrIngestionPatch patch = await patchHandler.CreateSinglePatchAsync(
                patchPath,
                parent,
                context.Commit,
                context.Repository.Path,
                cancellationToken);

            try
            {
                await patchHandler.ApplyPatch(
                    patch,
                    context.Repository.Path,
                    removePatchAfter: true,
                    keepConflicts: false,
                    reverseApply: true,
                    applyToIndex: true,
                    cancellationToken);
                return true;
            }
            catch (PatchApplicationFailedException exception)
            {
                logger.LogDebug(exception, "Reverse-applying commit {commit} failed", context.Commit);
                return false;
            }
        }
        finally
        {
            try
            {
                fileSystem.DeleteFile(patchPath);
            }
            finally
            {
                var reset = await context.Repository.ExecuteGitCommand(
                    ["reset", "--hard", context.BuildCommit],
                    CancellationToken.None);
                reset.ThrowIfFailed($"Failed to reset the clone to build commit {context.BuildCommit}");
            }
        }
    }
}
