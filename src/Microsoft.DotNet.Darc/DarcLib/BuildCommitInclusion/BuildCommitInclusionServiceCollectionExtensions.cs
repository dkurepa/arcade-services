// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

public static class BuildCommitInclusionServiceCollectionExtensions
{
    public static IServiceCollection AddBuildCommitInclusion(this IServiceCollection services)
    {
        services.TryAddTransient<IBuildCommitInclusionChecker, BuildCommitInclusionChecker>();
        services.AddTransient<ICommitInclusionCheck, AncestorCommitInclusionCheck>();
        services.AddTransient<ICommitInclusionCheck, PatchReverseApplyCommitInclusionCheck>();
        return services;
    }
}
