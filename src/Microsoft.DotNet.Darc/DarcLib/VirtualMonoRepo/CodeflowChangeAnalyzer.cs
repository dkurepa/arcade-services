// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.DotNet.DarcLib.Helpers;
using Microsoft.DotNet.DarcLib.Models;
using Microsoft.DotNet.DarcLib.Models.VirtualMonoRepo;
using Microsoft.DotNet.ProductConstructionService.Client.Models;
using Microsoft.Extensions.Logging;

#nullable enable
namespace Microsoft.DotNet.DarcLib.VirtualMonoRepo;

public interface ICodeflowChangeAnalyzer
{
    Task<bool> ForwardFlowHasMeaningfulChangesAsync(string mappingName, string headBranch, string targetBranch);

    /// <summary>
    /// Verifies that a forward-flow codeflow PR (source repo -> VMR) faithfully contains the source
    /// repo's commit diff (oldSha...newSha), accounting for the expected divergences (path remap,
    /// excludes, eng/common, version files, no-ops).
    /// </summary>
    Task<SourceDiffVerificationResult> VerifyForwardFlowAsync(
        string mappingName,
        string sourceRepoUri,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// This class can analyze changes during codeflows and tell if there are any meaningful ones that are worth flowing.
/// </summary>
public class CodeflowChangeAnalyzer : ICodeflowChangeAnalyzer
{
    // List of characters that belong to the git diff format.
    // Example diff output:
    // diff --git a/src/product-repo1/eng/Versions.props b/src/product-repo1/eng/Versions.props
    // index fb13f6d..76d73de 100644
    // --- a/src/product-repo1/eng/Versions.props
    // +++ b/src/product-repo1/eng/Versions.props
    // @@ -10,2 +10,2 @@
    // -    <PackageA1PackageVersion>1.0.0</PackageA1PackageVersion>
    // -    <PackageB1PackageVersion>2.0.0</PackageB1PackageVersion>
    // +    <PackageA1PackageVersion>2.0.1</PackageA1PackageVersion>
    // +    <PackageB1PackageVersion>2.0.1</PackageB1PackageVersion>
    private static readonly IReadOnlyCollection<string> IgnoredDiffLines = ["diff --git", "index ", "@@ ", "--- ", "+++ "];

    private readonly ILocalGitRepoFactory _localGitRepoFactory;
    private readonly IVersionDetailsParser _versionDetailsParser;
    private readonly IBasicBarClient _barClient;
    private readonly IRepositoryCloneManager _cloneManager;
    private readonly IVmrDependencyTracker _dependencyTracker;
    private readonly ISourceManifest _sourceManifest;
    private readonly IVmrInfo _vmrInfo;
    private readonly ILogger<CodeflowChangeAnalyzer> _logger;

    public CodeflowChangeAnalyzer(
        ILocalGitRepoFactory localGitRepoFactory,
        IVersionDetailsParser versionDetailsParser,
        IBasicBarClient barClient,
        IRepositoryCloneManager cloneManager,
        IVmrDependencyTracker dependencyTracker,
        ISourceManifest sourceManifest,
        IVmrInfo vmrInfo,
        ILogger<CodeflowChangeAnalyzer> logger)
    {
        _localGitRepoFactory = localGitRepoFactory;
        _versionDetailsParser = versionDetailsParser;
        _barClient = barClient;
        _cloneManager = cloneManager;
        _dependencyTracker = dependencyTracker;
        _sourceManifest = sourceManifest;
        _vmrInfo = vmrInfo;
        _logger = logger;
    }

    /// <summary>
    /// Checks whether the flow contains meaningful changes that warrant a PR creation.
    /// If it only contains version file changes that happened during the last backflow, we can skip it.
    /// Example:
    ///   - Changed files are only `source-manifest.json` and version files (Version.Details.xml, Versions.props,
    ///   global.json...)
    ///   - The version file changes are only bumps of dependencies that were backflowed
    /// </summary>
    /// <returns> True, if there are no meaningful changes that warrant a PR creation</returns>
    public async Task<bool> ForwardFlowHasMeaningfulChangesAsync(
        string mappingName,
        string headBranch,
        string targetBranch)
    {
        _logger.LogInformation("Checking if the flow can be skipped for {mappingName}", mappingName);

        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        var commonAncestor = await vmr.GetMergeBaseAsync(headBranch, targetBranch);
        var changedFiles = await vmr.GetChangedFilesAsync(commonAncestor, headBranch);

        if (HasSourceChanges(mappingName, changedFiles))
        {
            return true;
        }

        if (await HasMeaningfulVersioningChanges(mappingName, headBranch, commonAncestor))
        {
            return true;
        }

        _logger.LogInformation("No meaningful changes detected, code flow can be skipped");
        return false;
    }

    private bool HasSourceChanges(string mappingName, IReadOnlyCollection<string> changedFiles)
    {
        var mappingSrc = VmrInfo.GetRelativeRepoSourcesPath(mappingName);

        var versionDetailsPropsFile =
            VmrInfo.GetRelativeRepoSourcesPath(mappingName) / VersionFiles.VersionDetailsProps;

        var repoVersionFilesInVmr = DependencyFileManager.CodeflowDependencyFiles
            .Select(file => new UnixPath(mappingSrc) / file)
            .Append(versionDetailsPropsFile)
            .Select(unixPath => unixPath.ToString())
            .ToHashSet();

        var meaningfulChanges = changedFiles
            .Where(file => file.StartsWith(mappingSrc, StringComparison.OrdinalIgnoreCase))
            .Where(file => !repoVersionFilesInVmr.Any(
                v => v.Equals(file, StringComparison.OrdinalIgnoreCase)));

        if (mappingName != VmrInfo.ArcadeMappingName)
        {
            // for repos that are not arcade, we don't include their eng/common folder
            var mappingEngCommon = mappingSrc / Constants.CommonScriptFilesPath;

            meaningfulChanges = meaningfulChanges
                .Where(file => !file.StartsWith(mappingEngCommon, StringComparison.OrdinalIgnoreCase));
        }

        if (meaningfulChanges.Any())
        {
            _logger.LogInformation("Flow contains source changes that warrant PR creation");
            return true;
        }

        return false;
    }

    private async Task<bool> HasMeaningfulVersioningChanges(
        string mappingName,
        string headBranch,
        string ancestorCommit)
    {
        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);

        var versionFileInclusionRules = DependencyFileManager.CodeflowDependencyFiles
            .Select(VmrPatchHandler.GetInclusionRule)
            .ToList();

        var result = await vmr.ExecuteGitCommand(
        [
            "diff",
            "-U0",
            $"{ancestorCommit}..{headBranch}",
            "--",
            ..versionFileInclusionRules
        ]);

        result.ThrowIfFailed($"Failed to get the changes between {ancestorCommit} and {headBranch}");

        // We load all different pieces of build information that would be expected in the diff output
        Build? build1 = await GetBuildFromSourceTag(vmr, mappingName, ancestorCommit);
        Build? build2 = await GetBuildFromSourceTag(vmr, mappingName, headBranch);

        List<string> expectedContents =
        [
            ..GetExpectedContentsForBuild(build1),
            ..GetExpectedContentsForBuild(build2),
        ];

        IEnumerable<string> diffLines = result.GetOutputLines();

        if (diffLines.Any(line => ContainsUnexpectedChange(line, expectedContents)))
        {
            _logger.LogInformation("Flow contains version file changes that warrant PR creation");
            return true;
        }

        return false;
    }

    private async Task<Build?> GetBuildFromSourceTag(ILocalGitRepo vmr, string mappingName, string branch)
    {
        string? versionDetailsContent = await vmr.GetFileFromGitAsync(
            VmrInfo.GetRelativeRepoSourcesPath(mappingName) / VersionFiles.VersionDetailsXml,
            branch);

        if (versionDetailsContent == null)
        {
            _logger.LogWarning("Version details of repo {repo} in branch {branch} could not be loaded.",
                mappingName,
                branch);
            return null;
        }

        VersionDetails versionDetails = _versionDetailsParser.ParseVersionDetailsXml(versionDetailsContent);

        return versionDetails.Source?.BarId == null
            ? null
            : await _barClient.GetBuildAsync(versionDetails.Source.BarId.Value);
    }

    private static bool ContainsUnexpectedChange(string line, IEnumerable<string> expectedContents)
    {
        // Characters belonging to the diff command output
        if (IgnoredDiffLines.Any(line.StartsWith))
        {
            return false;
        }

        // Known build data that would appear if only build related changes were made
        if (expectedContents.Any(c => line.Contains(c, StringComparison.InvariantCultureIgnoreCase)))
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> GetExpectedContentsForBuild(Build? b)
    {
        if (b == null)
        {
            return [];
        }

        // We ignore stable feeds added in the backflow too
        var darcFeeds = b.Assets
            .SelectMany(a => a.Locations)
            .Where(l => l.Type == LocationType.NugetFeed)
            .Where(l => l.Location.Contains(FeedConstants.MaestroManagedPublicFeedPrefix)
                     || l.Location.Contains(FeedConstants.MaestroManagedInternalFeedPrefix))
            .Select(l => l.Location)
            .Distinct();

        return
        [
            b.Id.ToString(),
            b.Commit,
            b.GetRepository(),
            ..b.Assets.Select(a => a.Version).Distinct(),
            ..darcFeeds,
        ];
    }

    public async Task<SourceDiffVerificationResult> VerifyForwardFlowAsync(
        string mappingName,
        string sourceRepoUri,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Verifying forward flow PR for {mappingName} against source diff {oldSha}...{newSha}",
            mappingName,
            oldSha,
            newSha);

        ILocalGitRepo vmr = _localGitRepoFactory.Create(_vmrInfo.VmrPath);
        var srcMappingPath = VmrInfo.GetRelativeRepoSourcesPath(mappingName);

        await vmr.CheckoutAsync(vmrHeadBranch);
        await _dependencyTracker.RefreshMetadataAsync();
        SourceMapping mapping = _dependencyTracker.GetMapping(mappingName);

        var exclusionPathspecs = GetDiffFilters(mapping, _sourceManifest);

        // Obtain a local clone of the source repo containing both SHAs.
        ILocalGitRepo sourceRepo = await _cloneManager.PrepareCloneAsync(
            mapping,
            [sourceRepoUri],
            [oldSha, newSha],
            newSha,
            resetToRemote: false,
            cancellationToken);

        // Phase 1 - name-only partition (mapping-relative paths).
        HashSet<string> referenceFiles = await GetReferenceFilesAsync(sourceRepo, mappingName, oldSha, newSha, exclusionPathspecs, cancellationToken);
        HashSet<string> actualFiles = await GetActualFilesAsync(vmr, mappingName, srcMappingPath, vmrTargetBranch, vmrHeadBranch, cancellationToken);

        var intersection = referenceFiles.Where(actualFiles.Contains).ToList();
        var referenceOnly = referenceFiles.Where(f => !actualFiles.Contains(f)).ToList();
        var unexpectedFiles = actualFiles.Where(f => !referenceFiles.Contains(f)).ToList();

        var appliedFiles = new List<string>();
        var noOpFiles = new List<string>();
        var mismatchedFiles = new List<string>();

        // Phase 2 - per-file content compare on the intersection.
        foreach (var file in intersection)
        {
            if (await ChangedLinesMatchAsync(sourceRepo, vmr, file, srcMappingPath, oldSha, newSha, vmrTargetBranch, vmrHeadBranch, cancellationToken))
            {
                appliedFiles.Add(file);
            }
            else
            {
                mismatchedFiles.Add(file);
            }
        }

        // No-op check on files the source changed but the PR did not.
        foreach (var file in referenceOnly)
        {
            if (await IsLegitimateNoOpAsync(sourceRepo, vmr, file, srcMappingPath, newSha, vmrHeadBranch))
            {
                noOpFiles.Add(file);
            }
            else
            {
                mismatchedFiles.Add(file);
            }
        }

        var result = new SourceDiffVerificationResult
        {
            AppliedFiles = appliedFiles,
            NoOpFiles = noOpFiles,
            MismatchedFiles = mismatchedFiles,
            UnexpectedFiles = unexpectedFiles,
        };

        _logger.LogInformation(
            "Source diff verification for {mappingName} complete: {applied} applied, {noOp} no-op, {mismatched} mismatched, {unexpected} unexpected",
            mappingName,
            result.AppliedFiles.Count,
            result.NoOpFiles.Count,
            result.MismatchedFiles.Count,
            result.UnexpectedFiles.Count);

        return result;
    }

    /// <summary>
    /// Builds the git pathspec exclusion rules the same way VmrDiffOperation.GetDiffFilters does:
    /// the mapping's excludes plus submodule paths under the mapping, turned into git exclusion rules.
    /// </summary>
    private static IReadOnlyCollection<string> GetDiffFilters(SourceMapping mapping, ISourceManifest manifest)
    {
        var submodules = manifest.Submodules
            .Where(s => s.Path.StartsWith(mapping.Name + '/', StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Path.Substring(mapping.Name.Length + 1));

        return (mapping.Exclude ?? [])
            .Concat(submodules)
            .Select(VmrPatchHandler.GetExclusionRule)
            .ToList();
    }

    /// <summary>
    /// R - the set of mapping-relative paths the source diff should contribute, after dropping
    /// excluded/submodule paths, eng/common (non-arcade) and version/metadata files.
    /// </summary>
    private async Task<HashSet<string>> GetReferenceFilesAsync(
        ILocalGitRepo sourceRepo,
        string mappingName,
        string oldSha,
        string newSha,
        IReadOnlyCollection<string> exclusionPathspecs,
        CancellationToken cancellationToken)
    {
        var result = await sourceRepo.ExecuteGitCommand(
            ["diff", "--name-only", $"{oldSha}...{newSha}", "--", ".", .. exclusionPathspecs],
            cancellationToken);
        result.ThrowIfFailed($"Failed to get the source diff between {oldSha} and {newSha}");

        return FilterMappingFiles(result.GetOutputLines(), mappingName);
    }

    /// <summary>
    /// A - the set of mapping-relative paths the PR changed under src/&lt;mapping&gt;/, after stripping
    /// the prefix and dropping eng/common (non-arcade) and version/metadata files.
    /// </summary>
    private async Task<HashSet<string>> GetActualFilesAsync(
        ILocalGitRepo vmr,
        string mappingName,
        string srcMappingPath,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken)
    {
        var result = await vmr.ExecuteGitCommand(
            ["diff", "--name-only", $"{vmrTargetBranch}...{vmrHeadBranch}", "--", $"{srcMappingPath}/"],
            cancellationToken);
        result.ThrowIfFailed($"Failed to get the VMR diff between {vmrTargetBranch} and {vmrHeadBranch}");

        var prefix = srcMappingPath + "/";
        var mappingRelativeFiles = result.GetOutputLines()
            .Where(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Substring(prefix.Length));

        return FilterMappingFiles(mappingRelativeFiles, mappingName);
    }

    /// <summary>
    /// Drops eng/common (for non-arcade mappings) and codeflow version/metadata files, which are
    /// expected to legitimately diverge between the source repo and the VMR.
    /// </summary>
    private static HashSet<string> FilterMappingFiles(IEnumerable<string> files, string mappingName)
    {
        var engCommonPrefix = Constants.CommonScriptFilesPath + "/";
        var dropEngCommon = mappingName != VmrInfo.ArcadeMappingName;

        return files
            .Where(f => !DependencyFileManager.CodeflowDependencyFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
            .Where(f => !dropEngCommon || !f.StartsWith(engCommonPrefix, StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Compares the zero-context change lines of a single file between the source diff and the PR.
    /// Lines that belong to the diff format (and are expected to differ) are ignored before comparing.
    /// </summary>
    private async Task<bool> ChangedLinesMatchAsync(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo vmr,
        string file,
        string srcMappingPath,
        string oldSha,
        string newSha,
        string vmrTargetBranch,
        string vmrHeadBranch,
        CancellationToken cancellationToken)
    {
        var sourceResult = await sourceRepo.ExecuteGitCommand(["diff", "-U0", $"{oldSha}...{newSha}", "--", file], cancellationToken);
        sourceResult.ThrowIfFailed($"Failed to get the source diff of {file} between {oldSha} and {newSha}");

        var vmrResult = await vmr.ExecuteGitCommand(["diff", "-U0", $"{vmrTargetBranch}...{vmrHeadBranch}", "--", $"{srcMappingPath}/{file}"], cancellationToken);
        vmrResult.ThrowIfFailed($"Failed to get the VMR diff of {file} between {vmrTargetBranch} and {vmrHeadBranch}");

        var sourceChanges = GetChangeLines(sourceResult);
        var vmrChanges = GetChangeLines(vmrResult);

        return sourceChanges.SequenceEqual(vmrChanges);
    }

    /// <summary>
    /// Keeps only the +/- change lines from a zero-context diff, dropping the diff-format lines that
    /// are expected to differ between the source repo and its VMR copy.
    /// </summary>
    private static List<string> GetChangeLines(ProcessExecutionResult diffResult)
    {
        return diffResult.GetOutputLines()
            .Where(line => (line.StartsWith('+') || line.StartsWith('-')) && !IgnoredDiffLines.Any(line.StartsWith))
            .ToList();
    }

    /// <summary>
    /// A file the source changed but the PR did not is only legitimate when the VMR copy is already
    /// at the source's new state (equal content, or both absent for an already-reconciled deletion).
    /// </summary>
    private async Task<bool> IsLegitimateNoOpAsync(
        ILocalGitRepo sourceRepo,
        ILocalGitRepo vmr,
        string file,
        string srcMappingPath,
        string newSha,
        string vmrHeadBranch)
    {
        var sourceContent = await sourceRepo.GetFileFromGitAsync(file, newSha);
        var vmrContent = await vmr.GetFileFromGitAsync($"{srcMappingPath}/{file}", vmrHeadBranch);

        if (sourceContent == null && vmrContent == null)
        {
            return true;
        }

        return string.Equals(sourceContent, vmrContent, StringComparison.Ordinal);
    }
}
