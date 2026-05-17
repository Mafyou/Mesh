using System.Buffers.Binary;
using System.Text;
using Mesh.Mobile.Core;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class MeshCoreProtocolTests
{
    [Fact]
    public void EncodeAppStart_UsesCommandAndAppName()
    {
        var packet = MeshCoreProtocol.EncodeAppStart();
        var appNameBytes = Encoding.UTF8.GetBytes(MeshCoreProtocol.AppName);

        packet[0].ShouldBe((byte)0x01);
        packet.Length.ShouldBe(1 + 7 + appNameBytes.Length);
        packet[8..].ToArray().ShouldBe(appNameBytes);
    }

    [Fact]
    public void EncodeMessage_WritesCommandChannelTimestampAndText()
    {
        var before = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var packet = MeshCoreProtocol.EncodeMessage(0x02, "bonjour");
        var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        packet[0].ShouldBe((byte)0x03);
        packet[1].ShouldBe((byte)0x00);
        packet[2].ShouldBe((byte)0x02);

        var ts = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(3));
        ts.ShouldBeGreaterThanOrEqualTo((uint)before);
        ts.ShouldBeLessThanOrEqualTo((uint)after);
        Encoding.UTF8.GetString(packet, 7, packet.Length - 7).ShouldBe("bonjour");
    }

    [Fact]
    public void DecodeNotify_PktMsg_ReturnsChannelTextAndTimestamp()
    {
        var ts = (uint)new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var text = Encoding.UTF8.GetBytes("hello");
        var data = new byte[8 + text.Length];
        data[0] = MeshCoreProtocol.PKT_MSG;
        data[1] = 0x02;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ts);
        text.CopyTo(data, 8);

        var result = MeshCoreProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Channel.ShouldBe((byte)0x02);
        result.Value.Text.ShouldBe("hello");
        result.Value.SentAt.ShouldBe(new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DecodeNotify_PktMsgV3_ReturnsChannelTextAndTimestamp()
    {
        var ts = (uint)new DateTimeOffset(2025, 6, 1, 13, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var text = Encoding.UTF8.GetBytes("salut");
        var data = new byte[9 + text.Length];
        data[0] = MeshCoreProtocol.PKT_MSG_V3;
        data[1] = 0x03;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ts);
        data[8] = 0x7F;
        text.CopyTo(data, 9);

        var result = MeshCoreProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Channel.ShouldBe((byte)0x03);
        result.Value.Text.ShouldBe("salut");
        result.Value.SentAt.ShouldBe(new DateTimeOffset(2025, 6, 1, 13, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void DecodeNotify_UnknownPacket_ReturnsNull()
    {
        var result = MeshCoreProtocol.DecodeNotify(new byte[] { 0x99, 0x00, 0x00 });

        result.ShouldBeNull();
    }

    [Fact]
    public void IsMeshCoreDevice_MeshPrefixedName_ReturnsFalse()
    {
        MeshCoreProtocol.IsMeshCoreDevice("Mesh-Node").ShouldBeFalse();
    }

    [Fact]
    public void IsMeshCoreDevice_NonMeshName_ReturnsTrue()
    {
        MeshCoreProtocol.IsMeshCoreDevice("Node-123").ShouldBeTrue();
    }

    [Fact]
    public void IsMeshCoreDevice_WhitespaceOrNull_ReturnsFalse()
    {
        MeshCoreProtocol.IsMeshCoreDevice(string.Empty).ShouldBeFalse();
        MeshCoreProtocol.IsMeshCoreDevice(null).ShouldBeFalse();
    }
}
