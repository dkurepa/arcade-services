// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using FluentAssertions;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.DotNet.Internal.DependencyInjection.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NUnit.Framework;
using ProductConstructionService.SubscriptionTriggerer;

namespace SubscriptionTriggerer.Tests;

[TestFixture]
public class DependencyRegistrationTests
{
    [Test]
    public void AreDependenciesRegistered()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Configuration["QueueConnectionString"] = "queueConnectionString";
        builder.Configuration["BuildAssetRegistrySqlConnectionString"] = "barConnectionString";

        builder.ConfigureSubscriptionTriggerer(new InMemoryChannel(), false);

        DependencyInjectionValidation.IsDependencyResolutionCoherent(s =>
        {
            foreach (var descriptor in builder.Services)
            {
                s.Add(descriptor);
            }
        },
        out var message).Should().BeTrue(message);
    }
}
