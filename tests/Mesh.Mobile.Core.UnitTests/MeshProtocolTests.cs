using System.Buffers.Binary;
using System.Text;
using Mesh.Mobile.Core;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class MeshProtocolTests
{
    [Fact]
    public void EncodeWrite_BroadcastChannel0_HasCorrectHeader()
    {
        var packet = MeshProtocol.EncodeWrite(0xFF, 0, "hello");

        packet[0].ShouldBe((byte)0xFF);
        packet[1].ShouldBe((byte)0x00);
        packet[2].ShouldBe(MeshProtocol.PayloadVersion);
    }

    [Fact]
    public void EncodeWrite_Channel2_StoresChannelByte()
    {
        var packet = MeshProtocol.EncodeWrite(0xFF, 2, "test");

        packet[1].ShouldBe((byte)2);
    }

    [Fact]
    public void EncodeWrite_Utf8Text_EncodesTextCorrectly()
    {
        var packet = MeshProtocol.EncodeWrite(0xFF, 0, "café");

        var text = Encoding.UTF8.GetString(packet, 7, packet.Length - 7);
        text.ShouldBe("café");
    }

    [Fact]
    public void EncodeWrite_EmptyText_ProducesHeaderOnlyPacket()
    {
        var packet = MeshProtocol.EncodeWrite(0x20, 0, string.Empty);

        packet.Length.ShouldBe(7);
        packet[0].ShouldBe((byte)0x20);
    }

    [Fact]
    public void DecodeNotify_MsgFormat_ReturnsSrcChannelTypeText()
    {
        var data = new byte[] { 0x3C, 0x00, (byte)MeshPacketType.Msg }
                   .Concat(Encoding.UTF8.GetBytes("world")).ToArray();

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Src.ShouldBe((byte)0x3C);
        result.Value.Channel.ShouldBe((byte)0x00);
        result.Value.Type.ShouldBe(MeshPacketType.Msg);
        result.Value.Text.ShouldBe("world");
        result.Value.Dst.ShouldBe(MeshProtocol.Broadcast);
        result.Value.SentAt.ShouldBeNull();
    }

    [Fact]
    public void DecodeNotify_RawMsgWithoutVersion_ReturnsUtf8Text()
    {
        var data = new byte[] { 0x20, 0x01, (byte)MeshPacketType.Msg }
            .Concat(Encoding.UTF8.GetBytes("raw")).ToArray();

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Src.ShouldBe((byte)0x20);
        result.Value.Channel.ShouldBe((byte)0x01);
        result.Value.Type.ShouldBe(MeshPacketType.Msg);
        result.Value.Text.ShouldBe("raw");
        result.Value.SentAt.ShouldBeNull();
    }

    [Fact]
    public void DecodeNotify_NonMsgPacket_ReturnsEmptyText()
    {
        var data = new byte[] { 0x20, 0x01, (byte)MeshPacketType.Ack, 0xAA, 0xBB, 0xCC };

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Src.ShouldBe((byte)0x20);
        result.Value.Channel.ShouldBe((byte)0x01);
        result.Value.Type.ShouldBe(MeshPacketType.Ack);
        result.Value.Text.ShouldBe(string.Empty);
        result.Value.SentAt.ShouldBeNull();
    }

    [Fact]
    public void DecodeNotify_PingPacket_ReturnsEmptyText()
    {
        var data = new byte[] { 0x20, 0x01, (byte)MeshPacketType.Ping, 0x01, 0x02, 0x03, 0x04 };

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Type.ShouldBe(MeshPacketType.Ping);
        result.Value.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public void DecodeNotify_NeighborsPacket_ReturnsEmptyText()
    {
        var data = new byte[] { 0x20, 0x01, (byte)MeshPacketType.Neighbors, 0x01, 0x2A, unchecked((byte)-42), 0x05 };

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Type.ShouldBe(MeshPacketType.Neighbors);
        result.Value.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public void DecodeNotify_EmptySpan_ReturnsNull()
    {
        var result = MeshProtocol.DecodeNotify(ReadOnlySpan<byte>.Empty);

        result.ShouldBeNull();
    }

    [Fact]
    public void DecodeNotify_TwoBytes_ReturnsNull()
    {
        var result = MeshProtocol.DecodeNotify(new byte[] { 0x3C, 0x00 });

        result.ShouldBeNull();
    }

    [Fact]
    public void EncodeDecodeRoundtrip_PreservesTextChannelAndTimestamp()
    {
        const byte dst = 0x3C;
        const byte channel = 1;
        const string text = "round-trip éàü";

        var encoded = MeshProtocol.EncodeWrite(dst, channel, text);
        var withType = new byte[encoded.Length + 1];
        withType[0] = encoded[0];
        withType[1] = encoded[1];
        withType[2] = (byte)MeshPacketType.Msg;
        encoded[2..].CopyTo(withType.AsSpan(3));

        var decoded = MeshProtocol.DecodeNotify(withType);

        decoded.ShouldNotBeNull();
        decoded!.Value.Src.ShouldBe(dst);
        decoded.Value.Channel.ShouldBe(channel);
        decoded.Value.Type.ShouldBe(MeshPacketType.Msg);
        decoded.Value.Text.ShouldBe(text);
        decoded.Value.SentAt.ShouldNotBeNull();
    }

    [Fact]
    public void DecodeNotify_WithTimestamp_ExtractsSentAt()
    {
        var ts = (uint)new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var text = Encoding.UTF8.GetBytes("hello");
        var data = new byte[3 + 1 + 4 + text.Length];
        data[0] = 0x3C;
        data[1] = 0x02;
        data[2] = (byte)MeshPacketType.Msg;
        data[3] = MeshProtocol.PayloadVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ts);
        text.CopyTo(data, 8);

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Channel.ShouldBe((byte)2);
        result.Value.Type.ShouldBe(MeshPacketType.Msg);
        result.Value.Text.ShouldBe("hello");
        result.Value.SentAt!.Value.Year.ShouldBe(2025);
    }

    [Fact]
    public void Broadcast_ConstantIsCorrectValue()
    {
        MeshProtocol.Broadcast.ShouldBe((byte)0xFF);
    }
}
