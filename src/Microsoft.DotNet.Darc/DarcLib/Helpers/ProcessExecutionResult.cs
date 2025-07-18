﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.DotNet.DarcLib.Helpers;

public class ProcessExecutionResult
{
    public bool TimedOut { get; set; }
    public int ExitCode { get; set; }
    public bool Succeeded => !TimedOut && ExitCode == 0;
    public string StandardOutput { get; set; } = string.Empty;
    public string StandardError { get; set; } = string.Empty;

    public void ThrowIfFailed(string failureMessage)
    {
        if (!Succeeded)
        {
            throw new ProcessFailedException(this, failureMessage);
        }
    }

    public IReadOnlyCollection<string> GetOutputLines()
    {
        return [.. StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
    }

    public override string ToString()
    {
        var output = new StringBuilder();
        output.AppendLine($"Exit code: {ExitCode}");

        if (!string.IsNullOrEmpty(StandardOutput))
        {
            output.AppendLine($"Std out:{Environment.NewLine}{StandardOutput}{Environment.NewLine}");
        }

        if (!string.IsNullOrEmpty(StandardError))
        {
            output.AppendLine($"Std err:{Environment.NewLine}{StandardError}{Environment.NewLine}");
        }

        return output.ToString();
    }
}

public class ProcessFailedException(ProcessExecutionResult executionResult, string failureMessage)
    : Exception
{
    public ProcessExecutionResult ExecutionResult { get; } = executionResult;

    public override string Message => failureMessage + Environment.NewLine + ExecutionResult.ToString();
}
