// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Net;
using Maestro.Data;
using Microsoft.AspNetCore.ApiVersioning;
using Microsoft.AspNetCore.ApiVersioning.Swashbuckle;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ProductConstructionService.Api.v2020_02_20.Models;

namespace ProductConstructionService.Api.Api.v2020_02_20.Controllers;

/// <summary>
///   Exposes methods to read <see cref="SubscriptionOutcome"/> records produced by
///   subscription trigger operations.
/// </summary>
[Route("subscription-outcomes")]
[ApiVersion("2020-02-20")]
public class SubscriptionOutcomesController : ControllerBase
{
    private const int DefaultLimit = 10;
    private const int MaxLimit = 100;

    private readonly BuildAssetRegistryContext _context;

    public SubscriptionOutcomesController(BuildAssetRegistryContext context)
    {
        _context = context;
    }

    /// <summary>
    ///   Gets the latest <see cref="SubscriptionOutcome"/>s matching the given filters,
    ///   ordered by date descending.
    /// </summary>
    /// <param name="subscriptionId">Filter by subscription id.</param>
    /// <param name="buildId">Filter by build id.</param>
    /// <param name="fromDate">Filter to outcomes occurring on or after this UTC date/time.</param>
    /// <param name="type">Filter by outcome type.</param>
    /// <param name="operationId">Filter by operation id.</param>
    /// <param name="limit">Maximum number of results to return (default 100, max 1000).</param>
    [HttpGet]
    [SwaggerApiResponse(HttpStatusCode.OK, Type = typeof(List<SubscriptionOutcome>), Description = "The list of subscription outcomes")]
    [ValidateModelState]
    public IActionResult ListSubscriptionOutcomes(
        Guid? subscriptionId = null,
        int? buildId = null,
        DateTime? fromDate = null,
        SubscriptionOutcomeType? type = null,
        string? operationId = null,
        int? limit = null)
    {
        IQueryable<Maestro.Data.Models.SubscriptionOutcome> query = _context.SubscriptionOutcomes;

        if (subscriptionId.HasValue)
        {
            query = query.Where(o => o.SubscriptionId == subscriptionId.Value);
        }

        if (buildId.HasValue)
        {
            query = query.Where(o => o.BuildId == buildId.Value);
        }

        if (fromDate.HasValue)
        {
            query = query.Where(o => o.Date >= fromDate.Value);
        }

        if (type.HasValue)
        {
            var dataType = (Maestro.Data.Models.OutcomeType)type.Value;
            query = query.Where(o => o.Type == dataType);
        }

        if (!string.IsNullOrEmpty(operationId))
        {
            query = query.Where(o => o.OperationId == operationId);
        }

        var resultLimit = limit.GetValueOrDefault(DefaultLimit);
        if (resultLimit <= 0)
        {
            return BadRequest(new ApiError("limit must be greater than 0"));
        }

        if (resultLimit > MaxLimit)
        {
            resultLimit = MaxLimit;
        }

        List<SubscriptionOutcome> results =
        [
            .. query
                .OrderByDescending(o => o.Date)
                .Take(resultLimit)
                .AsEnumerable()
                .Select(o => new SubscriptionOutcome(o))
        ];

        return Ok(results);
    }
}
