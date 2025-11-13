using System;
using System.Runtime.InteropServices;

namespace UhfPrime.TestBench.Services;

internal static partial class BeepService
{
    public static void TryBeep()
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                NSBeep();
            }
            else if (OperatingSystem.IsWindows())
            {
                Console.Beep(1000, 150);
            }
        }
        catch
        {
            // Ignore audio failures; we just want a hint that the tag was read.
        }
    }

    [DllImport("/System/Library/Frameworks/AppKit.framework/AppKit")]
    private static extern void NSBeep();
}
