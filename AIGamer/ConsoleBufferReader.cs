using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AIGamer;

public class ConsoleBufferReader
{
    // Existing API declarations...
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out CONSOLE_SCREEN_BUFFER_INFO lpConsoleScreenBufferInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadConsoleOutputCharacter(IntPtr hConsoleOutput, [Out] StringBuilder lpCharacter, uint nLength, COORD dwReadCoord, out uint lpNumberOfCharsRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    // Constants for console and CreateFile
    private const int STD_OUTPUT_HANDLE = -11;
    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    // Struct declarations remain unchanged
    [StructLayout(LayoutKind.Sequential)]
    public struct COORD
    {
        public short X;
        public short Y;
        public COORD(short x, short y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SMALL_RECT
    {
        public short Left;
        public short Top;
        public short Right;
        public short Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct CONSOLE_SCREEN_BUFFER_INFO
    {
        public COORD dwSize;
        public COORD dwCursorPosition;
        public ushort wAttributes;
        public SMALL_RECT srWindow;
        public COORD dwMaximumWindowSize;
    }

    private Process gameProcess;
    private IntPtr windowHandle;

    public ConsoleBufferReader(Process process, IntPtr windowHandle)
    {
        gameProcess = process;
        this.windowHandle = windowHandle;
        // Minimal logging here so as not to interfere with console operations
        Console.WriteLine("ConsoleBufferReader initialized.");
    }

    public string ReadConsoleBuffer()
    {
        // Locking to synchronize with any other console access
        lock (Program.ConsoleLock)
        {
            string resultText = "";
            IntPtr consoleHandle = IntPtr.Zero;
            try
            {
                // Detach from our own console
                FreeConsole();

                // Attach to the game process's console
                if (!AttachConsole((uint)gameProcess.Id))
                {
                    // Optionally log the failure elsewhere if needed
                    return "Failed to attach to game process console.";
                }
                // Allow the attach to settle
                Thread.Sleep(200);

                // Instead of GetStdHandle, open the console output via CONOUT$
                consoleHandle = CreateFile("CONOUT$", GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);

                if (consoleHandle == INVALID_HANDLE_VALUE)
                {
                    return "Failed to open CONOUT$ handle.";
                }

                if (GetConsoleScreenBufferInfo(consoleHandle, out CONSOLE_SCREEN_BUFFER_INFO bufferInfo))
                {
                    int width = bufferInfo.srWindow.Right - bufferInfo.srWindow.Left + 1;
                    StringBuilder sb = new StringBuilder();
                    // Read each visible line from the game console buffer.
                    for (short row = bufferInfo.srWindow.Top; row <= bufferInfo.srWindow.Bottom; row++)
                    {
                        StringBuilder line = new StringBuilder(width);
                        COORD position = new COORD(bufferInfo.srWindow.Left, row);
                        if (ReadConsoleOutputCharacter(consoleHandle, line, (uint)width, position, out uint charsRead))
                        {
                            sb.AppendLine(line.ToString());
                        }
                    }
                    resultText = sb.ToString();
                }
                else
                {
                    int error = Marshal.GetLastWin32Error();
                    resultText = $"Failed to get console buffer info. Error: {error}";
                }
            }
            catch (Exception ex)
            {
                resultText = $"Error reading game console buffer: {ex.Message}";
            }
            finally
            {
                // Clean up the opened handle if it was successfully created.
                if (consoleHandle != IntPtr.Zero && consoleHandle != INVALID_HANDLE_VALUE)
                {
                    CloseHandle(consoleHandle);
                }
                // Detach from the game console and reattach to our original console.
                FreeConsole();
                AttachConsole(ATTACH_PARENT_PROCESS);
            }
            return resultText;
        }
    }
}
