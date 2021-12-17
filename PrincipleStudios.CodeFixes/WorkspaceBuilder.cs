// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;

namespace PrincipleStudios.CodeFixes;

class WorkspaceBuilder
{
    private readonly ILogger<WorkspaceBuilder> logger;

    public WorkspaceBuilder(ILogger<WorkspaceBuilder> logger)
    {
        this.logger = logger;
    }

    public async Task<MSBuildWorkspace> BuildWorkspace(IEnumerable<string> projects)
    {
        var workspace = MSBuildWorkspace.Create();
        workspace.WorkspaceFailed += (sender, args) => logger.WorkspaceFailed(args.Diagnostic.Kind, args.Diagnostic.Message);
        try
        {
            await Task.WhenAll(projects.Select(project => workspace.OpenProjectAsync(project)));
    
            return workspace;
        }
        catch
        {
            workspace?.Dispose();
            throw;
        }
    }

}
