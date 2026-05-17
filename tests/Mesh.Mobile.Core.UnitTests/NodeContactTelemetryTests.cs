using Mesh.Mobile.Core.Models;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class NodeContactTelemetryTests
{
    [Fact]
    public void ApplyPingPayload_WithFullPayload_UpdatesTelemetryAndNotifications()
    {
        var node = new NodeContact { Id = 0x3C };
        var changed = new List<string?>();
        node.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        node.ApplyPingPayload([0x10, 0x27, 0x00, 0x00, 0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A]);

        node.UptimeSeconds.ShouldBe((uint)0x00002710);
        node.VbatMv.ShouldBe((ushort)0x1234);
        node.TxPackets.ShouldBe((ushort)0x5678);
        node.RxPackets.ShouldBe((ushort)0x9ABC);
        node.HasTelemetry.ShouldBeTrue();
        node.BatteryLabel.ShouldBe("4,7 V");
        node.UptimeLabel.ShouldBe("2h46");
        changed.ShouldContain(nameof(NodeContact.UptimeSeconds));
        changed.ShouldContain(nameof(NodeContact.UptimeLabel));
        changed.ShouldContain(nameof(NodeContact.VbatMv));
        changed.ShouldContain(nameof(NodeContact.BatteryLabel));
        changed.ShouldContain(nameof(NodeContact.HasTelemetry));
        changed.ShouldContain(nameof(NodeContact.TxPackets));
        changed.ShouldContain(nameof(NodeContact.RxPackets));
    }

    [Fact]
    public void ApplyPingPayload_WithShortPayload_DoesNothing()
    {
        var node = new NodeContact { Id = 0x3C, VbatMv = 1234, UptimeSeconds = 42 };

        node.ApplyPingPayload([1, 2, 3]);

        node.VbatMv.ShouldBe((ushort)1234);
        node.UptimeSeconds.ShouldBe((uint)42);
    }

    [Fact]
    public void RssiSnrAndNeighborFlags_UpdateComputedSignalLabel()
    {
        var node = new NodeContact { Id = 0x3C };

        node.Rssi = -70;
        node.Snr = 12;
        node.IsDirectNeighbor = true;

        node.SignalLabel.ShouldBe("-70 dBm / SNR 12");
    }
}
