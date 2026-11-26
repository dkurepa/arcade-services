// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using NUnit.Framework;
using static Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion.BuildCommitInclusionTestData;

namespace Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion;

[TestFixture]
public class BuildCommitInclusionInputTests
{
    [Test]
    public void NormalizesUrlsAndShasAndRemovesDuplicatePairs()
    {
        var build = CreateBuild(1);
        RepositoryCommit[] commits =
        [
            new(" HTTPS://dnceng@dnceng.visualstudio.com/Internal/_git/DOTNET-RUNTIME.git/ ", " ABCDEF1 "),
            new(TargetRepository, "abcdef1"),
            new(TargetRepository.ToUpperInvariant(), "1234567"),
        ];

        var result = BuildCommitInclusionInput.NormalizeAndValidate(1, build, commits);

        result.Should().Equal(
            new RepositoryCommit(TargetRepository, "abcdef1"),
            new RepositoryCommit(TargetRepository, "1234567"));
    }

    [Test]
    public void ReportsAllInvalidCommitInputs()
    {
        var build = CreateBuild(42);
        RepositoryCommit[] commits =
        [
            new("https://github.com/dotnet/runtime", "--bad"),
            new(TargetRepository, "123"),
        ];

        Action action = () => BuildCommitInclusionInput.NormalizeAndValidate(42, build, commits);

        action.Should().Throw<DarcException>()
            .WithMessage("*Commit 1: repository must be an Azure DevOps URL*")
            .WithMessage("*Commit 1: SHA must contain 7 to 40 hexadecimal characters*")
            .WithMessage("*Commit 2: SHA must contain 7 to 40 hexadecimal characters*");
    }

    [Test]
    public void RejectsAnEmptyCommitListAndABuildWithoutAnAzureDevOpsRepository()
    {
        var build = CreateBuild(1, repository: null);

        Action action = () => BuildCommitInclusionInput.NormalizeAndValidate(1, build, []);

        action.Should().Throw<DarcException>()
            .WithMessage("*At least one repository commit must be provided*")
            .WithMessage("*Build 1 does not have an Azure DevOps repository*");
    }

    [Test]
    public void RejectsReleasedNonVmrBuilds()
    {
        var build = CreateBuild(1, released: true);

        Action action = () => BuildCommitInclusionInput.NormalizeAndValidate(
            1, build, [new RepositoryCommit(TargetRepository, RequestedCommit)]);

        action.Should().Throw<DarcException>().WithMessage("*Released non-VMR build 1 cannot be checked*");
    }

    [Test]
    public void VmrBuildOnlyAcceptsVmrCommitsEvenWhenReleased()
    {
        var build = CreateBuild(1, VmrRepository, released: true);
        Action invalid = () => BuildCommitInclusionInput.NormalizeAndValidate(
            1, build, [new RepositoryCommit(TargetRepository, RequestedCommit)]);

        invalid.Should().Throw<DarcException>().WithMessage("*A VMR build can only be checked*");
        BuildCommitInclusionInput.NormalizeAndValidate(
            1, build, [new RepositoryCommit(VmrRepository, RequestedCommit)]).Should().ContainSingle();
    }
}
