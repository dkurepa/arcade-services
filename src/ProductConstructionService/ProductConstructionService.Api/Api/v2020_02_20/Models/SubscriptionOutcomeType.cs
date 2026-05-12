// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace ProductConstructionService.Api.v2020_02_20.Models;

public enum SubscriptionOutcomeType
{
    Success = 0,
    NoUpdate = 1,
    NotUpdatable = 2,
    Failure = 3,
    UserError = 4,
}
