// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;

#nullable enable
namespace Microsoft.DotNet.DarcLib.BuildCommitInclusion;

internal sealed class RepositoryUrlComparer : IEqualityComparer<string>
{
    public static RepositoryUrlComparer Instance { get; } = new();

    public static string Normalize(string repository)
    {
        string url = AzureDevOpsClient.NormalizeUrl(repository.Trim().ToLowerInvariant());

        if (url.StartsWith("http://", StringComparison.Ordinal))
        {
            string httpsUrl = AzureDevOpsClient.NormalizeUrl("https://" + url["http://".Length..]);
            url = "http://" + httpsUrl["https://".Length..];
        }

        url = url.TrimEnd('/');
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        return url;
    }

    public bool Equals(string? x, string? y)
        => x is null || y is null
            ? x is null && y is null
            : StringComparer.OrdinalIgnoreCase.Equals(Normalize(x), Normalize(y));

    public int GetHashCode(string repository)
        => StringComparer.OrdinalIgnoreCase.GetHashCode(Normalize(repository));
}
