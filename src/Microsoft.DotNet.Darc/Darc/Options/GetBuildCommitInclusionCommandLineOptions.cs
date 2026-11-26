// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.IO;
using Microsoft.DotNet.Darc.Operations;
using Microsoft.DotNet.DarcLib.BuildCommitInclusion;
using Microsoft.DotNet.DarcLib.VirtualMonoRepo;
using Microsoft.Extensions.DependencyInjection;

#nullable enable
namespace Microsoft.DotNet.Darc.Options;

internal class GetBuildCommitInclusionCommandLineOptions : CommandLineOptions<GetBuildCommitInclusionOperation>
{
    public override string Verb => "get-build-commit-inclusion";

    public override string VerbDescription => "Reports whether repository commits are included in a BAR build.";

    public int Build { get; set; }

    public string[] Commits { get; set; } = [];

    protected override void Configure()
    {
        Add<int>("--build", "BAR build id.", value => Build = value, required: true);
        AddValueList("commits", "Azure DevOps repository and commit pairs in <repoUrl>@<sha> format.", true, value => Commits = value);
        base.Configure();
    }

    public override bool IsOutputFormatSupported()
        => OutputFormat == DarcOutputType.json || base.IsOutputFormatSupported();

    public override IServiceCollection RegisterServices(IServiceCollection services)
    {
        string tmpPath = Environment.GetEnvironmentVariable("DARC_TMP_DIR")
            ?? Path.Combine(Path.GetTempPath(), Constants.DefaultDarcClonesDirectoryName);
        Directory.CreateDirectory(tmpPath);

        services.AddCodeflow(tmpPath, vmrPath: null, GitLocation);
        services.AddBuildCommitInclusion();
        return base.RegisterServices(services);
    }
}
