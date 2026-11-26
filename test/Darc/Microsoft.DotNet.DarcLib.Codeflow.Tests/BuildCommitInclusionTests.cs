// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.Internal.Credentials;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Codeflow.Tests;

internal class BuildCommitInclusionTests : CodeFlowTestsBase
{
    private const string Repository = "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime";

    [Test]
    public async Task ChecksRealGitCommitsAndRestoresTheCloneAfterEachPatch()
    {
        string rootCommit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        await GitOperations.CreateBranch(ProductRepoPath, "feature");
        await File.WriteAllTextAsync(ProductRepoPath / "present.txt", "present\n");
        await GitOperations.CommitAll(ProductRepoPath, "Add present file");
        string presentCommit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        await File.WriteAllTextAsync(ProductRepoPath / "missing.txt", "missing\n");
        await GitOperations.CommitAll(ProductRepoPath, "Add missing file");
        string missingCommit = await GitOperations.GetRepoLastCommit(ProductRepoPath);
        await GitOperations.Commit(ProductRepoPath, "Empty commit", allowEmpty: true);
        string emptyCommit = await GitOperations.GetRepoLastCommit(ProductRepoPath);

        await GitOperations.Checkout(ProductRepoPath, "main");
        await File.WriteAllTextAsync(ProductRepoPath / "present.txt", "present\n");
        await GitOperations.CommitAll(ProductRepoPath, "Apply the same content independently");
        string buildCommit = await GitOperations.GetRepoLastCommit(ProductRepoPath);

        var repository = ServiceProvider.GetRequiredService<ILocalGitRepoFactory>().Create(ProductRepoPath);
        var cloneManager = new Mock<IRepositoryCloneManager>();
        cloneManager.Setup(manager => manager.PrepareCloneAsync(
                Repository, buildCommit, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repository);
        var build = new Build(1, DateTimeOffset.MinValue, 0, false, false, buildCommit, [], [], [], [])
        {
            AzureDevOpsRepository = Repository,
        };
        _basicBarClient.Setup(bar => bar.GetBuildAsync(1, false)).ReturnsAsync(build);
        _basicBarClient.Setup(bar => bar.GetBuildGraphAsync(1)).ReturnsAsync([build]);
        var checker = new BuildCommitInclusionChecker(
            _basicBarClient.Object,
            Mock.Of<IVmrCloneManager>(),
            cloneManager.Object,
            ServiceProvider.GetServices<ICommitInclusionCheck>(),
            NullLogger<BuildCommitInclusionChecker>.Instance);
        const string unknownCommit = "1111111111111111111111111111111111111111";

        var result = await checker.CheckAsync(
            1,
            [
                new RepositoryCommit(Repository, presentCommit),
                new RepositoryCommit(Repository, missingCommit),
                new RepositoryCommit(Repository, emptyCommit),
                new RepositoryCommit(Repository, unknownCommit),
                new RepositoryCommit(Repository, rootCommit),
            ],
            CancellationToken.None);

        result.Repositories.Single().Commits.Select(commit => commit.Status).Should().Equal(
            CommitInclusionStatus.Included,
            CommitInclusionStatus.NotIncluded,
            CommitInclusionStatus.Included,
            CommitInclusionStatus.CommitNotFound,
            CommitInclusionStatus.Included);

        var reverseCheck = ServiceProvider.GetServices<ICommitInclusionCheck>()
            .OfType<PatchReverseApplyCommitInclusionCheck>().Single();
        bool rootPatchApplies = await reverseCheck.IsIncludedAsync(
            new CommitInclusionCheckContext(repository, rootCommit, buildCommit), CancellationToken.None);

        rootPatchApplies.Should().BeTrue();
        (await repository.GetGitCommitAsync()).Should().Be(buildCommit);
        await GitOperations.CheckAllIsCommitted(ProductRepoPath);
        Directory.GetFiles(TmpPath, "commit-inclusion-*.patch").Should().BeEmpty();
    }

    protected override async Task CopyReposForCurrentTest()
    {
        Directory.CreateDirectory(ProductRepoPath);
        await File.WriteAllTextAsync(ProductRepoPath / "root.txt", "root\n");
        await GitOperations.InitialCommit(ProductRepoPath);
    }

    protected override Task CopyVmrForCurrentTest() => Task.CompletedTask;

    protected override IServiceCollection CreateServiceProvider()
        => new ServiceCollection()
            .AddLogging()
            .AddCodeflow(TmpPath, vmrPath: null)
            .AddBuildCommitInclusion()
            .AddSingleton(_basicBarClient.Object)
            .AddSingleton(Mock.Of<IRemoteTokenProvider>());
}
