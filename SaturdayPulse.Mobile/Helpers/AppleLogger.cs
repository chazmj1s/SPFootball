using System.Runtime.InteropServices;

namespace SaturdayPulse.Helpers
{

    public static class AppleLogger
    {
        [DllImport("/usr/lib/libsys.dylib", EntryPoint = "os_log_with_type", CharSet = CharSet.Ansi)]
        private static extern void os_log_with_type(IntPtr log, byte type, string format, string message);

        public static void LogToMacConsole(string message)
        {
            // 0x01 maps to OS_LOG_TYPE_INFO, forcing Apple to listen
            os_log_with_type(IntPtr.Zero, 0x01, "%s", $"[MAUI_DEBUG] {message}");
        }
    }
}