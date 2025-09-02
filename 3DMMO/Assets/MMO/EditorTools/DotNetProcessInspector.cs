#if UNITY_EDITOR_WIN
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

// NOTE: Requires .NET 4.x Equivalent and is Windows-only.
// If you get a missing reference error, enable .NET Framework in Player Settings or install the .NET targeting pack.
namespace MMO.EditorTools
{
    public class DotNetProcessInspector : EditorWindow
    {
        [MenuItem("Tools/MMO Starter/Diagnostics/List .NET Host Processes (Windows)")]
        static void Open() { GetWindow<DotNetProcessInspector>().Show(); }

        Vector2 _scroll;
        string _output;
        double _nextRefresh;

        void OnEnable()
        {
            titleContent = new GUIContent(".NET Hosts");
            RefreshNow();
        }

        void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh", GUILayout.Width(90))) RefreshNow();
                GUILayout.FlexibleSpace();
                GUILayout.Label("Shows running 'dotnet.exe' processes + parents & command lines.", EditorStyles.miniLabel);
            }
            using (var sv = new EditorGUILayout.ScrollViewScope(_scroll))
            {
                _scroll = sv.scrollPosition;
                EditorGUILayout.TextArea(_output ?? "", GUILayout.ExpandHeight(true));
            }

            if (EditorApplication.timeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = EditorApplication.timeSinceStartup + 5.0; // auto refresh every 5s
                RefreshNow();
            }
        }

        void RefreshNow()
        {
            try
            {
                var sb = new StringBuilder();
                var dots = Process.GetProcessesByName("dotnet");
                if (dots.Length == 0)
                {
                    _output = "No dotnet.exe processes found.";
                    Repaint();
                    return;
                }

                sb.AppendLine($"Found {dots.Length} dotnet.exe processes at {DateTime.Now:HH:mm:ss} (local time)\n");

                foreach (var p in dots.OrderBy(p => p.Id))
                {
                    string cmd = SafeGetCommandLine(p);
                    var parent = SafeGetParent(p);
                    sb.AppendLine($"PID {p.Id}  Parent {(parent?.Id.ToString() ?? "?")}: {(parent?.ProcessName ?? "<unknown>")}");
                    sb.AppendLine($"  Cmd: {cmd}");
                    sb.AppendLine();
                }

                _output = sb.ToString();
                Repaint();
            }
            catch (Exception ex)
            {
                _output = "Failed to enumerate processes: " + ex.Message + "\n" + ex.StackTrace;
                Repaint();
            }
        }

        static Process SafeGetParent(Process child)
        {
            try
            {
                // Windows-only: use NtQueryInformationProcess via WMI fallback
                // Simple heuristic: find any process whose threads started around the same time — but we can do better using Toolhelp snapshot
                // For brevity, use performance trick: query all processes and pick the one with PID == parentId via native API
                int ppid = ParentProcessUtilities.GetParentProcessId(child.Id);
                if (ppid > 0)
                {
                    try { return Process.GetProcessById(ppid); } catch { return null; }
                }
            }
            catch { }
            return null;
        }

        static string SafeGetCommandLine(Process p)
        {
            try { return ParentProcessUtilities.GetCommandLine(p.Id); }
            catch { return "<no access>"; }
        }
    }

    // Minimal native helpers to get command line & parent PID on Windows
    internal static class ParentProcessUtilities
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        static extern bool CloseHandle(IntPtr hObject);

        const uint QueryLimitedInformation = 0x1000;

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref PROCESS_BASIC_INFORMATION processInformation, int processInformationLength, out int returnLength);

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct PROCESS_BASIC_INFORMATION { public IntPtr Reserved1; public IntPtr PebBaseAddress; public IntPtr Reserved2_0; public IntPtr Reserved2_1; public IntPtr UniqueProcessId; public IntPtr InheritedFromUniqueProcessId; }

        public static int GetParentProcessId(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(QueryLimitedInformation, false, pid);
                if (h == IntPtr.Zero) return -1;
                var pbi = new PROCESS_BASIC_INFORMATION();
                int retLen;
                int status = NtQueryInformationProcess(h, 0, ref pbi, System.Runtime.InteropServices.Marshal.SizeOf(pbi), out retLen);
                if (status != 0) return -1;
                return pbi.InheritedFromUniqueProcessId.ToInt32();
            }
            catch { return -1; }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }

        // Command line via WMI is simpler and reliable
        public static string GetCommandLine(int pid)
        {
            // WMI (System.Management) is not always available in Unity. Fallback: return the executable path.
            try { return GetExecutablePath(pid); } catch { return "<unknown>"; }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

        public static string GetExecutablePath(int pid)
        {
            IntPtr h = IntPtr.Zero;
            try
            {
                h = OpenProcess(QueryLimitedInformation, false, pid);
                if (h == IntPtr.Zero) return "<no access>";
                var sb = new System.Text.StringBuilder(32768);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size))
                    return sb.ToString();
                return "<unknown>";
            }
            catch { return "<unknown>"; }
            finally { if (h != IntPtr.Zero) CloseHandle(h); }
        }
    }
}
#endif
