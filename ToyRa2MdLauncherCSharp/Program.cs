using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ToyRa2MdLauncherCSharp.AlefCrypto;

namespace ToyRa2MdLauncherCSharp;

internal static class Program {
    private const string MutexName = "48BC11BD-C4D7-466b-8A31-C6ABBAD47B3E";
    private const string EventName = "D6E7FC97-64F9-4d28-B52C-754EDF721C6F";
    private const uint WM_CUSTOM = 0xBEEF;

    private static string GetRawCommandLineWithoutFirstArg() {
        // Get the command line string
        string commandLine = Marshal.PtrToStringUni(NativeMethods.GetCommandLineW());

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

        IntPtr consoleWindow = NativeMethods.GetConsoleWindow();

        bool isRa2Md = true;
#if !RA2MD
        isRa2Md = false;
#endif

        string conquerDat = isRa2Md ? "ConquerMD.dat" : "Conquer.dat";
        string gameExe = isRa2Md ? "gamemd.exe" : "game.exe";
        string expectedPlaintext = isRa2Md ? "UIDATA,3DDATA,MAPS\0" : "(c) 2000 Electronic Arts, Inc. All Rights Reserved";

        string rawArgs = GetRawCommandLineWithoutFirstArg();
        string commandLine = $"\"{gameExe}\"" + (string.IsNullOrEmpty(rawArgs) ? string.Empty : " ") + rawArgs;
        Console.WriteLine(commandLine);

        using Mutex mutex = new(false, MutexName, out bool mutexCreatedNew);
        SafeFileHandle hMapping = null;
        IntPtr pView = IntPtr.Zero;

        if (mutexCreatedNew) {
            byte[] conquerData = File.Exists(conquerDat) ? File.ReadAllBytes(conquerDat) : null;
            if (conquerData == null) {
                Console.WriteLine(conquerDat + " missing.");
                return;
            }

            uint size = (uint)conquerData.Length;

            // Create inheritable security attributes
            NativeMethods.SECURITY_ATTRIBUTES sa = new() {
                nLength = Marshal.SizeOf(typeof(NativeMethods.SECURITY_ATTRIBUTES)),
                lpSecurityDescriptor = IntPtr.Zero,
                bInheritHandle = true
            };

            IntPtr pSa = Marshal.AllocHGlobal(sa.nLength);
            try {
                Marshal.StructureToPtr(sa, pSa, false);

                hMapping = NativeMethods.CreateFileMapping(
                    new IntPtr(-1),
                    pSa,
                    NativeMethods.PAGE_READWRITE,
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

            pView = NativeMethods.MapViewOfFile(hMapping, NativeMethods.FILE_MAP_ALL_ACCESS, 0, 0, new UIntPtr(size));
            if (pView == IntPtr.Zero) {
                Console.WriteLine($"Failed to map view. Error: {Marshal.GetLastWin32Error()}");
                hMapping.Close();
                return;
            }

            // Modify the file mapping by calculating with serial key etc. This step is only needed for a legit retail CD installation.
            DecryptConquerData(ref conquerData, isRa2Md);

            byte[] expectedPlaintextBytes = Encoding.ASCII.GetBytes(expectedPlaintext);
            if (expectedPlaintextBytes.SequenceEqual(conquerData)) {
                Console.WriteLine("You own a legit CD copy. This is awesome!");
            }
            else {
                conquerData = expectedPlaintextBytes;
            }

            Marshal.Copy(conquerData, 0, pView, conquerData.Length);
        }

        // Launch game using CreateProcess
        NativeMethods.STARTUPINFO si = new() { cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO)) };

        IntPtr hEvent = NativeMethods.CreateEvent(IntPtr.Zero, false, false, EventName);
        bool isOtherInstanceRunning = Marshal.GetLastWin32Error() == 183;

        bool success = NativeMethods.CreateProcessW(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            true, // Inherit handles
            0,
            IntPtr.Zero,
            Environment.CurrentDirectory,
            ref si,
            out NativeMethods.PROCESS_INFORMATION pi);

        if (!success) {
            Console.WriteLine($"Failed to launch game. Error: {Marshal.GetLastWin32Error()}");
            Cleanup(pView, hMapping);
            return;
        }

        try {
            // Start thread to handle event and message
            Thread monitorThread = new(() => {
                HandleEventAndMessage(pi.hProcess, pi.dwThreadId, hMapping, hEvent, isOtherInstanceRunning);

                // Hide console window
                if (consoleWindow != IntPtr.Zero) {
                    NativeMethods.ShowWindow(consoleWindow, NativeMethods.SW_HIDE);
                }
            });
            monitorThread.Start();

            // Wait for the game process to exit
            NativeMethods.WaitForSingleObject(pi.hProcess, 0xFFFFFFFF); // Infinite wait
        }
        finally {
            // Cleanup process handles
            NativeMethods.CloseHandle(pi.hProcess);
            NativeMethods.CloseHandle(pi.hThread);
        }

        if (mutexCreatedNew) {
            Cleanup(pView, hMapping);
        }
    }

    private static string GetBlowfishKey(bool isRa2Md) {
        StringBuilder keyBuilder = new();

        using RegistryKey HKLM32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
        string regKeyName = isRa2Md ? @"SOFTWARE\Westwood\Yuri's Revenge" : @"SOFTWARE\Westwood\Red Alert 2";
        using (RegistryKey regKey = HKLM32.OpenSubKey(regKeyName)) {
            if (regKey != null) {
                uint serialNum = 0;
                string installPath = regKey.GetValue("InstallPath") as string;
                if (!string.IsNullOrEmpty(installPath)) {
                    string root = Path.GetPathRoot(installPath);
                    _ = NativeMethods.GetVolumeInformationA(root, null, 0, out serialNum, out _, out _, null, 0);
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
        return keyStr;
    }

    private static void DecryptConquerData(ref byte[] data, bool isRa2Md) {
        string key = GetBlowfishKey(isRa2Md);
        if (string.IsNullOrEmpty(key)) {
            // No key info found; leave data as is
            return;
        }

        byte[] keyBytes = Encoding.ASCII.GetBytes(key);
        BlowfishContext bf = new(keyBytes);

        int numBlocks = data.Length / 8;
        for (int i = 0; i < numBlocks; i++) {
            byte[] block = new byte[8];
            Array.Copy(data, i * 8, block, 0, 8);

            bf.Decrypt(block, block.Length);
            Array.Copy(block, 0, data, i * 8, 8);
        }
        // Remainder (if any) is left unmodified
    }

    private static void HandleEventAndMessage(IntPtr hProcess, uint threadId, SafeFileHandle hMapping, IntPtr hEvent, bool isOtherInstanceRunning) {
        if (!isOtherInstanceRunning) {
            IntPtr[] handles = { hEvent, hProcess };
            uint waitResult = NativeMethods.WaitForMultipleObjects(2, handles, false, 300000);

            if (waitResult == NativeMethods.WAIT_OBJECT_0 && hMapping != null && !hMapping.IsInvalid) {
                _ = NativeMethods.PostThreadMessage(threadId, WM_CUSTOM, IntPtr.Zero, hMapping.DangerousGetHandle());
            }
        }

        _ = NativeMethods.CloseHandle(hEvent);
    }

    private static void Cleanup(IntPtr pView, SafeFileHandle hMapping) {
        if (pView != IntPtr.Zero) {
            _ = NativeMethods.UnmapViewOfFile(pView);
        }

        if (hMapping != null && !hMapping.IsInvalid) {
            hMapping.Close();
        }
    }
}
