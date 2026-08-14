using System.IO;

namespace CodexTokenBar.Codex;

public sealed class CodexCommandLocator
{
    private static readonly string[] CandidateNames = ["codex.exe", "codex.cmd", "codex"];
    private readonly Func<string?> _pathProvider;
    private readonly Func<string, bool> _fileExists;

    public CodexCommandLocator()
        : this(
            () => Environment.GetEnvironmentVariable("PATH"),
            File.Exists)
    {
    }

    public CodexCommandLocator(Func<string?> pathProvider, Func<string, bool> fileExists)
    {
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    public IReadOnlyList<string> FindAll()
    {
        var directories = (_pathProvider() ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var results = new List<string>();

        foreach (var candidate in CandidateNames)
        {
            foreach (var directory in directories)
            {
                var path = Path.Combine(directory.Trim('"'), candidate);
                if (_fileExists(path))
                    results.Add(path);
            }
        }

        if (results.Count == 0)
            throw new CodexProcessException("未找到 Codex CLI，请先安装并登录 Codex");

        return results;
    }

    public string Find() => FindAll()[0];
}

public sealed class CodexProcessException : Exception
{
    public CodexProcessException(string message) : base(message)
    {
    }

    public CodexProcessException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
