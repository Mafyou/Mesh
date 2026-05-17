using Mesh.Mobile.Core.Models;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class MeshMessageEventArgsTests
{
    [Fact]
    public void Constructor_WithDefaults_SetsExpectedValues()
    {
        var args = new MeshMessageEventArgs(0x3C, "Bonjour");

        args.Src.ShouldBe((byte)0x3C);
        args.Text.ShouldBe("Bonjour");
        args.Channel.ShouldBe((byte)0);
        args.SentAt.ShouldBeNull();
        args.Type.ShouldBe(MeshPacketType.Msg);
    }

    [Fact]
    public void Constructor_WithExplicitValues_PreservesValues()
    {
        var sentAt = new DateTimeOffset(2025, 6, 1, 12, 30, 0, TimeSpan.Zero);
        var args = new MeshMessageEventArgs(0x01, "Salut", 2, sentAt, MeshPacketType.Ack);

        args.Src.ShouldBe((byte)0x01);
        args.Text.ShouldBe("Salut");
        args.Channel.ShouldBe((byte)2);
        args.SentAt.ShouldBe(sentAt);
        args.Type.ShouldBe(MeshPacketType.Ack);
    }
}
