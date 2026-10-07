// ============================================================
//  PC-Lock Server — شغّله على الكمبيوتر واترك النافذة مفتوحة
//  يستقبل أوامر القفل من تطبيق الهاتف عبر البلوتوث (BLE)
// ============================================================

using System.Runtime.InteropServices;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

const string SERVICE_UUID = "a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d";
const string CHAR_UUID    = "b2c3d4e5-f6a7-4b8c-9d0e-1f2a3b4c5d6e";

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.Title = "PC-Lock Server";

Console.WriteLine("============================================");
Console.WriteLine("            PC-Lock Server v1.0");
Console.WriteLine("    Remote PC lock via Bluetooth (BLE)");
Console.WriteLine("============================================\n");

// 1) التأكد من وجود البلوتوث
var adapter = await BluetoothAdapter.GetDefaultAsync();
if (adapter is null)
{
    Console.WriteLine("[ERROR] No Bluetooth adapter found. Enable Bluetooth and restart.");
    Console.ReadKey();
    return;
}
Console.WriteLine("[OK] Bluetooth adapter ready");

// 2) إنشاء خدمة GATT
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

// 3) استقبال أوامر الهاتف
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

            // نرد على الهاتف فقط إذا كان الطلب من نوع "كتابة مع رد"
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

// 4) إشعار إذا توقف الإعلان عن نفسه
serviceProvider.AdvertisementStatusChanged += (s, e) =>
{
    if (e.Status == GattServiceProviderAdvertisementStatus.Aborted)
        Console.WriteLine("[ERROR] Advertising aborted — close other Bluetooth apps and restart.");
};

// 5) بدء الإعلان عن نفسه
serviceProvider.StartAdvertising(new GattServiceProviderAdvertisingParameters
{
    IsDiscoverable = true,
    IsConnectable = true,
});

Console.WriteLine("[OK] Server is running and waiting for your phone...");
Console.WriteLine($"[OK] Service UUID: {SERVICE_UUID}");
Console.WriteLine("\nNOTE: The PC will appear in the app under its computer");
Console.WriteLine("name (e.g. DESKTOP-XXXXXX). Keep this window OPEN!");
Console.WriteLine("\nPress Q to quit.");
Console.WriteLine("--------------------------------------------");

while (true)
{
    var key = Console.ReadKey(intercept: true).Key;
    if (key is ConsoleKey.Q or ConsoleKey.Escape) break;
}

serviceProvider.StopAdvertising();
Console.WriteLine("\nServer stopped. Bye!");

// استدعاء دالة ويندوز لقفل الشاشة
static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool LockWorkStation();
}
