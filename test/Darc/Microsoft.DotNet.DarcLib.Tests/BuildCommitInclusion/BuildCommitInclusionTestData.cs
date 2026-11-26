// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using Microsoft.DotNet.ProductConstructionService.Client.Models;

#nullable enable
namespace Microsoft.DotNet.DarcLib.Tests.BuildCommitInclusion;

internal static class BuildCommitInclusionTestData
{
    public const string RootRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-root";
    public const string TargetRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-runtime";
    public const string LegacyTargetRepository = "https://dnceng@dnceng.visualstudio.com/internal/_git/dotnet-runtime";
    public const string VmrRepository = "https://dev.azure.com/dnceng/internal/_git/dotnet-dotnet";
    public const string BuildCommit = "abcdef0123456789abcdef0123456789abcdef01";
    public const string RequestedCommit = "1234567";

    public static Build CreateBuild(
        int id,
        string? repository = TargetRepository,
        string commit = BuildCommit,
        bool released = false,
        params BuildRef[] dependencies)
        => new(id, DateTimeOffset.MinValue, 0, released, false, commit, [], [], [.. dependencies], [])
        {
            AzureDevOpsRepository = repository!,
        };

    public static BuildRef Product(int id) => new(id, true, 0);

    public static BuildRef Toolset(int id) => new(id, false, 0);
}
