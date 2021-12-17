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
}