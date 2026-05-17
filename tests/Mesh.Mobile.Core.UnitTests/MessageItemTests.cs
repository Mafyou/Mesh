using Mesh.Mobile.Core.Models;
using Shouldly;

namespace Mesh.Mobile.Core.UnitTests;

public class MessageItemTests
{
    [Fact]
    public void IsLocal_SrcZero_ReturnsTrue()
    {
        var item = new MessageItem(0x00, "hello");

        item.IsLocal.ShouldBeTrue();
        item.IsRemote.ShouldBeFalse();
    }

    [Fact]
    public void IsLocal_NonZeroSrc_ReturnsFalse()
    {
        var item = new MessageItem(0x3C, "hello");

        item.IsLocal.ShouldBeFalse();
        item.IsRemote.ShouldBeTrue();
    }

    [Fact]
    public void SrcLabel_LocalMessage_ReturnsMoi()
    {
        var item = new MessageItem(0x00, "test");

        item.SrcLabel.ShouldBe("Moi");
    }

    [Fact]
    public void SrcLabel_RemoteMessage_ReturnsHexString()
    {
        var item = new MessageItem(0x3C, "test");

        item.SrcLabel.ShouldBe("0x3C");
    }

    [Fact]
    public void SrcLabel_BroadcastSrc_ReturnsHexFF()
    {
        var item = new MessageItem(0xFF, "broadcast");

        item.SrcLabel.ShouldBe("0xFF");
    }

    [Fact]
    public void TimeLabel_UsesHHmmFormat()
    {
        var at = new DateTimeOffset(2025, 1, 15, 9, 5, 0, TimeSpan.Zero);
        var item = new MessageItem(0x00, "test", at.ToLocalTime());

        item.TimeLabel.ShouldMatch(@"^\d{2}:\d{2}$");
    }

    [Fact]
    public void Constructor_WithoutAt_SetsAtToApproximatelyNow()
    {
        var before = DateTimeOffset.Now;
        var item = new MessageItem(0x00, "hello");
        var after = DateTimeOffset.Now;

        item.At.ShouldBeGreaterThanOrEqualTo(before);
        item.At.ShouldBeLessThanOrEqualTo(after);
    }

    [Fact]
    public void Constructor_WithAt_SetsAtExactly()
    {
        var at = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);
        var item = new MessageItem(0x3C, "msg", at);

        item.At.ShouldBe(at);
        item.Src.ShouldBe((byte)0x3C);
        item.Text.ShouldBe("msg");
    }

    [Fact]
    public void RecordEquality_SameValues_AreEqual()
    {
        var at = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var a = new MessageItem(0x01, "hi", at);
        var b = new MessageItem(0x01, "hi", at);

        a.ShouldBe(b);
    }

    [Fact]
    public void RecordEquality_DifferentSrc_AreNotEqual()
    {
        var at = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var a = new MessageItem(0x01, "hi", at);
        var b = new MessageItem(0x02, "hi", at);

        a.ShouldNotBe(b);
    }
}
