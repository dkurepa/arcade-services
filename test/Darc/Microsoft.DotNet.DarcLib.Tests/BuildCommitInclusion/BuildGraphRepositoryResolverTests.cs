// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using NUnit.Framework;
using static Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion.BuildCommitInclusionTestData;

namespace Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion;

[TestFixture]
public class BuildGraphRepositoryResolverTests
{
    [Test]
    public void ResolvesTheRootAndMatchesLegacyAzureDevOpsUrls()
    {
        var root = CreateBuild(30, LegacyTargetRepository);

        var result = BuildGraphRepositoryResolver.Resolve(30, [root], [TargetRepository]);

        result[TargetRepository].Status.Should().Be(RepositoryInclusionStatus.Resolved);
        result[TargetRepository].Build.Should().BeSameAs(root);
    }

    [Test]
    public void ResolvesASingleProductDependency()
    {
        var root = CreateBuild(30, RootRepository, dependencies: [Product(20)]);
        var target = CreateBuild(20, TargetRepository);

        var result = BuildGraphRepositoryResolver.Resolve(30, [root, target], [TargetRepository]);

        result[TargetRepository].Status.Should().Be(RepositoryInclusionStatus.Resolved);
        result[TargetRepository].Build.Should().BeSameAs(target);
    }

    [Test]
    public void SkipsReleasedBuildsToolsetEdgesAndTheirSubtrees()
    {
        var root = CreateBuild(1, RootRepository, dependencies: [Product(2), Toolset(3), Product(6)]);
        var released = CreateBuild(2, RootRepository, released: true, dependencies: [Product(4)]);
        var toolset = CreateBuild(3, RootRepository, dependencies: [Product(5)]);
        var onlyGitHubRepository = CreateBuild(6, repository: null);
        onlyGitHubRepository.GitHubRepository = TargetRepository;

        var result = BuildGraphRepositoryResolver.Resolve(
            1,
            [root, released, toolset, CreateBuild(4), CreateBuild(5), onlyGitHubRepository],
            [TargetRepository]);

        result[TargetRepository].Status.Should().Be(RepositoryInclusionStatus.RepositoryNotInBuild);
        result[TargetRepository].Build.Should().BeNull();
    }

    [Test]
    public void ChoosesTheLowestBuildIdWhenMatchingCommitsAreEqual()
    {
        var root = CreateBuild(50, RootRepository, dependencies: [Product(30), Product(20)]);
        var first = CreateBuild(30);
        var lowest = CreateBuild(20, LegacyTargetRepository, commit: BuildCommit.ToUpperInvariant());

        var result = BuildGraphRepositoryResolver.Resolve(50, [root, first, lowest], [TargetRepository]);

        result[TargetRepository].Status.Should().Be(RepositoryInclusionStatus.Resolved);
        result[TargetRepository].Build.Should().BeSameAs(lowest);
        result[TargetRepository].Conflicts.Should().BeNull();
    }

    [Test]
    public void ReportsAllConflictingBuildsAfterTraversingTheWholeGraph()
    {
        var root = CreateBuild(50, RootRepository, dependencies: [Product(30)]);
        var first = CreateBuild(30, TargetRepository, dependencies: [Product(20)]);
        var second = CreateBuild(20, TargetRepository, commit: "7654321");

        var result = BuildGraphRepositoryResolver.Resolve(50, [root, first, second], [TargetRepository]);

        result[TargetRepository].Status.Should().Be(RepositoryInclusionStatus.IncoherentGraph);
        result[TargetRepository].Build.Should().BeNull();
        result[TargetRepository].Conflicts.Should().Equal(
            new BuildCommitConflict(20, "7654321"),
            new BuildCommitConflict(30, BuildCommit));
    }
}
