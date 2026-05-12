// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.v2020_02_20.Models;

public class SubscriptionOutcome
{
    public SubscriptionOutcome(Maestro.Data.Models.SubscriptionOutcome other)
    {
        ArgumentNullException.ThrowIfNull(other);

        OperationId = other.OperationId;
        SubscriptionId = other.SubscriptionId;
        BuildId = other.BuildId;
        Date = other.Date;
        Message = other.Message;
        Type = (SubscriptionOutcomeType)other.Type;
    }

    public string OperationId { get; set; }

    public Guid SubscriptionId { get; set; }

    public int BuildId { get; set; }

    public DateTime Date { get; set; }

    public string Message { get; set; }

    public SubscriptionOutcomeType Type { get; set; }
}
