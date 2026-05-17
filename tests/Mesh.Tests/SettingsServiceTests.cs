using Mesh.Mobile.Core.Services;

namespace Mesh.Tests;

public class SettingsServiceTests
{
    [Fact]
    public void FormatOutgoing_WithAlias_PrependsAlias()
    {
        var formatted = SettingsService.FormatOutgoing("Alice", "Bonjour");

        formatted.ShouldBe("Alice: Bonjour");
    }

    [Fact]
    public void FormatOutgoing_EmptyAlias_ReturnsOriginalText()
    {
        var formatted = SettingsService.FormatOutgoing(string.Empty, "Salut");

        formatted.ShouldBe("Salut");
    }

    [Fact]
    public void FormatOutgoing_WhitespaceOnlyAlias_ReturnsOriginalText()
    {
        var formatted = SettingsService.FormatOutgoing("   ", "Message");

        formatted.ShouldBe("Message");
    }

    [Theory]
    [InlineData("Bob", "Hello", "Bob: Hello")]
    [InlineData("", "Hello", "Hello")]
    [InlineData("  ", "Hello", "Hello")]
    public void FormatOutgoing_VariousCases_ReturnsExpected(string alias, string text, string expected)
    {
        SettingsService.FormatOutgoing(alias, text).ShouldBe(expected);
    }

    [Fact]
    public void ParsePreferredNodeIds_EmptyString_ReturnsEmpty()
    {
        var result = SettingsService.ParsePreferredNodeIds(string.Empty);

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParsePreferredNodeIds_WhitespaceOnly_ReturnsEmpty()
    {
        var result = SettingsService.ParsePreferredNodeIds("   ");

        result.ShouldBeEmpty();
    }

    [Fact]
    public void ParsePreferredNodeIds_SingleEntry_ReturnsSingleItem()
    {
        var result = SettingsService.ParsePreferredNodeIds("ab");

        result.ShouldHaveSingleItem();
        result[0].ShouldBe("AB");
    }

    [Fact]
    public void ParsePreferredNodeIds_NormalizesAndDeduplicates()
    {
        var result = SettingsService.ParsePreferredNodeIds(" ab,CD,ab , ef ");

        result.ShouldBe(["AB", "CD", "EF"]);
    }

    [Fact]
    public void SerializePreferredNodeIds_EmptyCollection_ReturnsEmptyString()
    {
        var result = SettingsService.SerializePreferredNodeIds([]);

        result.ShouldBe(string.Empty);
    }

    [Fact]
    public void SerializePreferredNodeIds_NormalizesAndDeduplicates()
    {
        var result = SettingsService.SerializePreferredNodeIds(["ab", " CD ", "ab", "ef"]);

        result.ShouldBe("AB,CD,EF");
    }

    [Fact]
    public void SerializePreferredNodeIds_WhitespaceEntries_AreSkipped()
    {
        var result = SettingsService.SerializePreferredNodeIds(["AA", "  ", "BB"]);

        result.ShouldBe("AA,BB");
    }

    [Fact]
    public void ParseThenSerialize_Roundtrip_IsStable()
    {
        const string raw = "AA,BB,CC";

        var parsed = SettingsService.ParsePreferredNodeIds(raw);
        var serialized = SettingsService.SerializePreferredNodeIds(parsed);

        serialized.ShouldBe(raw);
    }
}
