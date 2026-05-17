namespace Mesh.Mobile;

public static class BlePermissionHelper
{
    public static async Task<bool> EnsureScanPermissionsAsync()
    {
#if ANDROID
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            var scan = await Permissions.RequestAsync<BluetoothScanPermission>();
            var connect = await Permissions.RequestAsync<BluetoothConnectPermission>();
            return scan is PermissionStatus.Granted && connect is PermissionStatus.Granted;
        }
#endif

        var bluetoothStatus = await Permissions.RequestAsync<Permissions.Bluetooth>();
        var locationStatus = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        return bluetoothStatus is PermissionStatus.Granted
            && locationStatus is PermissionStatus.Granted;
    }

#if ANDROID
    // These inner types reference Android 31+ APIs; the IsAndroidVersionAtLeast guard above
    // ensures they are only instantiated on compatible devices, suppressing CA1416 here.
#pragma warning disable CA1416
    private sealed class BluetoothScanPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Android.Manifest.Permission.BluetoothScan, true)];
    }

    private sealed class BluetoothConnectPermission : Permissions.BasePlatformPermission
    {
        public override (string androidPermission, bool isRuntime)[] RequiredPermissions =>
            [(Android.Manifest.Permission.BluetoothConnect, true)];
    }
#pragma warning restore CA1416
#endif
}
