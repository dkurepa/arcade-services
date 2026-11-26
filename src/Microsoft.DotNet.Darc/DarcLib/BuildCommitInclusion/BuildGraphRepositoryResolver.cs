// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

internal record RepositoryBuildResolution(
    RepositoryInclusionStatus Status,
    Build? Build,
    IReadOnlyList<BuildCommitConflict>? Conflicts);

internal static class BuildGraphRepositoryResolver
{
    public static IReadOnlyDictionary<string, RepositoryBuildResolution> Resolve(
        int rootBuildId,
        IReadOnlyCollection<Build> graph,
        IReadOnlyCollection<string> repositories)
    {
        var buildsById = graph.ToDictionary(build => build.Id);
        if (!buildsById.TryGetValue(rootBuildId, out Build? root))
        {
            throw new DarcException($"Build graph does not contain root build {rootBuildId}.");
        }

        Dictionary<string, List<Build>> matches = new(RepositoryUrlComparer.Instance);
        foreach (string repository in repositories)
        {
            matches.TryAdd(repository, []);
        }

        HashSet<int> visited = [];
        Stack<Build> pending = new();
        pending.Push(root);

        while (pending.TryPop(out Build? build))
        {
            if (!visited.Add(build.Id) || build.Released)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(build.AzureDevOpsRepository)
                && matches.TryGetValue(build.AzureDevOpsRepository, out List<Build>? repositoryBuilds))
            {
                if (string.IsNullOrWhiteSpace(build.Commit))
                {
                    throw new DarcException($"Build {build.Id} has no commit.");
                }

                repositoryBuilds.Add(build);
            }

            if (build.Dependencies is null)
            {
                throw new DarcException($"Build {build.Id} has no dependency data in the build graph.");
            }

            foreach (BuildRef dependency in build.Dependencies.Where(dependency => dependency.IsProduct))
            {
                if (!buildsById.TryGetValue(dependency.BuildId, out Build? dependencyBuild))
                {
                    throw new DarcException($"Build {build.Id} references missing build {dependency.BuildId} in the build graph.");
                }

                pending.Push(dependencyBuild);
            }
        }

        Dictionary<string, RepositoryBuildResolution> results = new(RepositoryUrlComparer.Instance);
        foreach (var (repository, repositoryBuilds) in matches)
        {
            var orderedBuilds = repositoryBuilds.OrderBy(build => build.Id).ToList();
            if (orderedBuilds.Count == 0)
            {
                results.Add(repository, new(RepositoryInclusionStatus.RepositoryNotInBuild, null, null));
            }
            else if (orderedBuilds.Select(build => build.Commit).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            {
                results.Add(repository, new(
                    RepositoryInclusionStatus.IncoherentGraph,
                    null,
                    orderedBuilds.Select(build => new BuildCommitConflict(build.Id, build.Commit)).ToList()));
            }
            else
            {
                results.Add(repository, new(RepositoryInclusionStatus.Resolved, orderedBuilds[0], null));
            }
        }

        return results;
    }
}
