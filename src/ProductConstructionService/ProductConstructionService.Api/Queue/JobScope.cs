﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using ProductConstructionService.Api.Queue.JobRunners;
using ProductConstructionService.Api.Queue.Jobs;
using ProductConstructionService.Api.Metrics;

namespace ProductConstructionService.Api.Queue;

public class JobScope(
    Action finalizer,
    IServiceScope serviceScope,
    IMetricRecorder metricRecorder) : IDisposable
{
    private readonly IServiceScope _serviceScope = serviceScope;
    private readonly IMetricRecorder _metricRecorder = metricRecorder;
    private Job? _job = null;

    public void Dispose()
    {
        finalizer.Invoke();
        _serviceScope.Dispose();
    }

    public void InitializeScope(Job job)
    {
        _job = job;
    }

    public async Task RunJobAsync(CancellationToken cancellationToken)
    {
        if (_job is null)
        {
            throw new Exception("JobScope not initialized! Call InitializeScope before calling RunJob");
        }

        var jobRunner = _serviceScope.ServiceProvider.GetRequiredKeyedService<IJobRunner>(_job.Type);

        using (IMetricRecorderScope metricScope  = _metricRecorder.RecordJob(_job))
        {
            await jobRunner.RunAsync(_job, cancellationToken);
            metricScope.SetSuccess();
        }
    }
}
