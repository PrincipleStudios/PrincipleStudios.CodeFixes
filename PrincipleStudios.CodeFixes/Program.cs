// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Build.Locator;
using PrincipleStudios.CodeFixes;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeFixes;
using System.Diagnostics;
using System.Text;

CancellationToken cancellationToken = default;
var services = new ServiceCollection();
services.AddLogging(logging => logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Debug;
}));
var provider = services.BuildServiceProvider();

MSBuildLocator.RegisterDefaults();

var cli = new CommandLineApplication(true);
cli.Description = ApplicationInfo.VersionInfo;
cli.HelpOption("-? | -h | --help");
cli.Option("-p | --project", "Path to the project file(s)", CommandOptionType.MultipleValue);
//cli.Option("-n | --dry-run", "Log changes, but do not apply", CommandOptionType.NoValue);
cli.OnExecute(async () =>
{
    if (cli.Arguments.Any(arg => arg.Value == null))
    {
        cli.ShowHelp();
        return 1;
    }

    var projects = cli.Options.FindAll(opt => opt.LongName == "project").Select(opt => opt.Value());
    using var workspace = await ActivatorUtilities.GetServiceOrCreateInstance<WorkspaceBuilder>(provider).BuildWorkspace(projects: projects);
    
    var analyzers = ActivatorUtilities.GetServiceOrCreateInstance<AnalyzerLoader>(provider).LoadAnalyzers(workspace);

    var projectFixer = ActivatorUtilities.GetServiceOrCreateInstance<ProjectFixer>(provider);

    foreach (var project in workspace.CurrentSolution.Projects)
    {
        if (!await projectFixer.FixProject(project, workspace, analyzers, cancellationToken))
            return 1;
    }

    return 0;
});
return cli.Execute(args);
