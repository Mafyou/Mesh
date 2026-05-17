namespace Mesh.Tests;

public class MeshProtocolTests
{
    [Fact]
    public void EncodeWrite_BroadcastWithText_ProducesCorrectPacket()
    {
        var packet = MeshProtocol.EncodeWrite(0xFF, "hello");

        packet.Length.ShouldBe(6);
        packet[0].ShouldBe((byte)0xFF);
        Encoding.UTF8.GetString(packet, 1, packet.Length - 1).ShouldBe("hello");
    }

    [Fact]
    public void EncodeWrite_UnicastWithText_SetsDstByte()
    {
        var packet = MeshProtocol.EncodeWrite(0x3C, "ping");

        packet[0].ShouldBe((byte)0x3C);
        Encoding.UTF8.GetString(packet, 1, packet.Length - 1).ShouldBe("ping");
    }

    [Fact]
    public void EncodeWrite_EmptyText_ProducesSingleBytePacket()
    {
        var packet = MeshProtocol.EncodeWrite(0x20, string.Empty);

        packet.Length.ShouldBe(1);
        packet[0].ShouldBe((byte)0x20);
    }

    [Fact]
    public void EncodeWrite_Utf8Text_EncodesCorrectly()
    {
        var packet = MeshProtocol.EncodeWrite(0xFF, "café");

        var text = Encoding.UTF8.GetString(packet, 1, packet.Length - 1);
        text.ShouldBe("café");
    }

    [Fact]
    public void DecodeNotify_ValidData_ReturnsMeshPacket()
    {
        var data = new byte[] { 0x3C }.Concat(Encoding.UTF8.GetBytes("world")).ToArray();

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Src.ShouldBe((byte)0x3C);
        result!.Value.Text.ShouldBe("world");
        result!.Value.Dst.ShouldBe(MeshProtocol.Broadcast);
    }

    [Fact]
    public void DecodeNotify_OnlySrcByte_ReturnsEmptyText()
    {
        var data = new byte[] { 0x20 };

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Src.ShouldBe((byte)0x20);
        result!.Value.Text.ShouldBe(string.Empty);
    }

    [Fact]
    public void DecodeNotify_EmptySpan_ReturnsNull()
    {
        var result = MeshProtocol.DecodeNotify(ReadOnlySpan<byte>.Empty);

        result.ShouldBeNull();
    }

    [Fact]
    public void EncodeDecodeRoundtrip_PreservesData()
    {
        const byte dst = 0x3C;
        const string text = "round-trip éàü";

        var encoded = MeshProtocol.EncodeWrite(dst, text);
        var decoded = MeshProtocol.DecodeNotify(encoded);

        decoded.ShouldNotBeNull();
        decoded!.Value.Src.ShouldBe(dst);
        decoded!.Value.Text.ShouldBe(text);
    }

    [Fact]
    public void Broadcast_ConstantIsCorrectValue()
    {
        MeshProtocol.Broadcast.ShouldBe((byte)0xFF);
    }
}
