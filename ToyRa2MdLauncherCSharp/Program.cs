using Microsoft.Win32.SafeHandles;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace ToyRa2MdLauncherCSharp;

class Program
{
    const string MutexName = "48BC11BD-C4D7-466b-8A31-C6ABBAD47B3E";
    const string EventName = "D6E7FC97-64F9-4d28-B52C-754EDF721C6F";
    const uint WM_CUSTOM = 0xBEEF;

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern SafeFileHandle CreateFileMapping(
        IntPtr hFile,
        IntPtr lpAttributes,
        uint flProtect,
        uint dwMaxHigh,
        uint dwMaxLow,
        string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr MapViewOfFile(
        SafeFileHandle hFileMapping,
        uint dwDesiredAccess,
        uint dwFileOffsetHigh,
        uint dwFileOffsetLow,
        UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEvent(IntPtr lpSecurityAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    static extern uint GetLastError();

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern bool CreateProcess(
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    const uint PAGE_READWRITE = 0x04;
    const uint FILE_MAP_ALL_ACCESS = 0xF001F;
    const uint WAIT_OBJECT_0 = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO
    {
        public int cb;
        public string lpReserved;
        public string lpDesktop;
        public string lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    static void Main(string[] args)
    {
        string gameExe = "gamemd.exe";
        string commandLine = $"\"{gameExe}\""; // Add args if needed

        bool mutexCreatedNew;
        using (Mutex mutex = new Mutex(false, MutexName, out mutexCreatedNew))
        {
            SafeFileHandle hMapping = null;
            IntPtr pView = IntPtr.Zero;

            if (mutexCreatedNew)
            {
                byte[] fileData = File.Exists("ConquerMD.dat") ? File.ReadAllBytes("ConquerMD.dat") : null;
                if (fileData == null)
                {
                    Console.WriteLine("ConquerMD.dat missing.");
                    return;
                }

                uint size = (uint)fileData.Length;

                // Create inheritable security attributes
                SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES
                {
                    nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                    lpSecurityDescriptor = IntPtr.Zero,
                    bInheritHandle = true
                };

                IntPtr pSa = Marshal.AllocHGlobal(sa.nLength);
                try
                {
                    Marshal.StructureToPtr(sa, pSa, false);

                    hMapping = CreateFileMapping(
                        new IntPtr(-1),
                        pSa,
                        PAGE_READWRITE,
                        0,
                        size,
                        null);

                    if (hMapping.IsInvalid)
                    {
                        Console.WriteLine($"Failed to create file mapping. Error: {Marshal.GetLastWin32Error()}");
                        return;
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(pSa);
                }

                pView = MapViewOfFile(hMapping, FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr(size));
                if (pView == IntPtr.Zero)
                {
                    Console.WriteLine($"Failed to map view. Error: {Marshal.GetLastWin32Error()}");
                    hMapping.Close();
                    return;
                }

                Marshal.Copy(fileData, 0, pView, fileData.Length);
            }

            // Launch game using CreateProcess
            STARTUPINFO si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)) };
            PROCESS_INFORMATION pi;

            bool success = CreateProcess(
                null,
                commandLine,
                IntPtr.Zero,
                IntPtr.Zero,
                true, // Inherit handles
                0,
                IntPtr.Zero,
                Environment.CurrentDirectory,
                ref si,
                out pi);

            if (!success)
            {
                Console.WriteLine($"Failed to launch game. Error: {Marshal.GetLastWin32Error()}");
                Cleanup(pView, hMapping);
                return;
            }

            try
            {
                // Start thread to handle event and message
                Thread monitorThread = new Thread(() => HandleEventAndMessage(pi.hProcess, pi.dwThreadId, hMapping));
                monitorThread.Start();

                // Wait for the game process to exit
                WaitForSingleObject(pi.hProcess, 0xFFFFFFFF); // Infinite wait
            }
            finally
            {
                // Cleanup process handles
                CloseHandle(pi.hProcess);
                CloseHandle(pi.hThread);
            }

            if (mutexCreatedNew)
            {
                Cleanup(pView, hMapping);
            }
        }
    }

    static void HandleEventAndMessage(IntPtr hProcess, uint threadId, SafeFileHandle hMapping)
    {
        IntPtr hEvent = CreateEvent(IntPtr.Zero, false, false, EventName);
        uint lastError = GetLastError();

        if (hEvent == IntPtr.Zero)
        {
            Console.WriteLine($"Failed to create event. Error: {lastError}");
            return;
        }

        bool alreadyExists = (lastError == 183);

        if (!alreadyExists)
        {
            IntPtr[] handles = { hEvent, hProcess };
            uint waitResult = WaitForMultipleObjects(2, handles, false, 300000);

            if (waitResult == WAIT_OBJECT_0 && !hMapping.IsInvalid)
            {
                PostThreadMessage(threadId, WM_CUSTOM, IntPtr.Zero, hMapping.DangerousGetHandle());
            }
        }

        CloseHandle(hEvent);
    }

    static void Cleanup(IntPtr pView, SafeFileHandle hMapping)
    {
        if (pView != IntPtr.Zero) UnmapViewOfFile(pView);
        if (!hMapping.IsInvalid) hMapping.Close();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);
}
