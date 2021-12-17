// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Build.Locator;

var services = new ServiceCollection();
services.AddLogging(logging => logging.AddConsole());
var provider = services.BuildServiceProvider();

MSBuildLocator.RegisterDefaults();

var cli = new CommandLineApplication(true);
cli.Description = GetVersionInfo();
cli.HelpOption("-? | -h | --help");
cli.Option("-p | --project", "Path to the project file(s)", CommandOptionType.MultipleValue);
cli.Option("-n | --dry-run", "Log changes, but do not apply", CommandOptionType.NoValue);
cli.OnExecute(async () =>
{
    if (cli.Arguments.Any(arg => arg.Value == null))
    {
        cli.ShowHelp();
        return 1;
    }

    var projects = cli.Options.FindAll(opt => opt.LongName == "project").Select(opt => opt.Value());
    using var workspace = await ActivatorUtilities.GetServiceOrCreateInstance<WorkspaceBuilder>(provider).BuildWorkspace(projects: projects);

    return 0;
});
return cli.Execute(args);


static string GetVersionInfo()
{
    return $"{typeof(Program).Namespace} v{typeof(Program).Assembly.GetName().Version}";
}
