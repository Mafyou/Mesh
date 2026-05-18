namespace Mesh.Mobile.Core.Models;

public sealed record MeshCoreDeviceInfo(
    byte FirmwareVersionNum,
    byte MaxContacts,
    byte MaxGroupChannels,
    uint BlePin,
    string BuildDate,
    string Manufacturer,
    string FirmwareVersionString,
    bool ClientRepeat,
    byte PathHashMode)
{
    public string BlePinDisplay => BlePin.ToString();
}
