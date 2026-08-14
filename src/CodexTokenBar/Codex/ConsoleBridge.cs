using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace CodexTokenBar.Codex;

internal static class ConsoleBridge
{
    private const uint AttachParentProcess = 0xFFFFFFFF;
    private const int ErrorAccessDenied = 5;

    public static void AttachToParent()
    {
        if (!AttachConsole(AttachParentProcess) && Marshal.GetLastWin32Error() != ErrorAccessDenied)
            return;

        var output = Console.OpenStandardOutput();
        if (output != Stream.Null)
        {
            Console.SetOut(new StreamWriter(output, new UTF8Encoding(false))
            {
                AutoFlush = true,
            });
        }

        var error = Console.OpenStandardError();
        if (error != Stream.Null)
        {
            Console.SetError(new StreamWriter(error, new UTF8Encoding(false))
            {
                AutoFlush = true,
            });
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachConsole(uint processId);
}
