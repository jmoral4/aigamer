using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace AIGamer;
public class GameWindowFinder
{
    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // Delegate for EnumWindows
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // Struct to pass data to callback
    private class WindowSearchData
    {
        public List<IntPtr> FoundWindows { get; } = new List<IntPtr>();
        public string SearchString { get; set; }
    }

    public IntPtr FindWarsimWindow()
    {
        Console.WriteLine("Starting to search for Warsim window...");

        // Try finding by process name first
        Process[] processes = Process.GetProcessesByName("Warsim");
        if (processes.Length > 0)
        {
            Console.WriteLine($"Found {processes.Length} Warsim processes");

            // Look more thoroughly for the window
            foreach (var process in processes)
            {
                Console.WriteLine($"Examining Warsim process ID: {process.Id}");
                Console.WriteLine($"Process main window title: '{process.MainWindowTitle}'");

                if (process.MainWindowHandle != IntPtr.Zero)
                {
                    Console.WriteLine($"Found main window handle: {process.MainWindowHandle}");
                    return process.MainWindowHandle;
                }
            }
        }

        Console.WriteLine("Looking for Windows Terminal hosting Warsim...");

        // Look for Windows Terminal window with Warsim in title
        var terminalWindows = FindWindowsByTitle("Windows Terminal");
        Console.WriteLine($"Found {terminalWindows.Count} Windows Terminal windows");

        foreach (var windowHandle in terminalWindows)
        {
            // Get the window title to check if it contains Warsim
            StringBuilder titleBuilder = new StringBuilder(256);
            GetWindowText(windowHandle, titleBuilder, titleBuilder.Capacity);
            string windowTitle = titleBuilder.ToString();

            Console.WriteLine($"Terminal window title: '{windowTitle}'");

            if (windowTitle.Contains("Warsim", StringComparison.OrdinalIgnoreCase) ||
                windowTitle.Contains("Aslona", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Found Windows Terminal with Warsim in title: {windowHandle}");
                return windowHandle;
            }

            // Enumerate child windows to find tabs
            var childWindows = FindChildWindows(windowHandle);
            Console.WriteLine($"Terminal has {childWindows.Count} child windows");

            foreach (var childHandle in childWindows)
            {
                StringBuilder childTitleBuilder = new StringBuilder(256);
                GetWindowText(childHandle, childTitleBuilder, childTitleBuilder.Capacity);
                string childTitle = childTitleBuilder.ToString();

                StringBuilder classNameBuilder = new StringBuilder(256);
                GetClassName(childHandle, classNameBuilder, classNameBuilder.Capacity);
                string className = classNameBuilder.ToString();

                Console.WriteLine($"  Child window: '{childTitle}', Class: '{className}'");

                if (childTitle.Contains("Warsim", StringComparison.OrdinalIgnoreCase) ||
                    childTitle.Contains("Aslona", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"  Found child window with Warsim in title: {childHandle}");
                    return childHandle;
                }
            }
        }

        // If still not found, check all windows with Warsim in title
        Console.WriteLine("Searching all windows for 'Warsim' in title...");
        var warsimWindows = FindWindowsByTitle("Warsim");
        warsimWindows.AddRange(FindWindowsByTitle("Aslona"));

        if (warsimWindows.Count > 0)
        {
            Console.WriteLine($"Found {warsimWindows.Count} windows with Warsim/Aslona in title");
            return warsimWindows[0];
        }

        // If not found, try to use processes with console windows
        Console.WriteLine("Looking for console windows...");
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (string.IsNullOrEmpty(process.MainWindowTitle)) continue;

                Console.WriteLine($"Checking process: {process.ProcessName}, Title: '{process.MainWindowTitle}'");

                if (process.MainWindowTitle.Contains("Warsim", StringComparison.OrdinalIgnoreCase) ||
                    process.MainWindowTitle.Contains("Aslona", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found potential Warsim window by title: {process.MainWindowTitle}");
                    return process.MainWindowHandle;
                }

                if (process.ProcessName.Equals("WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Found Windows Terminal process: {process.Id}, Title: '{process.MainWindowTitle}'");

                    // If the main window title indicates this could be Warsim, return it
                    if (process.MainWindowTitle.Contains("Warsim", StringComparison.OrdinalIgnoreCase) ||
                        process.MainWindowTitle.Contains("Aslona", StringComparison.OrdinalIgnoreCase))
                    {
                        return process.MainWindowHandle;
                    }
                }
            }
            catch (Exception ex)
            {
                // Skip this process if we can't access it
                Console.WriteLine($"Error accessing process: {ex.Message}");
            }
        }

        Console.WriteLine("No Warsim window found. As a fallback, returning active Windows Terminal window if available...");
        if (terminalWindows.Count > 0)
        {
            Console.WriteLine($"Returning first Windows Terminal window as fallback: {terminalWindows[0]}");
            return terminalWindows[0];
        }

        Console.WriteLine("Could not find any suitable window for Warsim");
        return IntPtr.Zero;
    }

    private List<IntPtr> FindWindowsByTitle(string partialTitle)
    {
        var searchData = new WindowSearchData { SearchString = partialTitle };

        EnumWindows((hWnd, lParam) => {
            StringBuilder titleBuilder = new StringBuilder(256);
            GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string windowTitle = titleBuilder.ToString();

            if (!string.IsNullOrEmpty(windowTitle) &&
                windowTitle.Contains(searchData.SearchString, StringComparison.OrdinalIgnoreCase))
            {
                searchData.FoundWindows.Add(hWnd);
            }
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return searchData.FoundWindows;
    }

    private List<IntPtr> FindChildWindows(IntPtr parentWindow)
    {
        var children = new List<IntPtr>();

        EnumChildWindows(parentWindow, (hWnd, lParam) => {
            children.Add(hWnd);
            return true; // Continue enumeration
        }, IntPtr.Zero);

        return children;
    }

    public bool IsProcessSteamGame(Process process)
    {
        try
        {
            foreach (ProcessModule module in process.Modules)
            {
                if (module.FileName.Contains("steam", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore errors accessing modules
        }

        return false;
    }

    public Process FindSteamProcess()
    {
        return Process.GetProcessesByName("steam").FirstOrDefault();
    }
}