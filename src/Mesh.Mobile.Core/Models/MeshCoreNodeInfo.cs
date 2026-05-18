namespace Mesh.Mobile.Core.Models;

public sealed record MeshCoreNodeInfo(
    string DeviceName,
    byte[] PublicKey,
    float FrequencyMhz,
    float BandwidthKhz,
    byte SpreadingFactor,
    byte CodingRate,
    sbyte TxPower,
    sbyte MaxTxPower,
    float Latitude,
    float Longitude
)
{
    public string PublicKeyHex => Convert.ToHexString(PublicKey);

    public bool HasRadioInfo => FrequencyMhz > 0;

    public string RadioSummary => HasRadioInfo
        ? $"{FrequencyMhz:F3} MHz  BW{BandwidthKhz:F0}  SF{SpreadingFactor}  CR4/{CodingRate}  {TxPower} dBm"
        : string.Empty;
}
