using Detent.Core.Capture;

namespace Detent.Core.Tests;

public sealed class TextNormaliserTests
{
    // Escapes, never literals. The two spellings are visually identical, so a
    // source file normalised in transit by an editor or a diff tool would
    // silently void every test below.
    private const string Decomposed = "café";  // e + combining acute, 5 chars
    private const string Composed = "café";     // precomposed e-acute, 4 chars

    /// <summary>
    /// The canary for <c>docs/adr/0006-globalization.md</c>.
    /// </summary>
    /// <remarks>
    /// Under InvariantGlobalization, String.Normalize silently returns its input
    /// instead of throwing. If anyone re-enables that flag, this test is what
    /// tells them, rather than a mysterious description finding months later.
    /// </remarks>
    [Fact]
    public void Storage_form_composes_decomposed_characters()
    {
        // Guard the fixture before trusting it. If an editor normalised this
        // file, both constants would collapse to the same four characters and
        // the assertions below would pass without testing anything.
        Assert.Equal(5, Decomposed.Length);
        Assert.Equal(4, Composed.Length);

        var result = TextNormaliser.ForStorage(Decomposed);

        Assert.Equal(Composed, result);
        Assert.Equal(4, result.Length);
    }

    [Fact]
    public void Storage_form_preserves_line_structure()
    {
        const string markdown = "First line.\n\n- bullet\n- bullet";

        Assert.Equal(markdown, TextNormaliser.ForStorage(markdown));
    }

    [Fact]
    public void Comparison_form_collapses_whitespace_runs()
    {
        Assert.Equal("a b c", TextNormaliser.ForComparison("a   b \t\n c"));
    }

    [Fact]
    public void Comparison_form_trims_both_ends()
    {
        Assert.Equal("value", TextNormaliser.ForComparison("  \n value \t "));
    }

    /// <summary>
    /// The property that makes a reflowed description a non-event.
    /// </summary>
    [Theory]
    [InlineData("Search the catalogue.", "Search  the\n  catalogue.")]
    [InlineData("one two", "\tone\r\ntwo\n")]
    public void Comparison_form_is_stable_across_reflowing(string a, string b)
    {
        Assert.Equal(TextNormaliser.ForComparison(a), TextNormaliser.ForComparison(b));
    }

    [Fact]
    public void Comparison_form_normalises_unicode_before_comparing()
    {
        Assert.Equal(
            TextNormaliser.ForComparison(Composed + " menu"),
            TextNormaliser.ForComparison(Decomposed + "   menu"));
    }

    [Fact]
    public void Comparison_form_is_idempotent()
    {
        const string messy = "  Ambiguous \t spacing\n\nand accents. ";

        var once = TextNormaliser.ForComparison(messy);
        Assert.Equal(once, TextNormaliser.ForComparison(once));
    }

    [Fact]
    public void Whitespace_only_input_collapses_to_empty()
    {
        Assert.Equal(string.Empty, TextNormaliser.ForComparison(" \t\n "));
    }

    /// <summary>
    /// Astral-plane characters are two UTF-16 code units. Iterating chars rather
    /// than runes would split them and emit replacement characters.
    /// </summary>
    [Fact]
    public void Surrogate_pairs_survive_comparison_form()
    {
        var result = TextNormaliser.ForComparison("ship  \U0001F6A2  it");

        Assert.Equal("ship \U0001F6A2 it", result);
        Assert.DoesNotContain('�', result);
    }
}
