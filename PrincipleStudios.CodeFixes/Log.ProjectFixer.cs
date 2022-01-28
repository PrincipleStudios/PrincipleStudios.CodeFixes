// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace PrincipleStudios.CodeFixes;

public static partial class Log
{
    [LoggerMessage(
        EventId = 1000,
        Level = LogLevel.Debug,
        Message = "Did not find more fixable diagnostics in {timeSpan}")]
    public static partial void DidNotFindFixableDiagnostics(this ILogger logger, string timeSpan);

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Debug,
        Message = "Found fixable diagnostic {diagnosticId} in {timeSpan}")]
    public static partial void FoundFixableDiagnostic(this ILogger logger, string diagnosticId, string timeSpan);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Applying code fix for diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void ApplyingCodeFixFor(this ILogger logger, string diagnosticId, string diagnosticMessage);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "{fileLinePositionSpan}: No applicable changes were provided by the code action '{title}' for diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void NoApplicableChangesWereProvided(this ILogger logger, string title, string diagnosticId, string diagnosticMessage, FileLinePositionSpan fileLinePositionSpan);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Critical,
        Message = "{fileLinePositionSpan}: Failed to apply code action '{title}' for diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void FailedToApplyChange(this ILogger logger, string title, string diagnosticId, string diagnosticMessage, Exception ex, FileLinePositionSpan fileLinePositionSpan);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Information,
        Message = "Calculating fix for {numberOfDiagnostics} instances of {diagnosticId}: {diagnosticMessage}.")]
    public static partial void ApplyingCodeFixForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Error,
        Message = "No applicable changes were provided for {numberOfDiagnostics} instances of diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void NoApplicableChangesWereProvidedForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Critical,
        Message = "Failed to apply code for {numberOfDiagnostics} instances of diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void FailedToApplyChangeForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage, Exception ex);


    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Critical,
        Message = "After applying a bulk fix, detected more instances of diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void ApplyingCodeFixForMultipleDidNotMakeAllFixes(this ILogger logger, string diagnosticId, string diagnosticMessage);
    

}
