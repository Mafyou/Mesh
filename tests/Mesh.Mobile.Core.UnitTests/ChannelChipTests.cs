using Mesh.Mobile.Core.Models;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class ChannelChipTests
{
    [Fact]
    public void Constructor_SetsIdAndName()
    {
        var chip = new ChannelChip(0x2A, "General");

        chip.Id.ShouldBe((byte)0x2A);
        chip.Name.ShouldBe("General");
        chip.IsSelected.ShouldBeFalse();
    }

    [Fact]
    public void IsSelected_SetToTrue_FiresPropertyChanged()
    {
        var chip = new ChannelChip(0x01, "Chat");
        string? changedProperty = null;
        chip.PropertyChanged += (_, e) => changedProperty = e.PropertyName;

        chip.IsSelected = true;

        changedProperty.ShouldBe(nameof(ChannelChip.IsSelected));
        chip.IsSelected.ShouldBeTrue();
    }

    [Fact]
    public void IsSelected_SetToSameValue_DoesNotFirePropertyChanged()
    {
        var chip = new ChannelChip(0x01, "Chat");
        var eventCount = 0;
        chip.PropertyChanged += (_, _) => eventCount++;

        chip.IsSelected = false;

        eventCount.ShouldBe(0);
    }
}
