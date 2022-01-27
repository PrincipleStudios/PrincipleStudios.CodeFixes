// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

namespace PrincipleStudios.CodeFixes;

public record AnalyzerKey(object AnalyzerId, string Language);
