// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.Reflection;

namespace PrincipleStudios.CodeFixes;

class AnalyzerLoader
{
    static readonly Version MinRoslynVersion = new Version(1, 2);
    private readonly ILogger<AnalyzerLoader> logger;

    public AnalyzerLoader(ILogger<AnalyzerLoader> logger)
    {
        this.logger = logger;
    }

    internal AnalyzerData LoadAnalyzers(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace workspace)
    {
        var assembliesById = (
            from project in workspace.CurrentSolution.Projects
            from analyzer in project.AnalyzerReferences
            let optionalLoad = TryLoad(analyzer) as Optional<Assembly>.Some
            where optionalLoad != null
            select (analyzer.Id, optionalLoad.Value)
        ).ToDictionary(k => k.Id, k => k.Value);

        //var projectsById = workspace.CurrentSolution.Projects.ToDictionary(p => p.Id);
        var languages = workspace.CurrentSolution.Projects.Select(p => p.Language);

        var analyzers = (from project in workspace.CurrentSolution.Projects
                         from analyzer in project.AnalyzerReferences
                         group analyzer by new AnalyzerKey(analyzer.Id, project.Language));
        var analyzersLookup = analyzers
            .ToDictionary(
                analyzerReferences => analyzerReferences.Key,
                analyzerReferences => analyzerReferences.First().GetAnalyzers(analyzerReferences.Key.Language)
            );

        var codeFixProviders = (from x in MefHostServices.Create(assembliesById.Values).GetExports<CodeFixProvider, IDictionary<string, object>>()
                                where ((string[])x.Metadata["Languages"]).Intersect(languages).Any()
                                from diagnosticId in x.Value.FixableDiagnosticIds.Select(id => new { Id = id, Provider = x.Value })
                                group x.Value by diagnosticId.Id)
            .ToDictionary(x => x.Key, x => x.ToImmutableArray());

        var fixable = analyzers
            .ToDictionary(
                analyzerReferences => analyzerReferences.Key,
                analyzerReferences => (from analyzer in analyzerReferences.First().GetAnalyzers(analyzerReferences.Key.Language)
                                      from id in analyzer.SupportedDiagnostics.Select(d => d.Id)
                                      where codeFixProviders.ContainsKey(id)
                                      let codeFixProvider = codeFixProviders[id]
                                      select new CodeFixData(id, analyzer, codeFixProvider)).ToImmutableList()
            );

        return new AnalyzerData(analyzersLookup, codeFixProviders, fixable);
    }

    private Optional<Assembly> TryLoad(AnalyzerReference analyzer)
    {
        if (analyzer.FullPath is not { Length: > 1 })
            return Optional<Assembly>.None;

        try
        {
            var assembly = Assembly.LoadFrom(analyzer.FullPath);
            var roslyn = assembly.GetReferencedAssemblies().FirstOrDefault(x => x.Name == "Microsoft.CodeAnalysis");
            if (roslyn != null && roslyn.Version < MinRoslynVersion)
            {
                logger.MinRoslynVersion(analyzer.Display, ApplicationInfo.Name, MinRoslynVersion);
                return Optional<Assembly>.None;
            }
            else
            {
                return new Optional<Assembly>.Some(assembly);
            }
        }
        catch (Exception e)
        {
            logger.FailedToLoadAssembly(analyzer.Display, e.Message);
            return Optional<Assembly>.None;
        }
    }
}
