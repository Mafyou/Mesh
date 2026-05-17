using Mesh.Mobile.Core.Models;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class NodeContactTests
{
    [Fact]
    public void Label_BroadcastId_ReturnsTous()
    {
        var node = new NodeContact { Id = 0xFF };

        node.Label.ShouldBe("Tous");
    }

    [Fact]
    public void Label_UnicastId_ReturnsHexString()
    {
        var node = new NodeContact { Id = 0x3C };

        node.Label.ShouldBe("0x3C");
    }

    [Fact]
    public void Label_ZeroId_ReturnsHexString()
    {
        var node = new NodeContact { Id = 0x00 };

        node.Label.ShouldBe("0x00");
    }

    [Fact]
    public void IsSelected_DefaultValue_IsFalse()
    {
        var node = new NodeContact { Id = 0x01 };

        node.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void IsSelected_SetToTrue_FiresPropertyChanged()
    {
        var node = new NodeContact { Id = 0x01 };
        string? changedProperty = null;
        node.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        node.IsSelected = true;

        changedProperty.ShouldBe(nameof(NodeContact.IsSelected));
        node.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void IsSelected_SetToSameValue_DoesNotFirePropertyChanged()
    {
        var node = new NodeContact { Id = 0x01 };
        var eventCount = 0;
        node.PropertyChanged += (_, _) => eventCount++;

        node.IsSelected = false; // already false

        eventCount.ShouldBe(0);
    }

    [Fact]
    public void IsSelected_ToggleBackToFalse_FiresPropertyChanged()
    {
        var node = new NodeContact { Id = 0x01 };
        node.IsSelected = true;
        var events = new List<string?>();
        node.PropertyChanged += (_, e) => events.Add(e.PropertyName);

        node.IsSelected = false;

        events.ShouldHaveSingleItem();
        events[0].ShouldBe(nameof(NodeContact.IsSelected));
        node.IsSelected.ShouldBeFalse();
    }
}
