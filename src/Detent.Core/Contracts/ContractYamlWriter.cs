using System.Text;

namespace Detent.Core.Contracts;

/// <summary>
/// Renders a <see cref="Contract"/> as the YAML <see cref="ContractYamlReader"/>
/// reads back, for <c>detent init</c>.
/// </summary>
/// <remarks>
/// Writing is simpler than reading and does not go through
/// <see cref="YamlParser"/>'s grammar at all: there is no ambiguity to resolve
/// when producing text, only when consuming someone else's. Comments are not
/// an afterthought - this file exists specifically so a scaffolded contract
/// tells the person editing it what to do next, which is the entire reason
/// ADR-0002 chose a hand-editable format over JSON in the first place.
/// </remarks>
public static class ContractYamlWriter
{
    public static string Write(Contract contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var b = new StringBuilder();

        b.Append("apiVersion: ").Append(contract.ApiVersion).Append('\n');
        b.Append("consumer: ").Append(Scalar(contract.Consumer)).Append('\n');

        if (contract.Provider is { } provider)
        {
            b.Append('\n')
                .Append("provider:\n")
                .Append("  transport: ").Append(provider.Transport).Append('\n')
                .Append("  url: ").Append(provider.Url).Append('\n');
        }

        b.Append('\n').Append("requires:\n");

        if (contract.Tools.Count == 0)
        {
            b.Append("  tools: [] # detent init found no tools to scaffold.\n");
        }
        else
        {
            b.Append("  tools:\n");

            foreach (var tool in contract.Tools)
            {
                WriteTool(b, tool);
            }
        }

        b.Append('\n')
            .Append("# Narrow sends/reads to what this consumer actually uses - a property\n")
            .Append("# absent from either list will not fail your build when the server\n")
            .Append("# changes it. Add exhaustiveEnums for any output enum your code switches\n")
            .Append("# on exhaustively, and assumes for any safety hint you rely on without\n")
            .Append("# checking (see docs/arch/diff-rules.md §8 and §12).\n")
            .Append("#\n")
            .Append("# policy:\n")
            .Append("#   failOn: [breaking, security]\n")
            .Append("#   warnOn: [behavioural]\n")
            .Append("#   ignore:\n")
            .Append("#     - tool: some_tool\n")
            .Append("#       reason: \"why this is safe to ignore for now\"\n")
            .Append("#       expires: 2027-01-01\n");

        return b.ToString();
    }

    private static void WriteTool(StringBuilder b, ToolRequirement tool)
    {
        b.Append("    - name: ").Append(tool.Name).Append('\n');
        WriteList(b, "sends", tool.Sends);
        WriteList(b, "reads", tool.Reads);
    }

    private static void WriteList(StringBuilder b, string key, IReadOnlySet<string> values)
    {
        if (values.Count == 0)
        {
            b.Append("      ").Append(key).Append(": []\n");
            return;
        }

        b.Append("      ").Append(key).Append(": [")
            .Append(string.Join(", ", values.Order(StringComparer.Ordinal)))
            .Append("]\n");
    }

    /// <summary>
    /// Quotes a scalar only when it needs it - a bare <c>brand-site-agent</c>
    /// is more pleasant to read and re-edit than <c>"brand-site-agent"</c>,
    /// and YAML does not require the quotes unless the value could otherwise
    /// be misread as something else.
    /// </summary>
    private static string Scalar(string value)
        => value.Length == 0 || value.Any(c => c is ':' or '#' or '[' or ']' or '"' or '\'') || value != value.Trim()
            ? "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\""
            : value;
}
