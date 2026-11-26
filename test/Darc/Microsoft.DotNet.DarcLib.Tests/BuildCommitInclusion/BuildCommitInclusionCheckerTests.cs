// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure;
using Azure.Core.Pipeline;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using static Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion.BuildCommitInclusionTestData;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion;

[TestFixture]
public class BuildCommitInclusionCheckerTests
{
    private readonly Mock<IBasicBarClient> _bar = new();
    private readonly Mock<IVmrCloneManager> _vmrClones = new();
    private readonly Mock<IRepositoryCloneManager> _repositoryClones = new();
    private readonly Mock<ILocalGitRepo> _repository = new();

    [SetUp]
    public void SetUp()
    {
        _bar.Reset();
        _vmrClones.Reset();
        _repositoryClones.Reset();
        _repository.Reset();
        _repository.Setup(repo => repo.GetObjectTypeAsync(It.IsAny<string>())).ReturnsAsync(GitObjectType.Commit);
        _repositoryClones
            .Setup(clones => clones.PrepareCloneAsync(
                It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_repository.Object);
        _vmrClones
            .Setup(clones => clones.PrepareVmrAsync(
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<IReadOnlyCollection<string>>(),
                It.IsAny<string>(),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_repository.Object);
    }

    [Test]
    public async Task VmrBuildUsesTheVmrCloneAndDoesNotLoadTheGraph()
    {
        var build = CreateBuild(1, VmrRepository);
        _bar.Setup(bar => bar.GetBuildAsync(1, false)).ReturnsAsync(build);
        var check = CreateCheck("first", true, []);

        var result = await CreateChecker(check.Object).CheckAsync(
            1, [new RepositoryCommit(VmrRepository, RequestedCommit)], CancellationToken.None);

        result.IsVmrBuild.Should().BeTrue();
        result.IsCoherent.Should().BeTrue();
        result.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Repository = VmrRepository,
            Status = RepositoryInclusionStatus.Resolved,
            ResolvedBuildId = 1,
            ResolvedCommit = BuildCommit,
            Commits = new[] { new CommitInclusionResult(RequestedCommit, CommitInclusionStatus.Included) },
        });
        _vmrClones.Verify(clones => clones.PrepareVmrAsync(
            It.Is<IReadOnlyCollection<string>>(remotes => remotes.SequenceEqual(new[] { VmrRepository })),
            It.Is<IReadOnlyCollection<string>>(refs => refs.SequenceEqual(new[] { BuildCommit })),
            BuildCommit,
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        _bar.Verify(bar => bar.GetBuildGraphAsync(It.IsAny<int>()), Times.Never);
        _repositoryClones.Verify(clones => clones.PrepareCloneAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task NonVmrBuildChecksTheResolvedRepositoryAndCommit()
    {
        var root = CreateBuild(1, RootRepository, dependencies: [Product(2)]);
        var dependency = CreateBuild(2, LegacyTargetRepository, commit: "7654321");
        SetGraph(root, dependency);
        var check = CreateCheck("first", true, []);

        var result = await CreateChecker(check.Object).CheckAsync(
            1, [new RepositoryCommit(TargetRepository, RequestedCommit)], CancellationToken.None);

        result.IsVmrBuild.Should().BeFalse();
        result.Repositories.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            Repository = TargetRepository,
            Status = RepositoryInclusionStatus.Resolved,
            ResolvedBuildId = 2,
            ResolvedCommit = "7654321",
            Commits = new[] { new CommitInclusionResult(RequestedCommit, CommitInclusionStatus.Included) },
        });
        _repositoryClones.Verify(clones => clones.PrepareCloneAsync(
            LegacyTargetRepository, "7654321", false, It.IsAny<CancellationToken>()), Times.Once);
        check.Verify(x => x.IsIncludedAsync(
            It.Is<CommitInclusionCheckContext>(context =>
                context.Repository == _repository.Object
                && context.Commit == RequestedCommit
                && context.BuildCommit == "7654321"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(true, false, CommitInclusionStatus.Included, "first")]
    [TestCase(false, true, CommitInclusionStatus.Included, "first,second")]
    [TestCase(false, false, CommitInclusionStatus.NotIncluded, "first,second,third")]
    public async Task PipelineRunsInOrderAndStopsAtTheFirstMatch(
        bool firstMatches,
        bool secondMatches,
        CommitInclusionStatus expectedStatus,
        string expectedCalls)
    {
        SetGraph(CreateBuild(1));
        List<string> calls = [];
        var first = CreateCheck("first", firstMatches, calls);
        var second = CreateCheck("second", secondMatches, calls);
        var third = CreateCheck("third", false, calls);

        var result = await CreateChecker(first.Object, second.Object, third.Object).CheckAsync(
            1, [new RepositoryCommit(TargetRepository, RequestedCommit)], CancellationToken.None);

        result.Repositories.Single().Commits.Single().Status.Should().Be(expectedStatus);
        calls.Should().Equal(expectedCalls.Split(','));
    }

    [TestCase(GitObjectType.Unknown)]
    [TestCase(GitObjectType.Blob)]
    public async Task MissingOrNonCommitObjectDoesNotRunThePipeline(GitObjectType objectType)
    {
        SetGraph(CreateBuild(1));
        _repository.Setup(repo => repo.GetObjectTypeAsync(RequestedCommit)).ReturnsAsync(objectType);
        var check = CreateCheck("first", true, []);

        var result = await CreateChecker(check.Object).CheckAsync(
            1, [new RepositoryCommit(TargetRepository, RequestedCommit)], CancellationToken.None);

        result.Repositories.Single().Commits.Single().Status.Should().Be(CommitInclusionStatus.CommitNotFound);
        check.Verify(x => x.IsIncludedAsync(It.IsAny<CommitInclusionCheckContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task RepositoryNotInBuildIsNotClonedOrChecked()
    {
        SetGraph(CreateBuild(1, RootRepository));
        var check = CreateCheck("first", true, []);

        var result = await CreateChecker(check.Object).CheckAsync(
            1, [new RepositoryCommit(TargetRepository, RequestedCommit)], CancellationToken.None);

        result.IsCoherent.Should().BeTrue();
        result.Repositories.Single().Status.Should().Be(RepositoryInclusionStatus.RepositoryNotInBuild);
        result.Repositories.Single().Commits.Single().Status.Should().Be(CommitInclusionStatus.NotChecked);
        VerifyNoCloneOrCheck(check);
    }

    [Test]
    public async Task AnyIncoherentRepositoryPreventsAllClonesAndChecks()
    {
        var root = CreateBuild(1, RootRepository, dependencies: [Product(2), Product(3)]);
        var first = CreateBuild(2);
        var second = CreateBuild(3, commit: "7654321");
        SetGraph(root, first, second);
        var check = CreateCheck("first", true, []);

        var result = await CreateChecker(check.Object).CheckAsync(
            1,
            [
                new RepositoryCommit(RootRepository, RequestedCommit),
                new RepositoryCommit(TargetRepository, RequestedCommit),
            ],
            CancellationToken.None);

        result.IsCoherent.Should().BeFalse();
        result.Repositories.Select(repository => repository.Status).Should().Equal(
            RepositoryInclusionStatus.Resolved, RepositoryInclusionStatus.IncoherentGraph);
        result.Repositories.SelectMany(repository => repository.Commits)
            .Should().OnlyContain(commit => commit.Status == CommitInclusionStatus.NotChecked);
        result.Repositories[1].Conflicts.Should().Equal(
            new BuildCommitConflict(2, BuildCommit), new BuildCommitConflict(3, "7654321"));
        VerifyNoCloneOrCheck(check);
    }

    [Test]
    public async Task MissingBuildIsReportedBeforeValidatingCommits()
    {
        _bar.Setup(bar => bar.GetBuildAsync(42, false)).ReturnsAsync((Build)null!);

        Func<Task> action = () => CreateChecker().CheckAsync(
            42, [new RepositoryCommit("not-a-repository", "bad")], CancellationToken.None);

        await action.Should().ThrowAsync<DarcException>().WithMessage("*Build 42 was not found*");
        _bar.Verify(bar => bar.GetBuildGraphAsync(It.IsAny<int>()), Times.Never);
    }

    [TestCase(404)]
    [TestCase(500)]
    public async Task OnlyAnApiNotFoundResponseBecomesAMissingBuildError(int status)
    {
        var request = HttpClientTransport.Shared.CreateRequest();
        request.Uri.Reset(new Uri("https://example.test/"));
        Mock<Response> response = new();
        response.SetupGet(x => x.Status).Returns(status);
        response.SetupGet(x => x.ReasonPhrase).Returns("test");
        var error = new RestApiException(request, response.Object, null!);
        _bar.Setup(bar => bar.GetBuildAsync(42, false)).ThrowsAsync(error);
        Func<Task> action = () => CreateChecker().CheckAsync(
            42, [new RepositoryCommit("not-a-repository", "bad")], CancellationToken.None);

        if (status == 404)
        {
            await action.Should().ThrowAsync<DarcException>().WithMessage("*Build 42 was not found*");
        }
        else
        {
            (await action.Should().ThrowAsync<RestApiException>()).Which.Should().BeSameAs(error);
        }
    }

    private BuildCommitInclusionChecker CreateChecker(params ICommitInclusionCheck[] checks)
        => new(_bar.Object, _vmrClones.Object, _repositoryClones.Object, checks, NullLogger<BuildCommitInclusionChecker>.Instance);

    private void SetGraph(Build root, params Build[] dependencies)
    {
        _bar.Setup(bar => bar.GetBuildAsync(root.Id, false)).ReturnsAsync(root);
        _bar.Setup(bar => bar.GetBuildGraphAsync(root.Id)).ReturnsAsync([root, .. dependencies]);
    }

    private static Mock<ICommitInclusionCheck> CreateCheck(string name, bool matches, List<string> calls)
    {
        Mock<ICommitInclusionCheck> check = new();
        check.SetupGet(x => x.Name).Returns(name);
        check.Setup(x => x.IsIncludedAsync(It.IsAny<CommitInclusionCheckContext>(), It.IsAny<CancellationToken>()))
            .Callback(() => calls.Add(name))
            .ReturnsAsync(matches);
        return check;
    }

    private void VerifyNoCloneOrCheck(Mock<ICommitInclusionCheck> check)
    {
        _repositoryClones.Verify(clones => clones.PrepareCloneAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _vmrClones.Verify(clones => clones.PrepareVmrAsync(
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<IReadOnlyCollection<string>>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Never);
        check.Verify(x => x.IsIncludedAsync(It.IsAny<CommitInclusionCheckContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

[TestFixture]
public class AncestorCommitInclusionCheckTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task UsesGitAncestry(bool isAncestor)
    {
        Mock<ILocalGitRepo> repository = new();
        repository.Setup(repo => repo.IsAncestorCommit(RequestedCommit, BuildCommit)).ReturnsAsync(isAncestor);
        var check = new AncestorCommitInclusionCheck();

        bool result = await check.IsIncludedAsync(
            new CommitInclusionCheckContext(repository.Object, RequestedCommit, BuildCommit), CancellationToken.None);

        result.Should().Be(isAncestor);
        repository.Verify(repo => repo.IsAncestorCommit(RequestedCommit, BuildCommit), Times.Once);
    }
}
