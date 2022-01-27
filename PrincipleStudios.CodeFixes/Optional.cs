// Automatically runs Roslyn analyzers on a project
// Based on code from:
// - https://github.com/Vannevelj/RoslynTester/blob/master/RoslynTester/RoslynTester/Helpers/CodeFixVerifier.cs#L109
// - https://github.com/kzu/AutoCodeFix

public abstract record Optional<T>
{
    private Optional() { }

    public static readonly Optional<T> None = new Optional<T>.NoValue();

    private record NoValue() : Optional<T>;
    public record Some(T Value) : Optional<T>;
}