using System.Buffers.Binary;
using System.Text;
using Mesh.Mobile.Core;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class MeshProtocolEdgeCaseTests
{
    [Fact]
    public void DecodeNotify_MsgWithVersionAndNoText_ReturnsEmptyTextAndTimestamp()
    {
        var ts = (uint)new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var data = new byte[8];
        data[0] = 0x3C;
        data[1] = 0x01;
        data[2] = (byte)MeshPacketType.Msg;
        data[3] = MeshProtocol.PayloadVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), ts);

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Text.ShouldBe(string.Empty);
        result.Value.SentAt.ShouldNotBeNull();
    }

    [Fact]
    public void DecodeNotify_MsgWithRawTextWithoutVersion_ReturnsRawUtf8Text()
    {
        var data = new byte[] { 0x3C, 0x01, (byte)MeshPacketType.Msg }
            .Concat(Encoding.UTF8.GetBytes("salut")).ToArray();

        var result = MeshProtocol.DecodeNotify(data);

        result.ShouldNotBeNull();
        result!.Value.Text.ShouldBe("salut");
        result.Value.SentAt.ShouldBeNull();
    }
}
