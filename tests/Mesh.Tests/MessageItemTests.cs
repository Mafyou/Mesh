using Mesh.Mobile.Core.Models;

namespace Mesh.Tests;

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

    [Theory]
    [InlineData(0x3C, "0x3C")]
    [InlineData(0xFF, "0xFF")]
    [InlineData(0x01, "0x01")]
    public void SrcLabel_RemoteMessage_ReturnsHexString(byte src, string expected)
    {
        var item = new MessageItem(src, "test");

        item.SrcLabel.ShouldBe(expected);
    }

    [Fact]
    public void TimeLabel_UsesHHmmFormat()
    {
        var at = new DateTimeOffset(2025, 1, 15, 9, 5, 0, TimeSpan.Zero);
        var item = new MessageItem(0x00, "test", at.ToLocalTime());

        item.TimeLabel.ShouldMatch(@"^\d{2}:\d{2}$");
    }

    [Fact]
    public void TimeLabel_MidnightTime_FormatsCorrectly()
    {
        var at = new DateTimeOffset(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var item = new MessageItem(0x00, "msg", at);

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
    public void Constructor_WithAt_SetsAllProperties()
    {
        var at = new DateTimeOffset(2024, 6, 15, 14, 30, 0, TimeSpan.Zero);

        var item = new MessageItem(0x3C, "payload", at);

        item.Src.ShouldBe((byte)0x3C);
        item.Text.ShouldBe("payload");
        item.At.ShouldBe(at);
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
    public void RecordEquality_DifferentText_AreNotEqual()
    {
        var at = new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var a = new MessageItem(0x01, "hi", at);
        var b = new MessageItem(0x01, "bye", at);

        a.ShouldNotBe(b);
    }
}
