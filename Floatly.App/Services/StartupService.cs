using System;
using Microsoft.Win32;

namespace Floatly.Services;

public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Floatly";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("Windows startup settings are unavailable.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }
        var path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Unable to determine the executable path.");
        if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Publish Floatly as an executable before enabling startup.");
        if (path.Contains(@"\bin\Debug\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Create the Release executable first. Do not enable startup for a debug build.");
        key.SetValue(ValueName, $"\"{path}\"", RegistryValueKind.String);
    }
}
