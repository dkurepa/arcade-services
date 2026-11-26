// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using static Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion.BuildCommitInclusionTestData;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion;

[TestFixture]
public class PatchReverseApplyCommitInclusionCheckTests
{
    private readonly Mock<IVmrPatchHandler> _patches = new();
    private readonly Mock<IVmrInfo> _vmrInfo = new();
    private readonly Mock<IFileSystem> _fileSystem = new();
    private readonly Mock<ILocalGitRepo> _repository = new();
    private readonly NativePath _tmpPath = new(Path.Combine(Path.GetTempPath(), "commit-inclusion-tests"));
    private readonly NativePath _repoPath = new(Path.Combine(Path.GetTempPath(), "commit-inclusion-tests", "repo"));

    [SetUp]
    public void SetUp()
    {
        _patches.Reset();
        _vmrInfo.Reset();
        _fileSystem.Reset();
        _repository.Reset();
        _vmrInfo.SetupGet(info => info.TmpPath).Returns(_tmpPath);
        _repository.SetupGet(repo => repo.Path).Returns(_repoPath);
        _repository.Setup(repo => repo.ExecuteGitCommand(
                It.Is<string[]>(args => args[0] == "rev-list"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { StandardOutput = $"{RequestedCommit} parent\n" });
        _repository.Setup(repo => repo.ExecuteGitCommand(
                It.Is<string[]>(args => args[0] == "reset"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult());
        _patches.Setup(patches => patches.CreateSinglePatchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NativePath>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, string _, string _, NativePath _, CancellationToken _) => new VmrIngestionPatch(path, (string?)null));
        _patches.Setup(patches => patches.ApplyPatch(
                It.IsAny<VmrIngestionPatch>(), It.IsAny<NativePath>(), true, false, true, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<UnixPath>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ReverseApplyReportsInclusionAndAlwaysCleansAndResets(bool applies)
    {
        if (!applies)
        {
            _patches.Setup(patches => patches.ApplyPatch(
                    It.IsAny<VmrIngestionPatch>(), It.IsAny<NativePath>(), true, false, true, true, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new PatchApplicationFailedException(
                    new VmrIngestionPatch("failed.patch", (string?)null),
                    new ProcessExecutionResult { ExitCode = 1 },
                    reverseApply: true));
        }

        bool result = await CreateCheck().IsIncludedAsync(Context(), CancellationToken.None);

        result.Should().Be(applies);
        _patches.Verify(patches => patches.CreateSinglePatchAsync(
            It.Is<string>(path => path.StartsWith(_tmpPath.Path) && path.EndsWith(".patch")),
            $"{RequestedCommit}^", RequestedCommit, _repoPath, It.IsAny<CancellationToken>()), Times.Once);
        _patches.Verify(patches => patches.ApplyPatch(
            It.IsAny<VmrIngestionPatch>(), _repoPath, true, false, true, true, It.IsAny<CancellationToken>()), Times.Once);
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(It.IsAny<string>()), Times.Once);
        VerifyReset();
    }

    [Test]
    public async Task RootCommitUsesTheEmptyTreeAsPatchBase()
    {
        _repository.Setup(repo => repo.ExecuteGitCommand(
                It.Is<string[]>(args => args[0] == "rev-list"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { StandardOutput = $"{RequestedCommit}\n" });

        bool result = await CreateCheck().IsIncludedAsync(Context(), CancellationToken.None);

        result.Should().BeTrue();
        _patches.Verify(patches => patches.CreateSinglePatchAsync(
            It.IsAny<string>(), Constants.EmptyGitObject, RequestedCommit, _repoPath, It.IsAny<CancellationToken>()), Times.Once);
        VerifyReset();
    }

    [Test]
    public async Task PatchCreationFailureStillCleansAndResets()
    {
        _patches.Setup(patches => patches.CreateSinglePatchAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<NativePath>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Patch creation failed"));

        Func<Task> action = () => CreateCheck().IsIncludedAsync(Context(), CancellationToken.None);

        await action.Should().ThrowAsync<IOException>().WithMessage("Patch creation failed");
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(It.IsAny<string>()), Times.Once);
        VerifyReset();
    }

    [Test]
    public async Task CleanupFailurePropagatesButStillResets()
    {
        _fileSystem.Setup(fileSystem => fileSystem.DeleteFile(It.IsAny<string>()))
            .Throws(new IOException("Patch cleanup failed"));

        Func<Task> action = () => CreateCheck().IsIncludedAsync(Context(), CancellationToken.None);

        await action.Should().ThrowAsync<IOException>().WithMessage("Patch cleanup failed");
        VerifyReset();
    }

    [Test]
    public async Task ResetFailurePropagatesEvenAfterSuccessfulApply()
    {
        _repository.Setup(repo => repo.ExecuteGitCommand(
                It.Is<string[]>(args => args[0] == "reset"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ProcessExecutionResult { ExitCode = 1 });

        Func<Task> action = () => CreateCheck().IsIncludedAsync(Context(), CancellationToken.None);

        await action.Should().ThrowAsync<ProcessFailedException>().WithMessage("*Failed to reset the clone*");
        _fileSystem.Verify(fileSystem => fileSystem.DeleteFile(It.IsAny<string>()), Times.Once);
        VerifyReset();
    }

    [Test]
    public async Task CancellationStillResetsWithoutTheCancelledToken()
    {
        using CancellationTokenSource cancellation = new();
        _patches.Setup(patches => patches.ApplyPatch(
                It.IsAny<VmrIngestionPatch>(), It.IsAny<NativePath>(), true, false, true, true, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<IReadOnlyCollection<UnixPath>>(cancellation.Token);
            });

        Func<Task> action = () => CreateCheck().IsIncludedAsync(Context(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        VerifyReset();
    }

    private PatchReverseApplyCommitInclusionCheck CreateCheck()
        => new(_patches.Object, _vmrInfo.Object, _fileSystem.Object, NullLogger<PatchReverseApplyCommitInclusionCheck>.Instance);

    private CommitInclusionCheckContext Context() => new(_repository.Object, RequestedCommit, BuildCommit);

    private void VerifyReset()
        => _repository.Verify(repo => repo.ExecuteGitCommand(
            It.Is<string[]>(args =>
                args.Length == 3 && args[0] == "reset" && args[1] == "--hard" && args[2] == BuildCommit),
            CancellationToken.None), Times.Once);
}
