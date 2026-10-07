// ============================================================
//  PC-Lock Server v1.1 (تشخيصي — يطبع سبب المشاكل)
// ============================================================

using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

const string SERVICE_UUID = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";
const string CHAR_UUID    = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "PC-Lock Server";

Console.WriteLine("============================================");
Console.WriteLine("        PC-Lock Server v1.1 (diag)");
Console.WriteLine("    Remote PC lock via Bluetooth (BLE)");
Console.WriteLine("============================================\n");

// 1) فحص الأدابتر
var adapter = await BluetoothAdapter.GetDefaultAsync();
if (adapter is null)
{
    Console.WriteLine("[ERROR] No Bluetooth adapter found.");
    Console.ReadKey();
    return;
}
Console.WriteLine("[OK] Bluetooth adapter found");

// 2) فحص: هل البلوتوث مفعّل؟
var radios = await Radio.GetRadiosAsync();
var btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
if (btRadio is null)
{
    Console.WriteLine("[WARN] No Bluetooth radio detected (unusual).");
}
else
{
    Console.WriteLine($"[INFO] Bluetooth radio state : {btRadio.State}");
    if (btRadio.State != RadioState.On)
    {
        Console.WriteLine("[PROBLEM] Bluetooth is OFF!");
        Console.WriteLine(">> Turn it ON: Settings > Bluetooth & devices, then run again.");
        Console.ReadKey();
        return;
    }
}

// 3) فحص: هل العتاد يدعم وضع الخادم؟
Console.WriteLine($"[INFO] LE Peripheral mode     : {(adapter.IsPeripheralRoleSupported ? "SUPPORTED" : "NOT SUPPORTED")}");
Console.WriteLine($"[INFO] LE Advertising         : {(adapter.IsAdvertiseSupported ? "SUPPORTED" : "NOT SUPPORTED")}");

if (!adapter.IsPeripheralRoleSupported)
{
    Console.WriteLine();
    Console.WriteLine("[PROBLEM] This Bluetooth adapter CANNOT act as a BLE Peripheral.");
    Console.WriteLine(">> Fix 1: Update the Bluetooth driver (Windows Update / Device Manager).");
    Console.WriteLine(">> Fix 2: Use a USB Bluetooth 5.0+ dongle that supports LE Peripheral mode.");
    Console.ReadKey();
    return;
}

// 4) إنشاء خدمة GATT
var providerResult = await GattServiceProvider.CreateAsync(Guid.Parse(SERVICE_UUID));
if (providerResult.Error != BluetoothError.Success)
{
    Console.WriteLine($"[ERROR] Cannot create service: {providerResult.Error}");
    Console.ReadKey();
    return;
}
var serviceProvider = providerResult.ServiceProvider;

var charParams = new GattLocalCharacteristicParameters
{
    CharacteristicProperties =
        GattCharacteristicProperties.Write |
        GattCharacteristicProperties.WriteWithoutResponse,
};

var charResult = await serviceProvider.Service.CreateCharacteristicAsync(Guid.Parse(CHAR_UUID), charParams);
if (charResult.Error != BluetoothError.Success)
{
    Console.WriteLine($"[ERROR] Cannot create characteristic: {charResult.Error}");
    Console.ReadKey();
    return;
}
var characteristic = charResult.Characteristic;

// 5) استقبال أوامر الهاتف
characteristic.WriteRequested += (sender, args) =>
{
    var deferral = args.GetDeferral();
    _ = Task.Run(async () =>
    {
        try
        {
            var request = await args.GetRequestAsync();
            if (request is null) return;

            var reader = DataReader.FromBuffer(request.Value);
            var data = new byte[reader.UnconsumedBufferLength];
            reader.ReadBytes(data);

            if (data.Length > 0)
            {
                switch (data[0])
                {
                    case 0x01: // قفل
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] LOCK command received from phone");
                        bool ok = NativeMethods.LockWorkStation();
                        Console.WriteLine(ok
                            ? "[OK] Windows is now locked"
                            : "[ERROR] Failed to lock");
                        break;

                    case 0x02: // فتح
                        Console.WriteLine($"\n[{DateTime.Now:HH:mm:ss}] UNLOCK command received");
                        Console.WriteLine(">> Use Windows Hello (fingerprint / PIN) on the PC to sign in.");
                        break;
                }
            }

            if (request.Option == GattWriteOption.WriteWithResponse)
                request.Respond();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] Write handler: {ex.Message}");
        }
        finally
        {
            deferral.Complete();
        }
    });
};

serviceProvider.AdvertisementStatusChanged += (s, e) =>
{
    if (e.Status == GattServiceProviderAdvertisementStatus.Aborted)
        Console.WriteLine("\n[ERROR] Advertising aborted unexpectedly.");
};

// 6) بدء البث ثم التحقق بعد 3 ثوانٍ
await Task.Delay(500);

serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
{
    IsDiscoverable = true,
    IsConnectable = true,
});

await Task.Delay(3000);

if (serviceProvider.AdvertisementStatus != GattServiceProviderAdvertisementStatus.Started)
{
    Console.WriteLine($"[PROBLEM] Advertising failed (status: {serviceProvider.AdvertisementStatus}).");
    Console.WriteLine(">> Try: 1) Toggle Bluetooth OFF then ON");
    Console.WriteLine(">>      2) Restart the PC");
    Console.WriteLine(">>      3) Make sure only ONE copy of this app runs");
    Console.WriteLine(">>      4) Close other Bluetooth apps, then retry.");
    Console.ReadKey();
    return;
}

Console.WriteLine("[OK] Advertising started — waiting for your phone...");
Console.WriteLine($"[OK] Service UUID: {SERVICE_UUID}");
Console.WriteLine("\nKeep this window OPEN. Press Q to quit.");
Console.WriteLine("--------------------------------------------");

while (true)
{
    var key = Console.ReadKey(intercept: true).Key;
    if (key is ConsoleKey.Q or ConsoleKey.Escape) break;
}

serviceProvider.StopAdvertising();
Console.WriteLine("\nServer stopped. Bye!");

static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();
}
