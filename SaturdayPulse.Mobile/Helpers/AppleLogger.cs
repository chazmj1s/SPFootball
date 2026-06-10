using System.Runtime.InteropServices;
using System.Text;

namespace SaturdayPulse.Helpers
{

    public static class AppleLogger
    {
        // Point directly to Apple's native logging subsystem
        [DllImport("/usr/lib/libsys.dylib", EntryPoint = "os_log_with_type")]
        private static extern void os_log_with_type(IntPtr log, byte type, byte[] format, byte[] message);

        public static void LogToMacConsole(string message)
        {
            try
            {
                // Convert strings to null-terminated UTF-8 byte arrays to prevent interop corruption
                byte[] formatBytes = Encoding.UTF8.GetBytes("%s\0");
                byte[] messageBytes = Encoding.UTF8.GetBytes($"[MAUI_DEBUG] {message}\0");

                // 0x01 maps to OS_LOG_TYPE_INFO so Apple doesn't silence it
                os_log_with_type(IntPtr.Zero, 0x01, formatBytes, messageBytes);
            }
            catch
            {
                // Fail silently so a logging failure never crashes your app core
            }
        }
    }
}