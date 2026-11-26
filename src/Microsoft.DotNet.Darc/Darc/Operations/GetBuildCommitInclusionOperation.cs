// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

#nullable enable
namespace Microsoft.DotNet.Darc.Operations;

internal class GetBuildCommitInclusionOperation(
    GetBuildCommitInclusionCommandLineOptions options,
    IBuildCommitInclusionChecker checker,
    ILogger<GetBuildCommitInclusionOperation> logger) : Operation
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        Converters = { new StringEnumConverter() },
    };

    public override async Task<int> ExecuteAsync()
    {
        try
        {
            var commits = ParseCommits(options.Commits);
            var result = await checker.CheckAsync(options.Build, commits, CancellationToken.None);

            if (options.OutputFormat == DarcOutputType.json)
            {
                Console.WriteLine(JsonConvert.SerializeObject(result, Formatting.Indented, JsonSettings));
            }
            else
            {
                WriteText(result);
            }

            return Constants.SuccessCode;
        }
        catch (DarcException exception)
        {
            logger.LogError("{error}", exception.Message);
            return Constants.ErrorCode;
        }
    }

    internal static IReadOnlyList<RepositoryCommit> ParseCommits(IEnumerable<string> values)
    {
        List<RepositoryCommit> commits = [];
        List<string> errors = [];
        int index = 0;
        foreach (string value in values)
        {
            index++;
            int separator = value.LastIndexOf('@');
            if (separator <= 0
                || separator == value.Length - 1
                || string.IsNullOrWhiteSpace(value[..separator])
                || string.IsNullOrWhiteSpace(value[(separator + 1)..]))
            {
                errors.Add($"Commit {index}: expected <repoUrl>@<sha>.");
                continue;
            }

            commits.Add(new RepositoryCommit(value[..separator], value[(separator + 1)..]));
        }

        if (errors.Count > 0)
        {
            throw new DarcException(string.Join(Environment.NewLine, errors));
        }

        return commits;
    }

    private static void WriteText(BuildCommitInclusionResult result)
    {
        Console.WriteLine($"Build {result.BuildId}: {(result.IsCoherent ? "Coherent" : "Incoherent")}");
        foreach (RepositoryInclusionResult repository in result.Repositories)
        {
            Console.WriteLine($"{repository.Repository}: {repository.Status}");
            if (repository.ResolvedBuildId is not null)
            {
                Console.WriteLine($"  Build {repository.ResolvedBuildId}: {repository.ResolvedCommit}");
            }

            if (repository.Conflicts is not null)
            {
                foreach (BuildCommitConflict conflict in repository.Conflicts)
                {
                    Console.WriteLine($"  Conflicting build {conflict.BuildId}: {conflict.Commit}");
                }
            }

            foreach (CommitInclusionResult commit in repository.Commits)
            {
                Console.WriteLine($"  {commit.Sha}: {commit.Status}");
            }
        }
    }
}
