// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix
// - https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/74591294646621a7c77b0b8bfa8bbb5b694ca660/StyleCop.Analyzers/StyleCopTester/Program.cs#L314
// - https://github.com/kzu/AutoCodeFix/blob/50aace00922eb35d89bcdc77ef83da50e06fd50c/src/AutoCodeFix.Tasks/ApplyCodeFixes.cs

using Humanizer;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;

namespace PrincipleStudios.CodeFixes;

class ProjectFixer
{
    private readonly ILogger<ProjectFixer> logger;
    private readonly ConcurrentDictionary<string, int> appliedFixes = new ();

    public ProjectFixer(ILogger<ProjectFixer> logger)
    {
        this.logger = logger;
    }

    internal async Task<bool> FixProject(Project project, Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, AnalyzerData analyzers, CancellationToken cancellationToken)
    {

        var targetFixableAnalyzers = (from analyzer in project.AnalyzerReferences
                                      let key = new AnalyzerKey(analyzer.Id, project.Language)
                                      where analyzers.Fixable.ContainsKey(key)
                                      let fixable = analyzers.Fixable[key]
                                      from entry in fixable
                                      group entry by (AnalyzerId: analyzer.Id, project.Language, entry.Id) into entries
                                      select (entries.Key.AnalyzerId, entries.Key.Language, entries.Key.Id, entries.First().Analyzer, CodeFixProviders: entries.SelectMany(e => e.CodeFixProviders).Distinct().ToImmutableArray()));
        var fixProviders = targetFixableAnalyzers.ToDictionary(e => e.Id, e => e.CodeFixProviders);
        var targetAnalyzers = targetFixableAnalyzers.Select(e => e.Analyzer).Distinct().ToImmutableArray();

        var hardFailure = false;
        var successful = true;
        //var allDiagnostics = await GetAllFixableDiagnostics(project, targetAnalyzers);
        while (!hardFailure && (await GetNextFixableDiagnostic(project, targetAnalyzers, fixProviders)) is (var allDiagnostics, Diagnostic diagnostic))
        {
            var document = project.GetDocument(diagnostic.Location.SourceTree);
            if (document == null)
                throw new InvalidOperationException("Failed to locate document from diagnostic.");

            var nextProviders = fixProviders[diagnostic.Id];

            var manyResult = await FixMany(project, workspace, cancellationToken, allDiagnostics, diagnostic, nextProviders);
            if (manyResult.HardFailure)
                return false;
            if (!manyResult.Successful)
            {
                var result = await FixOne(workspace, hardFailure, diagnostic, document, nextProviders, cancellationToken);
                if (result.HardFailure)
                    return false;
                successful = successful && result.Successful;
            }

            if (!workspace.TryApplyChanges(workspace.CurrentSolution))
                throw new InvalidOperationException("Failed to apply changes to workspace.");

            project = workspace.CurrentSolution.GetProject(project.Id)!;
        }

        return successful;
    }

    private async Task<(bool HardFailure, bool Successful)> FixMany(Project project, Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, CancellationToken cancellationToken, ImmutableArray<Diagnostic> allDiagnostics, Diagnostic diagnostic, ImmutableArray<CodeFixProvider> nextProviders)
    {
        if (appliedFixes.ContainsKey(diagnostic.Id))
        {
            logger.ApplyingCodeFixForMultipleDidNotMakeAllFixes(diagnostic.Id, diagnostic.GetMessage());
            return (true, false);
        }
        var successful = false;
        foreach (var provider in nextProviders)
        {
            // See if we can get a FixAll provider for the diagnostic we are trying to fix.
            if (provider.GetFixAllProvider() is FixAllProvider fixAll &&
                fixAll != null &&
                fixAll.GetSupportedFixAllDiagnosticIds(provider).Contains(diagnostic.Id) &&
                fixAll.GetSupportedFixAllScopes().Contains(FixAllScope.Project))
            {
                var equivalenceGroups = await CodeFixEquivalenceGroup.CreateAsync(provider, new Dictionary<ProjectId, ImmutableArray<Diagnostic>> { [project.Id] = allDiagnostics }.ToImmutableDictionary(), workspace.CurrentSolution, cancellationToken);
                foreach (var equivalenceGroup in equivalenceGroups)
                {
                    try
                    {
                        logger.ApplyingCodeFixForMultiple(equivalenceGroup.DocumentDiagnosticsToFix[project.Id].Count, diagnostic.Id, diagnostic.GetMessage());


                        var operations = await equivalenceGroup.GetOperationsAsync(cancellationToken);
                        var applyChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                        if (applyChanges == null)
                        {
                            logger.NoApplicableChangesWereProvidedForMultiple(equivalenceGroup.DocumentDiagnosticsToFix[project.Id].Count, diagnostic.Id, diagnostic.GetMessage());
                            continue;
                        }

                        applyChanges.Apply(workspace, cancellationToken);
                        appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + equivalenceGroup.DocumentDiagnosticsToFix[project.Id].Count;
                        successful = true;
                    }
                    catch (Exception e)
                    {
                        logger.FailedToApplyChangeForMultiple(equivalenceGroup.DocumentDiagnosticsToFix[project.Id].Count, diagnostic.Id, diagnostic.GetMessage(), e);
                        return (true, false);
                    }
                }
            }
        }
        return (false, successful);
    }

    private async Task<(bool HardFailure, bool Successful)> FixOne(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, bool hardFailure, Diagnostic diagnostic, Document document, ImmutableArray<CodeFixProvider> nextProviders, CancellationToken cancellationToken)
    {
        var successful = false;
        foreach (var provider in nextProviders)
        {
            CodeAction? codeAction = null;

            await provider.RegisterCodeFixesAsync(
                new CodeFixContext(document, diagnostic,
                (action, diag) => codeAction = action,
                cancellationToken));

            if (codeAction == null)
                continue;

            // Try applying the individual fix in a specific document
            try
            {
                logger.ApplyingCodeFixFor(diagnostic.Id, diagnostic.GetMessage());
                if (await TryApplyChanges(workspace, document, codeAction, cancellationToken))
                {
                    // We successfully applied one code action for the given diagnostics, 
                    // consider it fixed even if there are other providers.

                    appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + 1;
                    successful = true;
                    return (false, successful);
                }
                else
                {
                    logger.NoApplicableChangesWereProvided(codeAction.Title, diagnostic.Id, diagnostic.GetMessage(), diagnostic.Location.GetLineSpan());
                    continue;
                }
            }
            catch (Exception e)
            {
                logger.FailedToApplyChange(codeAction.Title, diagnostic.Id, diagnostic.GetMessage(), e, diagnostic.Location.GetLineSpan());
                return (true, false);
            }
        }

        return (false, successful);
    }

    private static async Task<bool> TryApplyChanges(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, Document document, CodeAction codeAction, CancellationToken cancellationToken)
    {
        var operations = await codeAction.GetOperationsAsync(cancellationToken);
        var applyChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyChanges == null)
            return false;

        applyChanges.Apply(workspace, cancellationToken);

        return true;
    }

    private async Task<(ImmutableArray<Diagnostic> allDiagnostics, Diagnostic? nextFixable)> GetNextFixableDiagnostic(Project project, ImmutableArray<DiagnosticAnalyzer> targetAnalyzers, Dictionary<string, ImmutableArray<CodeFixProvider>> fixProviders)
    {
        var getNextWatch = Stopwatch.StartNew();

        var diagnostics = await GetAllFixableDiagnostics(project, targetAnalyzers);
        var nextDiagnostic = GetNextFixableDiagnostic(fixProviders, diagnostics);

        getNextWatch.Stop();

        if (nextDiagnostic == null)
            logger.DidNotFindFixableDiagnostics(getNextWatch.Elapsed.Humanize());
        else
            logger.FoundFixableDiagnostic(nextDiagnostic.Id, getNextWatch.Elapsed.Humanize());

        return (diagnostics, nextDiagnostic);
    }

    private static Diagnostic? GetNextFixableDiagnostic(Dictionary<string, ImmutableArray<CodeFixProvider>> fixProviders, ImmutableArray<Diagnostic> diagnostics)
    {
        return diagnostics.Where(diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden).Where(d => fixProviders.ContainsKey(d.Id)).FirstOrDefault();
    }

    static async Task<ImmutableArray<Diagnostic>> GetAllFixableDiagnostics(Project project, ImmutableArray<DiagnosticAnalyzer> targetAnalyzers)
    {
        var compilation = await project.GetCompilationAsync();

        if (compilation == null)
            return ImmutableArray<Diagnostic>.Empty;

        var analyzed = compilation.WithAnalyzers(targetAnalyzers, project.AnalyzerOptions);
        var allDiagnostics = await analyzed.GetAnalyzerDiagnosticsAsync(targetAnalyzers, default);

        return allDiagnostics;
    }

}
