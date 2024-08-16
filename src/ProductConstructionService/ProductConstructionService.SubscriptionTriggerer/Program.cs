﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
using Azure.Storage.Queues;
using Maestro.Data.Models;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.SubscriptionTriggerer;

if (args.Count() < 1)
{
    Console.WriteLine("Usage: SubscriptionTriggerer <daily|twicedaily|weekly>");
    return 1;
}

InMemoryChannel telemetryChannel = new();
UpdateFrequency frequency = args[0] switch
{
    "daily" => UpdateFrequency.EveryDay,
    "twicedaily" => UpdateFrequency.TwiceDaily,
    "weekly" => UpdateFrequency.EveryWeek,
    _ => throw new ArgumentException($"Invalid frequency ${args[0]} specified")
};

try
{
    var builder = Host.CreateApplicationBuilder();

    bool isDevelopment = builder.Environment.IsDevelopment();

    builder.ConfigureSubscriptionTriggerer(telemetryChannel, isDevelopment);

    ServiceProvider serviceProvider = builder.Services.BuildServiceProvider();

    if (isDevelopment)
    { 
        var client = serviceProvider.GetRequiredService<QueueClient>();
        client.CreateIfNotExists();
    }

    var triggerer = serviceProvider.GetRequiredService<SubscriptionTriggerer>();

    await triggerer.CheckSubscriptionsAsync(frequency);
}
finally
{
    telemetryChannel.Flush();
    await Task.Delay(TimeSpan.FromMilliseconds(1000));
}

return 0;
