// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

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

            //var manyResult = await FixMany(project, workspace, cancellationToken, allDiagnostics, diagnostic, nextProviders);
            //if (manyResult.HardFailure)
            //    return false;
            //if (!manyResult.Successful)
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


                //string codeActionEquivalenceKey;
                //FixAllContext.DiagnosticProvider fixAllDiagnosticProvider;
                //var diagnosticIds = allDiagnostics.Select(d => d.Id).Intersect(fixAll.GetSupportedFixAllDiagnosticIds(provider)).ToArray();
                //var matching = allDiagnostics.Where(d => provider.FixableDiagnosticIds.Contains(d.Id)).ToArray();

                //var codeAction = await fixAll.GetFixAsync(new FixAllContext(project, provider, FixAllScope.Project, codeActionEquivalenceKey, diagnosticIds, fixAllDiagnosticProvider, default));


                //if (matching.Length == 0)
                //    continue;

                // the following code adapted from: https://github.com/kzu/AutoCodeFix/blob/50aace00922eb35d89bcdc77ef83da50e06fd50c/src/AutoCodeFix.Tasks/ApplyCodeFixes.cs
                //foreach (var fix in matching)
                //{
                //    try
                //    {
                //        logger.ApplyingCodeFixForMultiple(fix.NumberOfDiagnostics);
                //        LogMessage($"Calculating fix for {fix.NumberOfDiagnostics} instances.", MessageImportance.Low.ForVerbosity(verbosity));
                //        var operations = await fix.GetOperationsAsync(cancellationToken);
                //        var fixAllChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                //        if (fixAllChanges != null)
                //        {
                //            fixAllChanges.Apply(workspace, cancellationToken);
                //            fixApplied = true;
                //            appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + fix.NumberOfDiagnostics;
                //            project = workspace.CurrentSolution.GetProject(project.Id);
                //            watch.Stop();
                //        }

                //        LogMessage($"Applied batch changes in {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}. This is {fix.NumberOfDiagnostics / fixAllWatch.Elapsed.TotalSeconds:0.000} instances/second.", MessageImportance.Low.ForVerbosity(verbosity));
                //    }
                //    catch (Exception ex)
                //    {
                //        // Report thrown exceptions
                //        LogMessage($"The fix '{fix.CodeFixEquivalenceKey}' failed after {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}: {ex.ToString()}", MessageImportance.High.ForVerbosity(verbosity));
                //    }
                //}


                ////var group = await CodeFixEquivalenceGroup.CreateAsync(provider, ImmutableDictionary.CreateRange(new[]
                ////{
                ////                new KeyValuePair<ProjectId, ImmutableArray<Diagnostic>>(project.Id, diagnostics)
                ////            }), project.Solution, Token);

                ////// TODO: should we only apply one equivalence group at a time? See https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/74591294646621a7c77b0b8bfa8bbb5b694ca660/StyleCop.Analyzers/StyleCopTester/Program.cs#L314
                ////if (group.Length > 0)
                ////{
                ////    LogMessage($"Applying batch code fix for {diagnostic.Id}: {diagnostic.Descriptor.Title}", MessageImportance.Normal.ForVerbosity(verbosity));
                ////    var fixAllWatch = Stopwatch.StartNew();
                ////    foreach (var fix in group)
                ////    {
                ////        try
                ////        {
                ////            LogMessage($"Calculating fix for {fix.NumberOfDiagnostics} instances.", MessageImportance.Low.ForVerbosity(verbosity));
                ////            var operations = await fix.GetOperationsAsync(cancellationToken);
                ////            var fixAllChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
                ////            if (fixAllChanges != null)
                ////            {
                ////                fixAllChanges.Apply(workspace, cancellationToken);
                ////                fixApplied = true;
                ////                appliedFixes[diagnostic.Id] = appliedFixes.GetOrAdd(diagnostic.Id, 0) + fix.NumberOfDiagnostics;
                ////                project = workspace.CurrentSolution.GetProject(project.Id);
                ////                watch.Stop();
                ////            }

                ////            LogMessage($"Applied batch changes in {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}. This is {fix.NumberOfDiagnostics / fixAllWatch.Elapsed.TotalSeconds:0.000} instances/second.", MessageImportance.Low.ForVerbosity(verbosity));
                ////        }
                ////        catch (Exception ex)
                ////        {
                ////            // Report thrown exceptions
                ////            LogMessage($"The fix '{fix.CodeFixEquivalenceKey}' failed after {TimeSpan.FromMilliseconds(fixAllWatch.ElapsedMilliseconds).Humanize()}: {ex.ToString()}", MessageImportance.High.ForVerbosity(verbosity));
                ////        }
                ////    }
                ////}
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

    private async Task<bool> TryApplyChanges(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace, Document document, CodeAction codeAction, CancellationToken cancellationToken)
    {
        var operations = await codeAction.GetOperationsAsync(cancellationToken);
        var applyChanges = operations.OfType<ApplyChangesOperation>().FirstOrDefault();
        if (applyChanges == null)
            return false;

        applyChanges.Apply(workspace, cancellationToken);

        //// According to https://github.com/DotNetAnalyzers/StyleCopAnalyzers/pull/935 and 
        //// https://github.com/dotnet/roslyn-sdk/issues/140, Sam Harwell mentioned that we should 
        //// be forcing a re-parse of the document syntax tree at this point. 
        //var newDoc = await workspace.CurrentSolution.GetDocument(document.Id)!.RecreateDocumentAsync(cancellationToken);

        //if (!workspace.TryApplyChanges(newDoc.Project.Solution))
        //    throw new InvalidOperationException("Failed to apply changes to workspace.");

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

    async Task<ImmutableArray<Diagnostic>> GetAllFixableDiagnostics(Project project, ImmutableArray<DiagnosticAnalyzer> targetAnalyzers)
    {
        var compilation = await project.GetCompilationAsync();

        if (compilation == null)
            return ImmutableArray<Diagnostic>.Empty;

        var analyzed = compilation.WithAnalyzers(targetAnalyzers, project.AnalyzerOptions);
        var allDiagnostics = await analyzed.GetAnalyzerDiagnosticsAsync(targetAnalyzers, default);

        return allDiagnostics;
    }

}

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
        Level = LogLevel.Debug,
        Message = "Calculating fix for {numberOfDiagnostics} instances of {diagnosticId}: {diagnosticMessage}.")]
    public static partial void ApplyingCodeFixForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Error,
        Message = "No applicable changes were provided for {numberOfDiagnostics} instances of diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void NoApplicableChangesWereProvidedForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Critical,
        Message = "Failed to apply code for {numberOfDiagnostics} instances of diagnostic {diagnosticId}: {diagnosticMessage}.")]
    public static partial void FailedToApplyChangeForMultiple(this ILogger logger, int numberOfDiagnostics, string diagnosticId, string diagnosticMessage, Exception ex);


}
