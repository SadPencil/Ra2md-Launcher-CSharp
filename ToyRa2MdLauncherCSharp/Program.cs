using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ToyRa2MdLauncherCSharp.AlefCrypto;

namespace ToyRa2MdLauncherCSharp;

internal static class Program {
    private const string MutexName = "48BC11BD-C4D7-466b-8A31-C6ABBAD47B3E";
    private const string EventName = "D6E7FC97-64F9-4d28-B52C-754EDF721C6F";
    private const uint WM_CUSTOM = 0xBEEF;

    // P/Invoke declarations
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateFileMapping(
        IntPtr hFile,
        IntPtr lpAttributes,
        uint flProtect,
        uint dwMaxHigh,
        uint dwMaxLow,
        string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr MapViewOfFile(
        SafeFileHandle hFileMapping,
        uint dwDesiredAccess,
        uint dwFileOffsetHigh,
        uint dwFileOffsetLow,
        UIntPtr dwNumberOfBytesToMap);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UnmapViewOfFile(IntPtr lpBaseAddress);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpSecurityAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForMultipleObjects(uint nCount, IntPtr[] lpHandles, bool bWaitAll, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(
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

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern bool GetVolumeInformationA(
        string rootPathName,
        StringBuilder volumeNameBuffer,
        uint volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength,
        out uint fileSystemFlags,
        StringBuilder fileSystemNameBuffer,
        uint fileSystemNameSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetCommandLineW();

    private const uint PAGE_READWRITE = 0x04;
    private const uint FILE_MAP_ALL_ACCESS = 0xF001F;
    private const uint WAIT_OBJECT_0 = 0x00000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct SECURITY_ATTRIBUTES {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFO {
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
    public struct PROCESS_INFORMATION {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    private static string GetRawCommandLineWithoutFirstArg() {
        // Get the command line string
        string commandLine = Marshal.PtrToStringUni(GetCommandLineW());

        // Handle null or empty command line
        if (string.IsNullOrEmpty(commandLine)) {
            Debug.WriteLine("Arguments: <empty>");
            return string.Empty;
        }

        // Find the end of the executable path
        int startIndex;
        if (commandLine.StartsWith("\"")) {
            startIndex = 1; // Skip opening quote
            startIndex = commandLine.IndexOf('"', startIndex);
            if (startIndex == -1) // Handle unbalanced quotes
            {
                Debug.WriteLine("Arguments: <unbalanced quotes, returning empty>");
                return string.Empty;
            }
            startIndex++; // Skip closing quote
        }
        else {
            startIndex = commandLine.IndexOf(' ');
            if (startIndex == -1) // No spaces, only executable path
            {
                Debug.WriteLine("Arguments: <no arguments>");
                return string.Empty;
            }
        }

        // Skip any spaces after the executable path
        while (startIndex < commandLine.Length && commandLine[startIndex] == ' ') {
            startIndex++;
        }

        // Extract arguments
        string arguments = startIndex < commandLine.Length ? commandLine.Substring(startIndex) : string.Empty;
        Debug.WriteLine("Arguments: " + arguments);
        return arguments;
    }

    private static void Main(string[] args) {
        // Change working directory to the executable's directory
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        bool isRa2Md = true;
#if !RA2MD
        isRa2Md = false;
#endif

        string conquerDat = isRa2Md ? "ConquerMD.dat" : "Conquer.dat";
        string gameExe = isRa2Md ? "gamemd.exe" : "game.exe";

        string rawArgs = GetRawCommandLineWithoutFirstArg();
        string commandLine = $"\"{gameExe}\"" + (string.IsNullOrEmpty(rawArgs) ? string.Empty : " ") + rawArgs;
        Console.WriteLine(commandLine);

        using Mutex mutex = new(false, MutexName, out bool mutexCreatedNew);
        SafeFileHandle hMapping = null;
        IntPtr pView = IntPtr.Zero;

        if (mutexCreatedNew) {
            byte[] fileData = File.Exists(conquerDat) ? File.ReadAllBytes(conquerDat) : null;
            if (fileData == null) {
                Console.WriteLine(conquerDat + " missing.");
                return;
            }

            uint size = (uint)fileData.Length;

            // Create inheritable security attributes
            SECURITY_ATTRIBUTES sa = new() {
                nLength = Marshal.SizeOf(typeof(SECURITY_ATTRIBUTES)),
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = true
            };

            IntPtr pSa = Marshal.AllocHGlobal(sa.nLength);
            try {
                Marshal.StructureToPtr(sa, pSa, false);

                hMapping = CreateFileMapping(
                    new IntPtr(-1),
                    pSa,
                    PAGE_READWRITE,
                    0,
                    size,
                    null);

                if (hMapping.IsInvalid) {
                    Console.WriteLine($"Failed to create file mapping. Error: {Marshal.GetLastWin32Error()}");
                    return;
                }
            }
            finally {
                Marshal.FreeHGlobal(pSa);
            }

            pView = MapViewOfFile(hMapping, FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr(size));
            if (pView == IntPtr.Zero) {
                Console.WriteLine($"Failed to map view. Error: {Marshal.GetLastWin32Error()}");
                hMapping.Close();
                return;
            }

            Marshal.Copy(fileData, 0, pView, fileData.Length);

            // Modify the file mapping by calculating with serial key etc. This step is only needed for a legit retail CD installation.
            ModifyMappedData(pView, fileData.Length, isRa2Md);
        }

        // Launch game using CreateProcess
        STARTUPINFO si = new() { cb = Marshal.SizeOf(typeof(STARTUPINFO)) };

        IntPtr hEvent = CreateEvent(IntPtr.Zero, false, false, EventName);
        bool isOtherInstanceRunning = Marshal.GetLastWin32Error() == 183;

        bool success = CreateProcessW(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            true, // Inherit handles
            0,
            IntPtr.Zero,
            Environment.CurrentDirectory,
            ref si,
            out PROCESS_INFORMATION pi);

        if (!success) {
            Console.WriteLine($"Failed to launch game. Error: {Marshal.GetLastWin32Error()}");
            Cleanup(pView, hMapping);
            return;
        }

        try {
            // Start thread to handle event and message
            Thread monitorThread = new(() => HandleEventAndMessage(pi.hProcess, pi.dwThreadId, hMapping, hEvent, isOtherInstanceRunning));
            monitorThread.Start();

            // Wait for the game process to exit
            WaitForSingleObject(pi.hProcess, 0xFFFFFFFF); // Infinite wait
        }
        finally {
            // Cleanup process handles
            CloseHandle(pi.hProcess);
            CloseHandle(pi.hThread);
        }

        if (mutexCreatedNew) {
            Cleanup(pView, hMapping);
        }
    }

    private static void ModifyMappedData(IntPtr pView, int length, bool isRa2Md = true) {
        StringBuilder keyBuilder = new();

        using RegistryKey HKLM32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        string regKeyName = isRa2Md ? @"SOFTWARE\Westwood\Yuri's Revenge" : @"SOFTWARE\Westwood\Red Alert 2";
        using (RegistryKey regKey = HKLM32.OpenSubKey(regKeyName)) {
            if (regKey != null) {
                uint serialNum = 0;
                string installPath = regKey.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath)) {
                    string root = Path.GetPathRoot(installPath);
                    _ = GetVolumeInformationA(root, null, 0, out serialNum, out _, out _, null, 0);
                }
                _ = keyBuilder.AppendFormat("{0:x}-", serialNum);

                string serial = regKey.GetValue("Serial") as string;
                if (!string.IsNullOrEmpty(serial)) {
                    _ = keyBuilder.Append(serial);
                }
            }
        }

        using (RegistryKey regKey = HKLM32.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion")) {
            if (regKey != null) {
                string productId = regKey.GetValue("ProductID") as string;
                _ = keyBuilder.Append("-"); // Append hyphen regardless of productId
                if (!string.IsNullOrEmpty(productId)) {
                    _ = keyBuilder.Append(productId);
                }
            }
        }

        string keyStr = keyBuilder.ToString();
        if (string.IsNullOrEmpty(keyStr)) {
            // No key info found; leave data as is
            return;
        }

        byte[] keyBytes = Encoding.ASCII.GetBytes(keyStr);
        BlowfishContext bf = new(keyBytes);

        byte[] data = new byte[length];
        Marshal.Copy(pView, data, 0, length);

        int numBlocks = length / 8;
        for (int i = 0; i < numBlocks; i++) {
            byte[] block = new byte[8];
            Array.Copy(data, i * 8, block, 0, 8);

            bf.Decrypt(block, block.Length);
            Array.Copy(block, 0, data, i * 8, 8);
        }
        // Remainder (if any) is left unmodified

        Marshal.Copy(data, 0, pView, length);
    }

    private static void HandleEventAndMessage(IntPtr hProcess, uint threadId, SafeFileHandle hMapping, IntPtr hEvent, bool isOtherInstanceRunning) {
        if (!isOtherInstanceRunning) {
            IntPtr[] handles = { hEvent, hProcess };
            uint waitResult = WaitForMultipleObjects(2, handles, false, 300000);

            if (waitResult == WAIT_OBJECT_0 && hMapping != null && !hMapping.IsInvalid) {
                _ = PostThreadMessage(threadId, WM_CUSTOM, IntPtr.Zero, hMapping.DangerousGetHandle());
            }
        }

        _ = CloseHandle(hEvent);
    }

    private static void Cleanup(IntPtr pView, SafeFileHandle hMapping) {
        if (pView != IntPtr.Zero) {
            _ = UnmapViewOfFile(pView);
        }

        if (hMapping != null && !hMapping.IsInvalid) {
            hMapping.Close();
        }
    }
}
