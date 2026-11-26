// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AwesomeAssertions;
using Maestro.Data.Models;

namespace Maestro.DataProviders.Tests;

[TestFixture]
public class SqlBarClientTests
{
    [Test]
    public void ToClientModelBuildMapsTheDependentBuildId()
    {
        var build = new Build
        {
            Id = 101,
            Commit = "abcdef0",
            Released = true,
            DependentBuildIds =
            [
                new BuildDependency
                {
                    BuildId = 101,
                    DependentBuildId = 42,
                    IsProduct = true,
                    TimeToInclusionInMinutes = 1.5,
                },
            ],
        };

        var result = SqlBarClient.ToClientModelBuild(build);

        result.Released.Should().BeTrue();
        result.Dependencies.Should().ContainSingle().Which.Should().BeEquivalentTo(new
        {
            BuildId = 42,
            IsProduct = true,
            TimeToInclusionInMinutes = 1.5,
        });
    }
}
