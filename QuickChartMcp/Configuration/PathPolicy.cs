using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace QuickChartMcp.Configuration;

/// <summary>
/// Compiled, ready-to-use view of the output-path allow-list from <see cref="QuickChartOptions"/>.
/// The regexes are compiled once (at construction) rather than on every write. Registered as a
/// singleton. Adapted from the Crawl4AiMcp path policy.
/// </summary>
public sealed class PathPolicy
{
    private readonly Regex[] _outputPatterns;

    // Windows paths are case-insensitive, so the allow-list matches case-insensitively too.
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public PathPolicy(IOptions<QuickChartOptions> options)
        : this(options.Value)
    {
    }

    public PathPolicy(QuickChartOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _outputPatterns = Compile(options.AllowedOutputPatterns);
    }

    /// <summary>True when at least one output pattern is configured.</summary>
    public bool HasOutputPatterns => _outputPatterns.Length > 0;

    /// <summary>
    /// Returns true when <paramref name="directory"/> matches any configured output pattern.
    /// An empty pattern list always returns false (deny-all).
    /// </summary>
    public bool IsOutputAllowed(string directory)
    {
        foreach (var pattern in _outputPatterns)
        {
            if (pattern.IsMatch(directory))
            {
                return true;
            }
        }

        return false;
    }

    private static Regex[] Compile(IReadOnlyList<string> patterns)
    {
        var compiled = new Regex[patterns.Count];
        for (var i = 0; i < patterns.Count; i++)
        {
            // Throws for an invalid pattern; surfaced at startup via options validation so
            // misconfiguration fails fast and loudly rather than at first tool call.
            compiled[i] = new Regex(patterns[i], Options);
        }

        return compiled;
    }
}
