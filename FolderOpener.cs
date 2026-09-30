using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Stellar.PhotoStudio;

/// <summary>Opens a folder in the host file manager: Explorer on Windows, winebrowser (→ xdg-open) under Wine.</summary>
internal static class FolderOpener
{
    private static readonly Lazy<bool> IsWine = new(DetectWine);

    public static (string File, string Args) Command(string folder, bool isWine) =>
        (isWine ? "winebrowser" : "explorer.exe", "\"" + folder + "\"");

    public static void Open(string folder)
    {
        var (file, args) = Command(folder, IsWine.Value);
        Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
    }

    private static bool DetectWine()
    {
        var ntdll = GetModuleHandleW("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
