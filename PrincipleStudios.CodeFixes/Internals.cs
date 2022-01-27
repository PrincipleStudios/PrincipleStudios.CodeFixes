// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using System.Reflection;

namespace PrincipleStudios.CodeFixes;

public static class Internals
{
    public static IEnumerable<Lazy<TExtension, TMetadata>> GetExports<TExtension, TMetadata>(this Microsoft.CodeAnalysis.Host.HostServices services)
    {
        var getExports = services.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name.EndsWith("GetExports") && m.IsGenericMethodDefinition && m.GetGenericArguments().Length == 2)
            ?.MakeGenericMethod(typeof(TExtension), typeof(TMetadata))
            ?? throw new NotSupportedException("Failed to retrieve exports from host services. Plase report the issue.");

        var exports = getExports.Invoke(services, null);

        return (IEnumerable<Lazy<TExtension, TMetadata>>)exports!;
    }
}