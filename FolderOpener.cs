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

    /// <summary>True when the game runs under Wine/Proton (Linux) rather than native Windows.</summary>
    public static bool IsRunningUnderWine => IsWine.Value;

    /// <summary>Opens <paramref name="folder"/> in the host file manager; returns false if that failed.</summary>
    public static bool Open(string folder)
    {
        var (file, args) = Command(folder, IsWine.Value);
        try
        {
            Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool DetectWine()
    {
        var ntdll = GetModuleHandleW("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
}
