using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace SynapticPro
{
    /// <summary>
    /// Launches Node.js HTTP server detached from Unity's Win32 JobObject.
    ///
    /// Unity Editor on Windows assigns a Job Object with
    /// JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE to itself; any child process started
    /// via Process.Start inherits the Job and is killed when Unity manipulates
    /// the Job (assembly reload, PlayMode transitions, certain GC paths).
    ///
    /// This launcher uses CreateProcessW via P/Invoke with
    /// CREATE_BREAKAWAY_FROM_JOB so the spawned node.exe is independent of
    /// Unity's Job. Combined with a parent-PID watchdog in http-server.js the
    /// child is reliably tied to Unity's lifecycle without being subject to
    /// Job-Object cascade kills.
    ///
    /// PID is persisted in SessionState so the same process can be re-attached
    /// after domain reload (recovers ESC-0095 "Connect Unity Only" case).
    /// </summary>
    public static class SynapticDetachedProcess
    {
        public const string PID_KEY = "Synaptic.NodeServer.PID";
        public const string PORT_KEY = "Synaptic.NodeServer.PORT";

        // ----- Win32 P/Invoke -----
        private const uint CREATE_BREAKAWAY_FROM_JOB  = 0x01000000;
        private const uint CREATE_NEW_PROCESS_GROUP   = 0x00000200;
        private const uint DETACHED_PROCESS           = 0x00000008;
        private const uint CREATE_NO_WINDOW           = 0x08000000;
        private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
            public int dwFlags;
            public short wShowWindow, cbReserved2;
            public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess, hThread;
            public int dwProcessId, dwThreadId;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateProcessW(
            string lpApplicationName, string lpCommandLine,
            IntPtr lpProcessAttributes, IntPtr lpThreadAttributes,
            bool bInheritHandles, uint dwCreationFlags,
            IntPtr lpEnvironment, string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr h);

        /// <summary>
        /// Start node.exe http-server.js detached from Unity's Job.
        /// Returns spawned PID on success, 0 on failure.
        /// Windows only — caller falls back to Process.Start on other platforms.
        /// </summary>
        public static int StartWindows(string nodeExe, string scriptPath, int port, string mcpServerDir, string logFile)
        {
            int unityPid = Process.GetCurrentProcess().Id;
            string cmd = $"\"{nodeExe}\" \"{scriptPath}\" {port} --parent-pid={unityPid} --log=\"{logFile}\"";

            var si = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            uint flags = CREATE_BREAKAWAY_FROM_JOB
                       | DETACHED_PROCESS
                       | CREATE_NEW_PROCESS_GROUP
                       | CREATE_NO_WINDOW
                       | CREATE_UNICODE_ENVIRONMENT;

            bool ok = CreateProcessW(
                null, cmd,
                IntPtr.Zero, IntPtr.Zero,
                false, flags,
                IntPtr.Zero, mcpServerDir,
                ref si, out PROCESS_INFORMATION pi);

            // Fallback: if BREAKAWAY denied (ACCESS_DENIED on JobObject without
            // JOB_OBJECT_LIMIT_BREAKAWAY_OK), retry with DETACHED only and rely
            // on node-side parent-PID watchdog for orphan cleanup.
            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                UnityEngine.Debug.LogWarning($"[Synaptic] CreateProcess with BREAKAWAY failed (err={err}). Retrying without BREAKAWAY.");
                flags &= ~CREATE_BREAKAWAY_FROM_JOB;
                ok = CreateProcessW(
                    null, cmd,
                    IntPtr.Zero, IntPtr.Zero,
                    false, flags,
                    IntPtr.Zero, mcpServerDir,
                    ref si, out pi);
                if (!ok)
                {
                    int err2 = Marshal.GetLastWin32Error();
                    UnityEngine.Debug.LogError($"[Synaptic] CreateProcess fallback also failed (err={err2}).");
                    return 0;
                }
            }

            CloseHandle(pi.hThread);
            CloseHandle(pi.hProcess);

            SessionState.SetInt(PID_KEY, pi.dwProcessId);
            SessionState.SetInt(PORT_KEY, port);
            EditorPrefs.SetInt(PID_KEY, pi.dwProcessId);
            EditorPrefs.SetInt(PORT_KEY, port);
            return pi.dwProcessId;
        }

        /// <summary>
        /// Check whether the previously-spawned PID is still alive.
        /// Used after domain reload to recover the connection rather than
        /// re-spawning.
        /// </summary>
        public static bool IsStoredProcessAlive(out int pid, out int port)
        {
            pid = SessionState.GetInt(PID_KEY, 0);
            port = SessionState.GetInt(PORT_KEY, 0);
            if (pid == 0) { pid = EditorPrefs.GetInt(PID_KEY, 0); port = EditorPrefs.GetInt(PORT_KEY, 0); }
            if (pid == 0) return false;

            try
            {
                var p = Process.GetProcessById(pid);
                if (p == null || p.HasExited) return false;
                // Sanity: must be a node process
                string name = p.ProcessName.ToLowerInvariant();
                return name.Contains("node");
            }
            catch
            {
                return false;
            }
        }

        public static void ClearStoredPid()
        {
            SessionState.SetInt(PID_KEY, 0);
            SessionState.SetInt(PORT_KEY, 0);
            EditorPrefs.DeleteKey(PID_KEY);
            EditorPrefs.DeleteKey(PORT_KEY);
        }

        public static bool KillStored()
        {
            int pid = SessionState.GetInt(PID_KEY, 0);
            if (pid == 0) pid = EditorPrefs.GetInt(PID_KEY, 0);
            if (pid == 0) return false;
            try
            {
                var p = Process.GetProcessById(pid);
                if (!p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                ClearStoredPid();
            }
        }
    }
}
