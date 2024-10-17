﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ProductConstructionService.Common;

namespace ProductConstructionService.WorkItems;

public static class WorkItemConfiguration
{
    public const string WorkItemQueueNameConfigurationKey = "WorkItemQueueName";
    public const string ReplicaNameKey = "CONTAINER_APP_REPLICA_NAME";
    public const string SubscriptionIdKey = "SubscriptionId";
    public const string ResourceGroupNameKey = "ResourceGroupName";
    public const string ContainerAppNameKey = "ContainerAppName";
    public const int PollingRateSeconds = 10;
    public const string LocalReplicaName = "localReplica";

    internal static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static void AddWorkItemQueues(this IHostApplicationBuilder builder, DefaultAzureCredential credential, bool waitForInitialization)
    {
        builder.AddWorkItemProducerFactory(credential);

        // When running the service locally, the WorkItemProcessor should start in the Working state
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemProcessorState>(
            sp,
            builder.Configuration[ReplicaNameKey] ?? LocalReplicaName,
            new AutoResetEvent(false)));
        builder.Services.AddSingleton(sp => ActivatorUtilities.CreateInstance<WorkItemScopeManager>(
            sp,
            PollingRateSeconds));

        builder.Configuration[$"{WorkItemConsumerOptions.ConfigurationKey}:{WorkItemQueueNameConfigurationKey}"] =
            builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);
        builder.Services.Configure<WorkItemConsumerOptions>(
            builder.Configuration.GetSection(WorkItemConsumerOptions.ConfigurationKey));
        builder.Services.AddHostedService<WorkItemConsumer>();
        builder.Services.AddTransient<IReminderManagerFactory, ReminderManagerFactory>();
        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddTransient<IReplicaWorkItemProcessorStateFactory, LocalReplicaWorkItemProcessorStateFactory>();
        }
        else
        {
            builder.Services.AddTransient(sp =>
                new ArmClient(credential)
                    .GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{builder.Configuration.GetRequiredValue(SubscriptionIdKey)}"))
                    .GetResourceGroups().Get(builder.Configuration.GetRequiredValue(ResourceGroupNameKey)).Value
                    .GetContainerApp(builder.Configuration.GetRequiredValue(ContainerAppNameKey)).Value
            );
            builder.Services.AddTransient<IReplicaWorkItemProcessorStateFactory, ReplicaWorkItemProcessorStateFactory>();
        }
    }

    public static void AddWorkItemProducerFactory(this IHostApplicationBuilder builder, DefaultAzureCredential credential)
    {
        builder.AddAzureQueueClient("queues", settings => settings.Credential = credential);

        var queueName = builder.Configuration.GetRequiredValue(WorkItemQueueNameConfigurationKey);

        builder.Services.AddTransient<IWorkItemProducerFactory>(sp =>
            ActivatorUtilities.CreateInstance<WorkItemProducerFactory>(sp, queueName));
    }

    // When running locally, create the workitem queue, if it doesn't already exist
    public static async Task UseLocalWorkItemQueues(this IServiceProvider serviceProvider, string queueName)
    {
        var queueServiceClient = serviceProvider.GetRequiredService<QueueServiceClient>();
        var queueClient = queueServiceClient.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync();
    }

    public static void AddWorkItemProcessor<TWorkItem, TProcessor>(
            this IServiceCollection services,
            Func<IServiceProvider, TProcessor>? factory = null)
        where TWorkItem : WorkItem
        where TProcessor : class, IWorkItemProcessor
    {
        // We need IOption<WorkItemProcessorRegistrations> where we add the registrations
        services.AddOptions();
        services.TryAddSingleton<WorkItemProcessorRegistrations>();

        var diKey = typeof(TWorkItem).Name;
        if (factory != null)
        {
            services.TryAddKeyedTransient<IWorkItemProcessor>(diKey, (sp, _) => factory(sp));
        }
        else
        {
            services.TryAddKeyedTransient<IWorkItemProcessor, TProcessor>(diKey);
        }

        services.Configure<WorkItemProcessorRegistrations>(registrations =>
        {
            registrations.RegisterProcessor<TWorkItem, TProcessor>();
        });
    }
}
