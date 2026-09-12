using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace EdgeReminder {
    internal sealed class Profile {
        public string Name, Channel, Path, Reminder, Note;
    }
    internal static class Discovery {
        [DllImport("shell32.dll", SetLastError = true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
        public static string UserDataArgument(string command) {
            if (string.IsNullOrWhiteSpace(command)) return null;
            int count; IntPtr ptr = CommandLineToArgvW(command, out count); if (ptr == IntPtr.Zero) return null;
            try {
                List<string> args = new List<string>(); for (int i = 0; i < count; i++) args.Add(Marshal.PtrToStringUni(Marshal.ReadIntPtr(ptr, i * IntPtr.Size)));
                for (int i = 0; i < args.Count; i++) {
                    if (args[i].StartsWith("--user-data-dir=", StringComparison.OrdinalIgnoreCase)) return args[i].Substring(16);
                    if (string.Equals(args[i], "--user-data-dir", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count) return args[i + 1];
                } return null;
            } finally { LocalFree(ptr); }
        }
        public static string ExpandPolicy(string value) {
            Dictionary<string, string> map = new Dictionary<string, string> {
                {"user_name", Environment.UserName}, {"machine_name", Environment.MachineName},
                {"documents", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)},
                {"local_app_data", Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)},
                {"roaming_app_data", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)},
                {"profile", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)},
                {"global_app_data", Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)},
                {"program_files", Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)},
                {"windows", Environment.GetFolderPath(Environment.SpecialFolder.Windows)},
                {"client_name", Environment.GetEnvironmentVariable("CLIENTNAME") ?? ""},
                {"session_name", Environment.GetEnvironmentVariable("SESSIONNAME") ?? ""}
            };
            string result = Regex.Replace(value, @"\$\{([^}]+)\}", delegate(Match m) { return map.ContainsKey(m.Groups[1].Value) ? map[m.Groups[1].Value] : m.Value; });
            return Environment.ExpandEnvironmentVariables(result);
        }
        public static List<Profile> Scan(IEnumerable<string> manual) {
            Dictionary<string, string> roots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            roots[System.IO.Path.Combine(local, "Microsoft", "Edge", "User Data")] = "稳定版";
            roots[System.IO.Path.Combine(local, "Microsoft", "Edge Beta", "User Data")] = "Beta";
            roots[System.IO.Path.Combine(local, "Microsoft", "Edge Dev", "User Data")] = "Dev";
            roots[System.IO.Path.Combine(local, "Microsoft", "Edge SxS", "User Data")] = "Canary";
            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine }) {
                foreach (RegistryView view in new[] { RegistryView.Registry32, RegistryView.Registry64 }) try {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, view)) using (RegistryKey policy = root.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Edge")) {
                        if (policy != null) { string custom = policy.GetValue("UserDataDir") as string; if (!string.IsNullOrWhiteSpace(custom)) roots[ExpandPolicy(custom)] = "策略目录"; }
                    }
                } catch { }
            }
            try {
                int session = Process.GetCurrentProcess().SessionId;
                EnumerationOptions opts = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(4), ReturnImmediately = true };
                using (ManagementObjectSearcher query = new ManagementObjectSearcher("root\\CIMV2", "SELECT CommandLine FROM Win32_Process WHERE Name='msedge.exe' AND SessionId=" + session, opts)) {
                    foreach (ManagementObject process in query.Get()) using (process) {
                        string custom = UserDataArgument(process["CommandLine"] as string);
                        if (!string.IsNullOrWhiteSpace(custom) && !roots.ContainsKey(custom)) roots[custom] = "自定义目录";
                    }
                }
            } catch { }
            foreach (string path in manual) roots[path] = "手动添加";
            Dictionary<string, Profile> profiles = new Dictionary<string, Profile>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> root in roots) try {
                foreach (Profile p in InRoot(root.Key, root.Value)) profiles[p.Path] = p;
            } catch { }
            return profiles.Values.OrderBy(delegate(Profile p) { return p.Channel == "稳定版" ? "0" : p.Channel; }).ThenBy(delegate(Profile p) { return p.Name; }).ToList();
        }
        public static List<Profile> InRoot(string supplied, string channel) {
            string path = System.IO.Path.GetFullPath(supplied); List<string> files = new List<string>();
            if (File.Exists(path)) {
                if (!string.Equals(System.IO.Path.GetFileName(path), "Preferences", StringComparison.OrdinalIgnoreCase)) throw new IOException("请选择 Edge 的 Preferences 文件。");
                files.Add(path);
            } else if (Directory.Exists(path)) {
                string direct = System.IO.Path.Combine(path, "Preferences");
                if (File.Exists(direct)) files.Add(direct);
                else {
                    string childRoot = System.IO.Path.Combine(path, "User Data");
                    if (Directory.Exists(childRoot)) path = childRoot;
                    foreach (string folder in Directory.GetDirectories(path)) {
                        string pref = System.IO.Path.Combine(folder, "Preferences"); if (File.Exists(pref)) files.Add(pref);
                    }
                }
            }
            List<Profile> result = new List<Profile>(); foreach (string file in files) {
                string dir = System.IO.Path.GetDirectoryName(file), leaf = System.IO.Path.GetFileName(dir);
                Profile profile = new Profile { Path = file, Channel = channel, Name = leaf, Note = "" };
                try {
                    Snapshot s = Snapshot.Read(file); JNode n = Json.At(s.Root, "profile", "name");
                    if (n != null && n.Kind == '"' && !string.IsNullOrWhiteSpace(n.Text)) profile.Name = n.Text + " · " + leaf;
                    profile.Reminder = Engine.Describe(s);
                    string secure = System.IO.Path.Combine(dir, "Secure Preferences");
                    if (File.Exists(secure)) {
                        Snapshot sp = Snapshot.Read(secure); JNode dev = Json.At(sp.Root, "extensions", "ui", "developer_mode");
                        if (dev != null && dev.Text == "true") profile.Note = "开发者模式已开启";
                    }
                } catch (Exception ex) { profile.Reminder = "读取失败"; profile.Note = ex.Message; }
                result.Add(profile);
            } return result;
        }
    }
}
