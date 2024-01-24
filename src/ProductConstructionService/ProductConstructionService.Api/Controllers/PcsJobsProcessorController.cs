﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Microsoft.AspNetCore.Mvc;
using ProductConstructionService.Api.Queue;

namespace ProductConstructionService.Api.Controllers;

[Route("pcsJobsProcessor")]
public class PcsJobsProcessorController
    (ILogger<PcsJobsProcessorController> logger,
    PcsJobsProcessorStatus pcsJobsProcessorStatus,
    IHostApplicationLifetime hostApplicationLifetime) : Controller
{
    private readonly ILogger<PcsJobsProcessorController> _logger = logger;
    private readonly PcsJobsProcessorStatus _pcsJobsProcessorStatus = pcsJobsProcessorStatus;
    private readonly IHostApplicationLifetime _hostApplicationLifetime = hostApplicationLifetime;

    private const int StoppedCheckDelaySeconds = 5;

    [HttpPut("stop")]
    public async Task<IActionResult> StopPcsJobsProcessor()
    {
        _logger.LogInformation("Stopping {pcsJobsProcessor}", nameof(PcsJobsProcessor));
        _pcsJobsProcessorStatus.ContinueWorking = false;

        _logger.LogInformation("Waiting for {pcsJobsProcessor} to finish processing current job", nameof(PcsJobsProcessor));

        while (!_pcsJobsProcessorStatus.StoppedWorking)
        {
            await Task.Delay(TimeSpan.FromSeconds(StoppedCheckDelaySeconds));
        }

        return Ok();
    }

    [HttpPut("start")]
    public IActionResult StartPcsJobsProcessor()
    {
        _logger.LogInformation("Starting {pcsJobsProcessor}", nameof(PcsJobsProcessor));
        _pcsJobsProcessorStatus.Reset();

        return Ok();
    }
}
