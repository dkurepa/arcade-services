// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

public interface IBuildCommitInclusionChecker
{
    Task<BuildCommitInclusionResult> CheckAsync(
        int buildId,
        IReadOnlyCollection<RepositoryCommit> commits,
        CancellationToken cancellationToken);
}

public class BuildCommitInclusionChecker(
    IBasicBarClient barClient,
    IVmrCloneManager vmrCloneManager,
    IRepositoryCloneManager repositoryCloneManager,
    IEnumerable<ICommitInclusionCheck> checks,
    ILogger<BuildCommitInclusionChecker> logger) : IBuildCommitInclusionChecker
{
    private readonly IReadOnlyList<ICommitInclusionCheck> _checks = checks.ToList();

    public async Task<BuildCommitInclusionResult> CheckAsync(
        int buildId,
        IReadOnlyCollection<RepositoryCommit> commits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Build? build;
        try
        {
            build = await barClient.GetBuildAsync(buildId);
        }
        catch (RestApiException exception) when (exception.Response.Status == (int)HttpStatusCode.NotFound)
        {
            throw new DarcException($"Build {buildId} was not found.");
        }

        if (build is null)
        {
            throw new DarcException($"Build {buildId} was not found.");
        }

        var normalizedCommits = BuildCommitInclusionInput.NormalizeAndValidate(buildId, build, commits);
        if (string.IsNullOrWhiteSpace(build.Commit))
        {
            throw new DarcException($"Build {buildId} has no commit.");
        }

        bool isVmrBuild = RepositoryUrlComparer.Instance.Equals(
            build.AzureDevOpsRepository,
            BuildCommitInclusionInput.VmrRepository);

        var groups = normalizedCommits
            .GroupBy(commit => commit.Repository, RepositoryUrlComparer.Instance)
            .ToList();

        BuildCommitInclusionResult result;
        if (isVmrBuild)
        {
            result = await CheckVmrBuildAsync(buildId, build, groups, cancellationToken);
        }
        else
        {
            result = await CheckRepositoryBuildAsync(buildId, build, groups, cancellationToken);
        }

        return result;
    }

    private async Task<BuildCommitInclusionResult> CheckVmrBuildAsync(
        int buildId,
        Build build,
        IReadOnlyCollection<IGrouping<string, RepositoryCommit>> groups,
        CancellationToken cancellationToken)
    {
        var clone = await vmrCloneManager.PrepareVmrAsync(
            [build.AzureDevOpsRepository],
            [build.Commit],
            build.Commit,
            resetToRemote: false,
            cancellationToken);

        var results = new List<RepositoryInclusionResult>();
        foreach (var group in groups)
        {
            results.Add(new RepositoryInclusionResult(
                group.Key,
                RepositoryInclusionStatus.Resolved,
                build.Id,
                build.Commit,
                null,
                await CheckCommitsAsync(clone, build.Commit, group, cancellationToken)));
        }

        return new BuildCommitInclusionResult(buildId, true, true, results);
    }

    private async Task<BuildCommitInclusionResult> CheckRepositoryBuildAsync(
        int buildId,
        Build build,
        IReadOnlyCollection<IGrouping<string, RepositoryCommit>> groups,
        CancellationToken cancellationToken)
    {
        var graph = await barClient.GetBuildGraphAsync(build.Id);
        var resolutions = BuildGraphRepositoryResolver.Resolve(
            build.Id,
            graph,
            groups.Select(group => group.Key).ToList());
        bool isCoherent = resolutions.Values.All(resolution => resolution.Status != RepositoryInclusionStatus.IncoherentGraph);

        List<RepositoryInclusionResult> repositories = [];
        foreach (var group in groups)
        {
            var resolution = resolutions[group.Key];
            Build? resolvedBuild = resolution.Build;
            IReadOnlyList<CommitInclusionResult> results;

            if (!isCoherent || resolvedBuild is null)
            {
                results = group.Select(commit => new CommitInclusionResult(commit.Sha, CommitInclusionStatus.NotChecked)).ToList();
            }
            else
            {
                var clone = await repositoryCloneManager.PrepareCloneAsync(
                    resolvedBuild.AzureDevOpsRepository,
                    resolvedBuild.Commit,
                    resetToRemote: false,
                    cancellationToken);
                results = await CheckCommitsAsync(clone, resolvedBuild.Commit, group, cancellationToken);
            }

            repositories.Add(new RepositoryInclusionResult(
                group.Key,
                resolution.Status,
                resolvedBuild?.Id,
                resolvedBuild?.Commit,
                resolution.Conflicts,
                results));
        }

        return new BuildCommitInclusionResult(buildId, false, isCoherent, repositories);
    }

    private async Task<IReadOnlyList<CommitInclusionResult>> CheckCommitsAsync(
        ILocalGitRepo repository,
        string buildCommit,
        IEnumerable<RepositoryCommit> commits,
        CancellationToken cancellationToken)
    {
        List<CommitInclusionResult> results = [];
        foreach (RepositoryCommit commit in commits)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await repository.GetObjectTypeAsync(commit.Sha) != GitObjectType.Commit)
            {
                results.Add(new CommitInclusionResult(commit.Sha, CommitInclusionStatus.CommitNotFound));
                continue;
            }

            var context = new CommitInclusionCheckContext(repository, commit.Sha, buildCommit);
            CommitInclusionStatus status = CommitInclusionStatus.NotIncluded;
            foreach (ICommitInclusionCheck check in _checks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await check.IsIncludedAsync(context, cancellationToken))
                {
                    logger.LogInformation("Commit {commit} was included by {check}", commit.Sha, check.Name);
                    status = CommitInclusionStatus.Included;
                    break;
                }
            }

            results.Add(new CommitInclusionResult(commit.Sha, status));
        }

        return results;
    }
}
