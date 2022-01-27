// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.Extensions.Logging;

namespace PrincipleStudios.CodeFixes;

public static partial class Log
{
    [LoggerMessage(
        EventId = 0,
        Level = LogLevel.Error,
        Message = "{kind}: {message}")]
    public static partial void WorkspaceFailed(
        this ILogger logger, Microsoft.CodeAnalysis.WorkspaceDiagnosticKind kind, string message);

    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Analyzer assembly {assemblyName} is incompatible with {appName}. Must reference Microsoft.CodeAnalysis version {version} or greater.")]
    public static partial void MinRoslynVersion(this ILogger logger, string assemblyName, string appName, Version version);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Warning,
        Message = "Analyzer Failed to load analyzer assembly {assemblyName}: {message}.")]
    public static partial void FailedToLoadAssembly(this ILogger logger, string assemblyName, string message);
    
}