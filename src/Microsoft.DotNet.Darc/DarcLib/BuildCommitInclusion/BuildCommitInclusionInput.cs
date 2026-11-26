// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Maestro.Common;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

internal static partial class BuildCommitInclusionInput
{
    public const string VmrRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-dotnet";

    [GeneratedRegex("^[0-9a-f]{7,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitShaPattern();

    public static IReadOnlyList<RepositoryCommit> NormalizeAndValidate(
        int buildId,
        Build build,
        IReadOnlyCollection<RepositoryCommit> commits)
    {
        List<string> errors = [];
        List<RepositoryCommit> normalizedCommits = [];
        List<string> repositories = [];
        HashSet<RepositoryCommit> seen = [];

        if (commits is null || commits.Count == 0)
        {
            errors.Add("At least one repository commit must be provided.");
        }
        else
        {
            int index = 0;
            foreach (RepositoryCommit commit in commits)
            {
                index++;
                string repository = commit.Repository?.Trim() ?? string.Empty;
                string sha = commit.Sha?.Trim().ToLowerInvariant() ?? string.Empty;
                bool validRepository = GitRepoUrlUtils.ParseTypeFromUri(repository) == GitRepoType.AzureDevOps;
                bool validSha = CommitShaPattern().IsMatch(sha);

                if (!validRepository)
                {
                    errors.Add($"Commit {index}: repository must be an Azure DevOps URL.");
                }
                else
                {
                    repository = RepositoryUrlComparer.Normalize(repository);
                    repositories.Add(repository);
                }

                if (!validSha)
                {
                    errors.Add($"Commit {index}: SHA must contain 7 to 40 hexadecimal characters.");
                }

                if (validRepository && validSha)
                {
                    RepositoryCommit normalized = new(repository, sha);
                    if (seen.Add(normalized))
                    {
                        normalizedCommits.Add(normalized);
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(build.AzureDevOpsRepository))
        {
            errors.Add($"Build {buildId} does not have an Azure DevOps repository.");
        }
        else if (RepositoryUrlComparer.Instance.Equals(build.AzureDevOpsRepository, VmrRepository))
        {
            foreach (string repository in repositories)
            {
                if (!RepositoryUrlComparer.Instance.Equals(repository, VmrRepository))
                {
                    errors.Add("A VMR build can only be checked for commits from the VMR repository.");
                    break;
                }
            }
        }
        else if (build.Released)
        {
            errors.Add($"Released non-VMR build {buildId} cannot be checked.");
        }

        if (errors.Count > 0)
        {
            throw new DarcException(string.Join(Environment.NewLine, errors));
        }

        return normalizedCommits;
    }
}
