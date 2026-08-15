using Detent.Core.Contracts;
using Detent.Core.Policy;

namespace Detent.Core.Tests;

public sealed class ContractYamlReaderTests
{
    private const string FullExample = """
        apiVersion: detent/v1
        consumer: brand-site-agent
        provider:
          transport: http
          url: https://mcp.example.com/mcp

        requires:
          tools:
            - name: search_products
              sends: [query, market]
              reads: [sku, name, price, market]
              exhaustiveEnums: [market]
              assumes:
                readOnlyHint: true

        policy:
          failOn: [breaking, security]
          warnOn: [behavioural]
          ignore:
            - tool: legacy_export
              reason: "Scheduled for removal, we no longer call it"
              expires: 2026-12-01
        """;

    [Fact]
    public void The_documented_shape_parses_completely()
    {
        var contract = ContractYamlReader.Read(FullExample);

        Assert.Equal("detent/v1", contract.ApiVersion);
        Assert.Equal("brand-site-agent", contract.Consumer);
        Assert.Equal("http", contract.Provider!.Transport);
        Assert.Equal("https://mcp.example.com/mcp", contract.Provider.Url);

        var tool = Assert.Single(contract.Tools);
        Assert.Equal("search_products", tool.Name);
        Assert.Equal(["market", "query"], tool.Sends.Order(StringComparer.Ordinal));
        Assert.Equal(["market", "name", "price", "sku"], tool.Reads.Order(StringComparer.Ordinal));
        Assert.Equal(["market"], tool.ExhaustiveEnums);
        Assert.True(tool.Assumes!.ReadOnlyHint);

        Assert.Equal(new HashSet<Severity> { Severity.Breaking, Severity.Security }, contract.Policy!.FailOn);
        Assert.Equal(new HashSet<Severity> { Severity.Behavioural }, contract.Policy.WarnOn);

        var suppression = Assert.Single(contract.Policy.Ignore);
        Assert.Equal("legacy_export", suppression.Tool);
        Assert.Equal(new DateOnly(2026, 12, 1), suppression.Expires);
    }

    /// <summary>
    /// YAML's default schema recognises an unquoted yyyy-mm-dd scalar as a
    /// timestamp, not a string. This pins that it still reaches our string
    /// field intact rather than being silently reinterpreted or rejected.
    /// </summary>
    [Fact]
    public void An_unquoted_date_literal_is_read_correctly()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            requires:
              tools: []
            policy:
              ignore:
                - tool: x
                  reason: r
                  expires: 2027-03-15
            """;

        var contract = ContractYamlReader.Read(yaml);

        Assert.Equal(new DateOnly(2027, 3, 15), contract.Policy!.Ignore[0].Expires);
    }

    [Fact]
    public void Minimal_contract_with_no_tools_parses()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            """;

        var contract = ContractYamlReader.Read(yaml);

        Assert.Equal("c", contract.Consumer);
        Assert.Empty(contract.Tools);
        Assert.Null(contract.Provider);
        Assert.Null(contract.Policy);
    }

    [Fact]
    public void Unknown_top_level_keys_are_ignored_rather_than_rejected()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            futureFeature: something we do not understand yet
            """;

        var contract = ContractYamlReader.Read(yaml);
        Assert.Equal("c", contract.Consumer);
    }

    [Theory]
    [InlineData("consumer: c")]
    [InlineData("apiVersion: detent/v1")]
    public void Missing_a_required_top_level_field_is_a_format_error(string yaml)
        => Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));

    [Fact]
    public void An_unsupported_api_version_is_a_format_error()
    {
        const string yaml = """
            apiVersion: detent/v99
            consumer: c
            """;

        var ex = Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
        Assert.Contains("detent/v99", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_tool_with_no_name_is_a_format_error()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - sends: [query]
            """;

        Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
    }

    [Fact]
    public void A_suppression_with_no_reason_is_a_format_error()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            policy:
              ignore:
                - tool: x
                  expires: 2027-01-01
            """;

        var ex = Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
        Assert.Contains("reason", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_malformed_expiry_date_is_a_format_error()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            policy:
              ignore:
                - tool: x
                  reason: r
                  expires: not-a-date
            """;

        Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
    }

    [Fact]
    public void An_unknown_severity_name_in_policy_is_a_format_error()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            policy:
              failOn: [catastrophic]
            """;

        Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
    }

    [Fact]
    public void Malformed_yaml_is_a_format_error_not_a_crash()
    {
        const string yaml = "apiVersion: [unterminated";
        Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
    }

    [Fact]
    public void Empty_input_is_a_format_error()
        => Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(string.Empty));

    // --- hand-rolled parser specifics -------------------------------------

    [Fact]
    public void Comments_are_stripped()
    {
        const string yaml = """
            # a leading comment
            apiVersion: detent/v1 # trailing comment
            consumer: c  # another one
            """;

        var contract = ContractYamlReader.Read(yaml);
        Assert.Equal("c", contract.Consumer);
    }

    /// <summary>
    /// A '#' inside a quoted value is data, not a comment marker - a consumer
    /// name or reason genuinely could contain one.
    /// </summary>
    [Fact]
    public void A_hash_inside_a_quoted_string_is_not_a_comment()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: "team #3"
            """;

        Assert.Equal("team #3", ContractYamlReader.Read(yaml).Consumer);
    }

    /// <summary>
    /// A colon inside a scheme like https:// must never be mistaken for the
    /// mapping key/value separator, or a provider URL could never be parsed.
    /// </summary>
    [Fact]
    public void A_url_scheme_colon_does_not_split_the_mapping_line()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            provider:
              transport: http
              url: https://mcp.example.com/mcp?x=1
            """;

        Assert.Equal("https://mcp.example.com/mcp?x=1", ContractYamlReader.Read(yaml).Provider!.Url);
    }

    [Fact]
    public void Blank_lines_between_entries_are_ignored()
    {
        const string yaml = """
            apiVersion: detent/v1

            consumer: c

            requires:
              tools:
                - name: a

                - name: b
            """;

        Assert.Equal(["a", "b"], ContractYamlReader.Read(yaml).Tools.Select(t => t.Name));
    }

    [Fact]
    public void Multiple_tools_in_a_sequence_all_parse_with_their_own_fields()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: search_products
                  sends: [query]
                - name: list_orders
                  sends: [customerId]
            """;

        var contract = ContractYamlReader.Read(yaml);

        Assert.Equal(2, contract.Tools.Count);
        Assert.Equal(["query"], contract.Tools[0].Sends);
        Assert.Equal(["customerId"], contract.Tools[1].Sends);
    }

    [Fact]
    public void An_empty_inline_list_parses_as_empty_not_an_error()
    {
        const string yaml = """
            apiVersion: detent/v1
            consumer: c
            requires:
              tools:
                - name: a
                  sends: []
            """;

        Assert.Empty(ContractYamlReader.Read(yaml).Tools[0].Sends);
    }

    [Fact]
    public void Tabs_in_indentation_are_a_format_error()
    {
        var yaml = "apiVersion: detent/v1\n\tconsumer: c";
        Assert.Throws<ContractFormatException>(() => ContractYamlReader.Read(yaml));
    }

    [Fact]
    public void Round_tripping_the_hand_written_example_from_the_project_plan_works()
    {
        // Same shape as docs/adr and the README will show consumers, kept
        // here as an integration check across every feature at once.
        const string yaml = """
            apiVersion: detent/v1
            consumer: brand-site-agent
            provider:
              transport: http
              url: https://mcp.example.com/mcp
            requires:
              tools:
                - name: search_products
                  sends: [query, market]
                  reads: [sku, name, price, market]
                  exhaustiveEnums: [market]
                  assumes:
                    readOnlyHint: true
                - name: legacy_export
                  sends: [format]
            policy:
              failOn: [breaking, security]
              warnOn: [behavioural]
              ignore:
                - tool: legacy_export
                  reason: "Scheduled for removal, we no longer call it"
                  expires: 2026-12-01
            """;

        var contract = ContractYamlReader.Read(yaml);

        Assert.Equal(2, contract.Tools.Count);
        Assert.Equal("legacy_export", contract.Tools[1].Name);
        Assert.Single(contract.Policy!.Ignore);
    }
}
