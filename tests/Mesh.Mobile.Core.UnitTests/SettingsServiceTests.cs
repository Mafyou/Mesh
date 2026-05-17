using Mesh.Mobile.Core.Services;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class SettingsServiceTests
{
    [Fact]
    public void FormatOutgoing_WithAlias_PrependsAlias()
    {
        var formatted = SettingsService.FormatOutgoing("Alice", "Bonjour");

        formatted.ShouldBe("Alice: Bonjour");
    }

    [Fact]
    public void FormatOutgoing_WithoutAlias_ReturnsOriginalText()
    {
        var formatted = SettingsService.FormatOutgoing(string.Empty, "Salut");

        formatted.ShouldBe("Salut");
    }

    [Fact]
    public void ParsePreferredNodeIds_NormalizesAndDeduplicates()
    {
        var nodeIds = SettingsService.ParsePreferredNodeIds(" ab,CD,ab , ef ");

        nodeIds.ShouldBe(["AB", "CD", "EF"]);
    }

    [Fact]
    public void SerializePreferredNodeIds_NormalizesAndDeduplicates()
    {
        var serialized = SettingsService.SerializePreferredNodeIds(["ab", " CD ", "ab", "ef"]);

        serialized.ShouldBe("AB,CD,EF");
    }

    [Fact]
    public void FormatOutgoing_WhitespaceOnlyAlias_ReturnsOriginalText()
    {
        var formatted = SettingsService.FormatOutgoing("   ", "Message");

        formatted.ShouldBe("Message");
    }

    [Fact]
    public void ParsePreferredNodeIds_EmptyString_ReturnsEmpty()
    {
        var nodeIds = SettingsService.ParsePreferredNodeIds(string.Empty);

        nodeIds.ShouldBeEmpty();
    }

    [Fact]
    public void ParsePreferredNodeIds_WhitespaceOnly_ReturnsEmpty()
    {
        var nodeIds = SettingsService.ParsePreferredNodeIds("   ");

        nodeIds.ShouldBeEmpty();
    }

    [Fact]
    public void SerializePreferredNodeIds_EmptyCollection_ReturnsEmptyString()
    {
        var serialized = SettingsService.SerializePreferredNodeIds([]);

        serialized.ShouldBe(string.Empty);
    }

    [Fact]
    public void SerializePreferredNodeIds_WhitespaceEntries_AreSkipped()
    {
        var serialized = SettingsService.SerializePreferredNodeIds(["AA", "  ", "BB"]);

        serialized.ShouldBe("AA,BB");
    }

    [Fact]
    public void ParsePreferredNodeIds_SingleEntry_ReturnsSingleItem()
    {
        var nodeIds = SettingsService.ParsePreferredNodeIds("ab");

        nodeIds.ShouldHaveSingleItem();
        nodeIds[0].ShouldBe("AB");
    }
}
