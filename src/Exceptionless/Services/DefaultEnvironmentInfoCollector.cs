﻿using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
#if !PORTABLE && !NETSTANDARD1_2
using System.Net.Sockets;
#endif
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Exceptionless.Logging;
using Exceptionless.Models.Data;

namespace Exceptionless.Services {
    public class DefaultEnvironmentInfoCollector : IEnvironmentInfoCollector {
        private static EnvironmentInfo _environmentInfo;
        private readonly IExceptionlessLog _log;

        public DefaultEnvironmentInfoCollector(IExceptionlessLog log) {
            _log = log;
        }

        public EnvironmentInfo GetEnvironmentInfo() {
            if (_environmentInfo != null) {
                PopulateThreadInfo(_environmentInfo);
                PopulateMemoryInfo(_environmentInfo);            
                return _environmentInfo;
            }

            var info = new EnvironmentInfo();
            PopulateRuntimeInfo(info);
            PopulateProcessInfo(info);
            PopulateThreadInfo(info);
            PopulateMemoryInfo(info);

            _environmentInfo = info;
            return _environmentInfo;
        }

        private void PopulateApplicationInfo(EnvironmentInfo info) {
#if NET45 || NETSTANDARD1_5
            try {
                info.Data.Add("AppDomainName", AppDomain.CurrentDomain.FriendlyName);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get AppDomain friendly name. Error message: {0}", ex.Message);
            }
#endif

#if !PORTABLE && !NETSTANDARD1_2
            try {
                IPHostEntry hostEntry = Dns.GetHostEntryAsync(Dns.GetHostName()).ConfigureAwait(false).GetAwaiter().GetResult();
                if (hostEntry != null && hostEntry.AddressList.Any())
                    info.IpAddress = String.Join(", ", hostEntry.AddressList.Where(x => x.AddressFamily == AddressFamily.InterNetwork).Select(a => a.ToString()).ToArray());
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get ip address. Error message: {0}", ex.Message);
            }
#endif
        }

        private void PopulateProcessInfo(EnvironmentInfo info) {
            try {
                info.ProcessorCount = Environment.ProcessorCount;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get processor count. Error message: {0}", ex.Message);
            }

#if !PORTABLE && !NETSTANDARD1_2
            try {
                Process process = Process.GetCurrentProcess();
                info.ProcessName = process.ProcessName;
                info.ProcessId = process.Id.ToString(NumberFormatInfo.InvariantInfo);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get process name or id. Error message: {0}", ex.Message);
            }

            try {
#if NETSTANDARD1_5
                info.CommandLine = String.Join(" ", Environment.GetCommandLineArgs());
#elif NET45
                info.CommandLine = Environment.CommandLine;
#endif
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get command line. Error message: {0}", ex.Message);
            }
#endif
        }

        private void PopulateThreadInfo(EnvironmentInfo info) {
#if !PORTABLE && !NETSTANDARD1_2
            try {
                info.ThreadId = Thread.CurrentThread.ManagedThreadId.ToString(NumberFormatInfo.InvariantInfo);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get thread id. Error message: {0}", ex.Message);
            }

            try {
                info.ThreadName = Thread.CurrentThread.Name;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get current thread name. Error message: {0}", ex.Message);
            }
#endif
        }

        private void PopulateMemoryInfo(EnvironmentInfo info) {
#if !PORTABLE && !NETSTANDARD1_2
            try {
                Process process = Process.GetCurrentProcess();
                info.ProcessMemorySize = process.PrivateMemorySize64;
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get process memory size. Error message: {0}", ex.Message);
            }
#endif

#if NET45
            try {
                if (IsMonoRuntime) {
                    if (PerformanceCounterCategory.Exists("Mono Memory")) {
                        var totalPhysicalMemory = new PerformanceCounter("Mono Memory", "Total Physical Memory");
                        info.TotalPhysicalMemory = Convert.ToInt64(totalPhysicalMemory.RawValue);

                        var availablePhysicalMemory = new PerformanceCounter("Mono Memory", "Available Physical Memory"); //mono 4.0+
                        info.AvailablePhysicalMemory = Convert.ToInt64(availablePhysicalMemory.RawValue);
                    }
                } else {
                    var computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();
                    info.TotalPhysicalMemory = Convert.ToInt64(computerInfo.TotalPhysicalMemory);
                    info.AvailablePhysicalMemory = Convert.ToInt64(computerInfo.AvailablePhysicalMemory);
                }
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get physical memory. Error message: {0}", ex.Message);
            }
#endif
        }

#if NET45
        private bool IsMonoRuntime {
            get {
                try {
                    return Type.GetType("Mono.Runtime") != null;
                } catch (Exception) {
                    return false;
                }
            }
        }
#endif

        private void PopulateRuntimeInfo(EnvironmentInfo info) {
            try {
#if NET45 || NETSTANDARD1_5
                info.MachineName = Environment.MachineName;
#elif !PORTABLE && !NETSTANDARD1_2
                Process process = Process.GetCurrentProcess();
                info.MachineName = process.MachineName;
#else
                info.MachineName = Guid.NewGuid().ToString("N");
#endif
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get machine name. Error message: {0}", ex.Message);
            }

#if !PORTABLE && !NETSTANDARD1_2
#if NETSTANDARD
            Microsoft.Extensions.PlatformAbstractions.PlatformServices computerInfo = null;
#elif NET45
            Microsoft.VisualBasic.Devices.ComputerInfo computerInfo = null;
#endif

            try {
#if NETSTANDARD
                computerInfo = Microsoft.Extensions.PlatformAbstractions.PlatformServices.Default;
#elif NET45
                computerInfo = new Microsoft.VisualBasic.Devices.ComputerInfo();                
#endif
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get computer info. Error message: {0}", ex.Message);
            }

#if NETSTANDARD || NET45
            if (computerInfo == null)
                return;
#endif

            try {
#if NETSTANDARD
                info.Data["ApplicationBasePath"] = computerInfo.Application.ApplicationBasePath;
                info.Data["ApplicationName"] = computerInfo.Application.ApplicationName;
                info.Data["RuntimeFramework"] = computerInfo.Application.RuntimeFramework.FullName;

                info.OSName = computerInfo.Runtime.OperatingSystem;
                info.OSVersion = computerInfo.Runtime.OperatingSystemVersion;
                info.Architecture = computerInfo.Runtime.RuntimeArchitecture;
                info.RuntimeVersion = computerInfo.Runtime.RuntimeVersion;
                info.Data["RuntimeType"] = computerInfo.Runtime.RuntimeType; // Mono, CLR, CoreCLR
#elif NET45
                info.OSName = computerInfo.OSFullName;
                info.OSVersion = computerInfo.OSVersion;
                info.Architecture = Is64BitOperatingSystem() ? "x64" : "x86";
                info.RuntimeVersion = Environment.Version.ToString();
#endif
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get populate runtime info. Error message: {0}", ex.Message);
            }
#endif
            }

#if NET45
        private bool Is64BitOperatingSystem() {
            if (IntPtr.Size == 8) // 64-bit programs run only on Win64
                return true;

            try {
                // Detect whether the current process is a 32-bit process running on a 64-bit system.
                bool is64;
                bool methodExist = KernelNativeMethods.MethodExists("kernel32.dll", "IsWow64Process");

                return ((methodExist && KernelNativeMethods.IsWow64Process(KernelNativeMethods.GetCurrentProcess(), out is64)) && is64);
            } catch (Exception ex) {
                _log.FormattedInfo(typeof(DefaultEnvironmentInfoCollector), "Unable to get CPU architecture. Error message: {0}", ex.Message);
            }

            return false;
        }

        private static class KernelNativeMethods {
#region Kernel32

            [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr GetProcAddress(IntPtr hModule, [MarshalAs(UnmanagedType.LPStr)] string procName);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool IsWow64Process(IntPtr hProcess, out bool wow64Process);

            [DllImport("kernel32.dll")]
            public static extern IntPtr GetCurrentProcess();

            [DllImport("kernel32.dll")]
            public static extern int GetCurrentProcessId();

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            [PreserveSig]
            public static extern int GetModuleFileName([In] IntPtr hModule, [Out] StringBuilder lpFilename, [In] [MarshalAs(UnmanagedType.U4)] int nSize);

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr GetModuleHandle(string moduleName);

            [DllImport("kernel32.dll")]
            public static extern int GetCurrentThreadId();

#endregion

            public static bool MethodExists(string moduleName, string methodName) {
                IntPtr moduleHandle = GetModuleHandle(moduleName);
                if (moduleHandle == IntPtr.Zero)
                    return false;

                return (GetProcAddress(moduleHandle, methodName) != IntPtr.Zero);
            }
        }
#endif
    }
}