using System.Globalization;
using Detent.Core.Policy;

namespace Detent.Core.Contracts;

/// <summary>
/// Parses <c>.detent/contract.yaml</c> text into <see cref="Contract"/>.
/// </summary>
/// <remarks>
/// Takes a string of already-loaded text, never a file path - actual file
/// reading happens at the CLI boundary, the same split <c>SnapshotReader</c>
/// keeps between parsing bytes and reading a file. The YAML shape (nested
/// <c>requires.tools</c>) is the file format a contract author writes; the
/// <see cref="Contract"/> shape is flatter, because nothing downstream of
/// parsing needs the nesting. Bridging the two is this type's whole job.
/// </remarks>
public static class ContractYamlReader
{
    /// <summary>The only <c>apiVersion</c> this build understands.</summary>
    public const string SupportedApiVersion = "detent/v1";

    /// <exception cref="ContractFormatException">The text is not a contract this build can read.</exception>
    public static Contract Read(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        if (YamlParser.Parse(yaml) is not { } node)
        {
            throw new ContractFormatException("The contract is empty.");
        }

        if (node is not YamlMap root)
        {
            throw new ContractFormatException("A contract must be a mapping at the top level.");
        }

        return Map(root);
    }

    private static Contract Map(YamlMap root)
    {
        var apiVersion = GetString(root, "apiVersion");

        if (string.IsNullOrWhiteSpace(apiVersion))
        {
            throw new ContractFormatException("Contract is missing 'apiVersion'.");
        }

        if (apiVersion != SupportedApiVersion)
        {
            throw new ContractFormatException(
                $"Contract apiVersion is '{apiVersion}', and this build reads '{SupportedApiVersion}'. "
                + "Upgrade detent, or update the contract.");
        }

        var consumer = GetString(root, "consumer");

        if (string.IsNullOrWhiteSpace(consumer))
        {
            throw new ContractFormatException("Contract is missing 'consumer'.");
        }

        var toolsNode = GetMap(root, "requires") is { } requires ? GetList(requires, "tools") : null;

        return new Contract
        {
            ApiVersion = apiVersion,
            Consumer = consumer,
            Provider = MapProvider(GetMap(root, "provider")),
            Tools = (toolsNode?.Items ?? []).Select(MapTool).ToList(),
            Policy = MapPolicy(GetMap(root, "policy")),
        };
    }

    private static ContractProvider? MapProvider(YamlMap? provider)
    {
        if (provider is null)
        {
            return null;
        }

        var transport = GetString(provider, "transport");
        var url = GetString(provider, "url");

        if (string.IsNullOrWhiteSpace(transport) || string.IsNullOrWhiteSpace(url))
        {
            throw new ContractFormatException("Contract 'provider' needs both 'transport' and 'url'.");
        }

        return new ContractProvider { Transport = transport, Url = url };
    }

    private static ToolRequirement MapTool(YamlNode node)
    {
        var tool = AsMap(node, "a tool under 'requires.tools'");
        var name = GetString(tool, "name");

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ContractFormatException("A tool under 'requires.tools' has no 'name'.");
        }

        var assumes = GetMap(tool, "assumes");

        return new ToolRequirement
        {
            Name = name,
            Sends = ToSet(GetList(tool, "sends")),
            Reads = ToSet(GetList(tool, "reads")),
            ExhaustiveEnums = ToSet(GetList(tool, "exhaustiveEnums")),
            Assumes = assumes is null
                ? null
                : new ToolAssumptions
                {
                    ReadOnlyHint = GetBool(assumes, "readOnlyHint"),
                    DestructiveHint = GetBool(assumes, "destructiveHint"),
                    IdempotentHint = GetBool(assumes, "idempotentHint"),
                    OpenWorldHint = GetBool(assumes, "openWorldHint"),
                },
        };
    }

    private static ContractPolicy? MapPolicy(YamlMap? policy)
    {
        if (policy is null)
        {
            return null;
        }

        var failOn = GetList(policy, "failOn");
        var warnOn = GetList(policy, "warnOn");
        var ignore = GetList(policy, "ignore");

        return new ContractPolicy
        {
            FailOn = failOn is null ? null : ToSeverities(failOn, "failOn"),
            WarnOn = warnOn is null ? null : ToSeverities(warnOn, "warnOn"),
            Ignore = (ignore?.Items ?? []).Select(MapSuppression).ToList(),
        };
    }

    private static Suppression MapSuppression(YamlNode node)
    {
        var entry = AsMap(node, "an entry under 'policy.ignore'");
        var tool = GetString(entry, "tool");

        if (string.IsNullOrWhiteSpace(tool))
        {
            throw new ContractFormatException("An entry under 'policy.ignore' has no 'tool'.");
        }

        var reason = GetString(entry, "reason");

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ContractFormatException(
                $"The suppression for '{tool}' has no 'reason'. "
                + "A suppression nobody can explain is one nobody will ever remove.");
        }

        var expiresText = GetString(entry, "expires");

        if (expiresText is null
            || !DateOnly.TryParseExact(expiresText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires))
        {
            throw new ContractFormatException(
                $"The suppression for '{tool}' has an invalid or missing 'expires' date "
                + $"'{expiresText}'. Use yyyy-MM-dd.");
        }

        return new Suppression { Tool = tool, Reason = reason, Expires = expires };
    }

    private static HashSet<string> ToSet(YamlList? list)
        => list is null ? [] : new HashSet<string>(list.Items.Select(i => AsScalar(i, "a list entry")), StringComparer.Ordinal);

    private static HashSet<Severity> ToSeverities(YamlList list, string field)
    {
        var severities = new HashSet<Severity>();

        foreach (var item in list.Items)
        {
            var name = AsScalar(item, $"policy.{field}");

            if (!Enum.TryParse<Severity>(name, ignoreCase: true, out var severity))
            {
                throw new ContractFormatException($"Unknown severity '{name}' in policy.{field}.");
            }

            severities.Add(severity);
        }

        return severities;
    }

    private static string? GetString(YamlMap map, string key)
        => map[key] is { } node ? AsScalar(node, $"'{key}'") : null;

    private static bool? GetBool(YamlMap map, string key)
    {
        var value = GetString(map, key);

        return value?.ToLowerInvariant() switch
        {
            null => null,
            "true" => true,
            "false" => false,
            _ => throw new ContractFormatException($"'{key}' must be true or false, not '{value}'."),
        };
    }

    private static YamlMap? GetMap(YamlMap map, string key)
        => map[key] switch
        {
            null => null,
            YamlMap child => child,
            _ => throw new ContractFormatException($"'{key}' must be a mapping."),
        };

    private static YamlList? GetList(YamlMap map, string key)
        => map[key] switch
        {
            null => null,
            YamlList child => child,
            _ => throw new ContractFormatException($"'{key}' must be a list."),
        };

    private static YamlMap AsMap(YamlNode node, string what)
        => node as YamlMap ?? throw new ContractFormatException($"{what} must be a mapping.");

    private static string AsScalar(YamlNode node, string what)
        => node is YamlScalar scalar ? scalar.Value : throw new ContractFormatException($"{what} must be a plain value, not a list or mapping.");
}

/// <summary>A contract file that cannot be read as one.</summary>
public sealed class ContractFormatException : Exception
{
    public ContractFormatException()
    {
    }

    public ContractFormatException(string message)
        : base(message)
    {
    }

    public ContractFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
