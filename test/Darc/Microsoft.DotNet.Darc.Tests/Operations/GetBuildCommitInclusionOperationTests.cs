// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.Darc.Options;
using Microsoft.DotNet.Darc.Tests.Helpers;
using Microsoft.DotNet.DarcLib;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.Darc.Tests.Operations;

[TestFixture]
[NonParallelizable]
public class GetBuildCommitInclusionOperationTests
{
    private const string Repository = "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime";
    private const string LegacyRepository = "https://dnceng@dnceng.visualstudio.com/internal/_git/dotnet-runtime";
    private const string Sha = "1234567";

    [Test]
    public void ParsesEachValueUsingTheLastAtSign()
    {
        var result = GetBuildCommitInclusionOperation.ParseCommits(
            [$"{LegacyRepository}@{Sha}", $"{Repository}@abcdef0"]);

        result.Should().Equal(new RepositoryCommit(LegacyRepository, Sha), new RepositoryCommit(Repository, "abcdef0"));
    }

    [Test]
    public void ReportsAllMalformedValues()
    {
        Action action = () => GetBuildCommitInclusionOperation.ParseCommits(["no-separator", "@abc", "repo@", " @ "]);

        var error = action.Should().Throw<DarcException>().Which;
        error.Message.Should().Contain("Commit 1:").And.Contain("Commit 2:").And.Contain("Commit 3:").And.Contain("Commit 4:");
    }

    [Test]
    public async Task WritesCamelCaseJsonWithStringStatuses()
    {
        using ConsoleOutputIntercepter output = new();
        Mock<IBuildCommitInclusionChecker> checker = new();
        checker.Setup(x => x.CheckAsync(42, It.IsAny<IReadOnlyCollection<RepositoryCommit>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildCommitInclusionResult(
                42,
                false,
                true,
                [new RepositoryInclusionResult(
                    Repository,
                    RepositoryInclusionStatus.Resolved,
                    7,
                    "abcdef0",
                    null,
                    [new CommitInclusionResult(Sha, CommitInclusionStatus.Included)])]));
        var options = new GetBuildCommitInclusionCommandLineOptions
        {
            Build = 42,
            Commits = [$"{LegacyRepository}@{Sha}"],
            OutputFormat = DarcOutputType.json,
        };

        int exitCode = await new GetBuildCommitInclusionOperation(
            options, checker.Object, NullLogger<GetBuildCommitInclusionOperation>.Instance).ExecuteAsync();

        exitCode.Should().Be(Constants.SuccessCode);
        JObject json = JObject.Parse(output.GetOutput());
        json["buildId"]!.Value<int>().Should().Be(42);
        json["isVmrBuild"]!.Value<bool>().Should().BeFalse();
        json["repositories"]![0]!["status"]!.Value<string>().Should().Be("Resolved");
        json["repositories"]![0]!["commits"]![0]!["status"]!.Value<string>().Should().Be("Included");
        json["repositories"]![0]!["conflicts"].Should().BeNull();
        checker.Verify(x => x.CheckAsync(
            42,
            It.Is<IReadOnlyCollection<RepositoryCommit>>(commits =>
                commits.Count == 1 && commits.Contains(new RepositoryCommit(LegacyRepository, Sha))),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task TextShowsConflictsAndUnexaminedCommits()
    {
        using ConsoleOutputIntercepter output = new();
        Mock<IBuildCommitInclusionChecker> checker = new();
        checker.Setup(x => x.CheckAsync(42, It.IsAny<IReadOnlyCollection<RepositoryCommit>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BuildCommitInclusionResult(
                42,
                false,
                false,
                [new RepositoryInclusionResult(
                    Repository,
                    RepositoryInclusionStatus.IncoherentGraph,
                    null,
                    null,
                    [new BuildCommitConflict(7, "abcdef0"), new BuildCommitConflict(8, "9876543")],
                    [new CommitInclusionResult(Sha, CommitInclusionStatus.NotChecked)])]));
        var options = new GetBuildCommitInclusionCommandLineOptions { Build = 42, Commits = [$"{Repository}@{Sha}"] };

        int exitCode = await new GetBuildCommitInclusionOperation(
            options, checker.Object, NullLogger<GetBuildCommitInclusionOperation>.Instance).ExecuteAsync();

        exitCode.Should().Be(Constants.SuccessCode);
        output.GetOutput().Should().Contain("IncoherentGraph")
            .And.Contain("Conflicting build 7: abcdef0")
            .And.Contain("Conflicting build 8: 9876543")
            .And.Contain($"{Sha}: NotChecked");
    }

    [Test]
    public async Task InvalidInputReturnsAnErrorWithoutInvokingTheChecker()
    {
        Mock<IBuildCommitInclusionChecker> checker = new();
        var options = new GetBuildCommitInclusionCommandLineOptions { Build = 42, Commits = ["malformed"] };

        int exitCode = await new GetBuildCommitInclusionOperation(
            options, checker.Object, NullLogger<GetBuildCommitInclusionOperation>.Instance).ExecuteAsync();

        exitCode.Should().Be(Constants.ErrorCode);
        checker.Verify(x => x.CheckAsync(
            It.IsAny<int>(), It.IsAny<IReadOnlyCollection<RepositoryCommit>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public void RegistersTheCheckerAndUsesAnIsolatedVmrClonePath()
    {
        string tmpPath = Path.Combine(Path.GetTempPath(), $"darc-inclusion-test-{Guid.NewGuid():N}");
        string? previousTmpPath = Environment.GetEnvironmentVariable("DARC_TMP_DIR");
        try
        {
            Environment.SetEnvironmentVariable("DARC_TMP_DIR", tmpPath);
            var options = new GetBuildCommitInclusionCommandLineOptions { GitLocation = "git", IsCi = true };
            using var provider = options.RegisterServices(new ServiceCollection()).BuildServiceProvider();

            provider.GetRequiredService<IBuildCommitInclusionChecker>().Should().BeOfType<BuildCommitInclusionChecker>();
            provider.GetServices<ICommitInclusionCheck>().Select(check => check.GetType()).Should().Equal(
                typeof(AncestorCommitInclusionCheck), typeof(PatchReverseApplyCommitInclusionCheck));
            var vmrInfo = provider.GetRequiredService<IVmrInfo>();
            vmrInfo.VmrPath.Path.Should().BeEmpty();
            vmrInfo.TmpPath.Path.Should().Be(tmpPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DARC_TMP_DIR", previousTmpPath);
            if (Directory.Exists(tmpPath))
            {
                Directory.Delete(tmpPath);
            }
        }
    }
}
