Allows source analyzers to be run on a C# project from the command line. Automatically applies all fixes.

Usage:

	principled-csharp --project <path-to-csproj>

Options:
	* `-? | -h | --help` - displays help text
	* `-p | --project` - specifies the path to a project to analyze. Does not support glob formats. Can be specified multiple times.