namespace SDI.Enki.Migrator.Commands;

/// <summary>
/// Minimal long-form option parser (--key value). Good enough for this
/// two-command CLI; if we grow more commands we can swap in System.CommandLine.
/// </summary>
internal sealed class ArgParser
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public ArgParser(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) continue;
            var key = a[2..];
            var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--")) ? args[++i] : null;
            _options[key] = value;
        }
    }

    public bool Has(string key) => _options.ContainsKey(key);

    public string? Get(string key) => _options.TryGetValue(key, out var v) ? v : null;

    public string Require(string key) =>
        Get(key) ?? throw new ArgumentException($"--{key} is required.");

    public int GetInt(string key, int defaultValue) =>
        int.TryParse(Get(key), out var v) ? v : defaultValue;

    public string[] GetList(string key) =>
        (Get(key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
