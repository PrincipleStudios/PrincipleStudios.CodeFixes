// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using System.Collections.Immutable;

namespace PrincipleStudios.CodeFixes;

public record CodeFixData(string Id, DiagnosticAnalyzer Analyzer, ImmutableArray<CodeFixProvider> CodeFixProviders);
