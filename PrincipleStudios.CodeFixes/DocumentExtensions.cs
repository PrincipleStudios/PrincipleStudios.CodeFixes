// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PrincipleStudios.CodeFixes;

public static class DocumentExtensions
{
    public static async Task<Document> RecreateDocumentAsync(this Document document, CancellationToken cancellationToken)
    {
        var newText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        newText = newText.WithChanges(new TextChange(new TextSpan(0, 0), " "));
        newText = newText.WithChanges(new TextChange(new TextSpan(0, 1), string.Empty));
        return document.WithText(newText);
    }
}