namespace UhfPrime.Client.Protocol;

/// <summary>
/// Command identifiers documented in the UHF Prime reader manual.
/// </summary>
public enum UhfPrimeCommand : ushort
{
    InventoryIsoContinue = 0x0001,
    InventoryStop = 0x0002,
    ModuleInit = 0x0050,
    Reboot = 0x0052,
    SetPower = 0x0053,
    SetProtocol = 0x0059,
    SetGetNetworkParameters = 0x0064,
    GetDeviceInfo = 0x0070,
    SetAllParameters = 0x0071,
    GetAllParameters = 0x0072,
    RelayControl = 0x0077
}
