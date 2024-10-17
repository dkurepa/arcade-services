﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.Extensions.Logging;
using ProductConstructionService.Client;

namespace ProductConstructionService.Cli.Operations;
internal class StartPCSOperation : IOperation
{
    private readonly IProductConstructionServiceApi _client;
    private readonly ILogger<GetPCSStatusOperation> _logger;

    public StartPCSOperation(IProductConstructionServiceApi client, ILogger<GetPCSStatusOperation> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<int> RunAsync()
    {
        var statuses = await _client.Status.StartPcsWorkItemProcessorsAsync();
        foreach (var stat in statuses)
        {
            _logger.LogInformation("Replica {replica} has status {status}", stat.Key, stat.Value);
        }
        return 0;
    }
}