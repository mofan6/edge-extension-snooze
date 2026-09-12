using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace EdgeReminder {
    internal sealed class Snapshot {
        public string Path, Text; public byte[] Bytes; public bool Bom;
        public JNode Root;
        public static Snapshot Read(string path) {
            FileInfo info = new FileInfo(path);
            if (!info.Exists) throw new FileNotFoundException("找不到 Preferences 文件。", path);
            if (info.Length > 64 * 1024 * 1024) throw new IOException("配置超过 64 MB，已停止自动修改。");
            byte[] bytes = File.ReadAllBytes(path); bool bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
            string text = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
            JNode root = Json.Parse(text); if (root.Kind != '{') throw new FormatException("Preferences 必须是 JSON 对象。");
            return new Snapshot { Path = System.IO.Path.GetFullPath(path), Bytes = bytes, Text = text, Bom = bom, Root = root };
        }
        public byte[] Encode(string text) {
            byte[] body = new UTF8Encoding(false, true).GetBytes(text); if (!Bom) return body;
            return new byte[] { 239, 187, 191 }.Concat(body).ToArray();
        }
    }
    public sealed class ChangeRecord {
        public int Schema = 1; public string ProfilePath; public bool OldPresent;
        public string OldRaw, WrittenValue, AppliedUtc, BackupFile; public bool Restored, HadExtensions, HadUi;
    }
    internal sealed class Plan {
        public Snapshot Before; public byte[] After; public ChangeRecord Record;
    }
    internal static class Engine {
        public static readonly string[] Key = { "extensions", "ui", "dev_mode_warning_snooze_end_time" };
        public static readonly DateTime Epoch = new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public static readonly string BackupRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EdgeReminder", "Backups");
        public static string TimeValue(DateTime now, int weeks) {
            if (weeks < 1 || weeks > 1000) throw new ArgumentOutOfRangeException("weeks", "周数必须在 1～1000 之间。");
            return ((now.ToUniversalTime().AddDays(weeks * 7) - Epoch).Ticks / 10).ToString(CultureInfo.InvariantCulture);
        }
        public static string Raw(Snapshot s) { JNode n = Json.At(s.Root, Key); return n == null ? null : s.Text.Substring(n.Start, n.End - n.Start); }
        public static string Describe(Snapshot s) {
            JNode n = Json.At(s.Root, Key); if (n == null) return "尚未设置";
            long value; if (n.Kind != '"' || !long.TryParse(n.Text, out value)) return "现有值格式特殊";
            try { return Epoch.AddTicks(checked(value * 10)).ToLocalTime().ToString("yyyy-MM-dd HH:mm"); } catch { return "现有时间超出范围"; }
        }
        public static Plan Prepare(string path, int weeks, DateTime now) {
            Snapshot s = Snapshot.Read(path); string value = TimeValue(now, weeks); string old = Raw(s);
            return new Plan { Before = s, After = s.Encode(Json.Set(s.Text, Key, Json.Quote(value))), Record = new ChangeRecord {
                ProfilePath = s.Path, OldPresent = old != null, OldRaw = old, WrittenValue = value, AppliedUtc = now.ToUniversalTime().ToString("o"),
                HadExtensions = Json.At(s.Root, "extensions") != null, HadUi = Json.At(s.Root, "extensions", "ui") != null
            }};
        }
        public static string Hash(byte[] bytes) { using (SHA256 hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant(); }
        public static string RecordFolder(string path, string backupRoot) { return Path.Combine(backupRoot, Hash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())).Substring(0, 24)); }
        public static void RequireStopped() {
            List<Process> processes = Processes(); int count = processes.Count; foreach (Process p in processes) p.Dispose();
            if (count != 0) throw new IOException("Edge 仍在运行。请保存浏览内容，使用“关闭 Edge”后再试。");
        }
        public static List<Process> Processes() {
            int session = Process.GetCurrentProcess().SessionId; List<Process> result = new List<Process>();
            foreach (Process p in Process.GetProcessesByName("msedge")) {
                try { if (p.SessionId == session && !p.HasExited) { result.Add(p); continue; } } catch { }
                p.Dispose();
            } return result;
        }
        static void CheckUnchanged(Snapshot before) {
            if (!File.ReadAllBytes(before.Path).SequenceEqual(before.Bytes)) throw new IOException("配置刚刚被其他程序修改，已停止写入。请刷新后重试。");
        }
        static void Replace(Snapshot before, byte[] after, Action guard) {
            guard(); CheckUnchanged(before);
            if ((File.GetAttributes(before.Path) & FileAttributes.ReadOnly) != 0) throw new IOException("Preferences 是只读文件，未修改。");
            string temp = before.Path + ".edge-reminder-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { stream.Write(after, 0, after.Length); stream.Flush(true); }
                guard(); CheckUnchanged(before);
                File.Replace(temp, before.Path, null);
                if (!File.ReadAllBytes(before.Path).SequenceEqual(after)) throw new IOException("文件已写入，但读回验证未通过。请停止使用并检查备份。");
            } finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static string Commit(Plan plan, string backupRoot, Action guard) {
            guard(); CheckUnchanged(plan.Before);
            string folder = RecordFolder(plan.Before.Path, backupRoot); Directory.CreateDirectory(folder);
            string id = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string backup = Path.Combine(folder, id + ".preferences.bak"), recordPath = Path.Combine(folder, id + ".json");
            File.WriteAllBytes(backup, plan.Before.Bytes); plan.Record.BackupFile = Path.GetFileName(backup);
            // Save recovery metadata before touching the live file.
            File.WriteAllText(recordPath, new JavaScriptSerializer().Serialize(plan.Record), new UTF8Encoding(false));
            try { Replace(plan.Before, plan.After, guard); }
            catch {
                // Keep the backup, but don't offer a failed, unchanged write as an undo step.
                try { if (File.ReadAllBytes(plan.Before.Path).SequenceEqual(plan.Before.Bytes)) {
                    plan.Record.Restored = true; File.WriteAllText(recordPath, new JavaScriptSerializer().Serialize(plan.Record), new UTF8Encoding(false));
                }} catch { }
                throw;
            }
            return recordPath;
        }
        public static string LatestRecord(string profilePath, string backupRoot) {
            string folder = RecordFolder(profilePath, backupRoot); if (!Directory.Exists(folder)) return null;
            foreach (string file in Directory.GetFiles(folder, "*.json").OrderByDescending(delegate(string x) { return x; })) {
                try { ChangeRecord r = ReadRecord(file); if (!r.Restored && string.Equals(Path.GetFullPath(r.ProfilePath), Path.GetFullPath(profilePath), StringComparison.OrdinalIgnoreCase)) return file; } catch { }
            } return null;
        }
        public static ChangeRecord ReadRecord(string file) {
            ChangeRecord r = new JavaScriptSerializer().Deserialize<ChangeRecord>(File.ReadAllText(file, Encoding.UTF8));
            if (r == null || r.Schema != 1 || string.IsNullOrEmpty(r.ProfilePath) || string.IsNullOrEmpty(r.WrittenValue)) throw new FormatException("备份记录无效。"); return r;
        }
        public static void Restore(string recordPath, string expectedProfile, Action guard) {
            ChangeRecord r = ReadRecord(recordPath);
            if (r.Restored || !string.Equals(Path.GetFullPath(r.ProfilePath), Path.GetFullPath(expectedProfile), StringComparison.OrdinalIgnoreCase)) throw new IOException("恢复记录与所选配置不匹配。");
            Snapshot current = Snapshot.Read(expectedProfile); JNode n = Json.At(current.Root, Key);
            if (n == null || n.Kind != '"' || n.Text != r.WrittenValue) throw new IOException("提醒时间已被再次修改，未覆盖新的值。请先确认当前配置。");
            string changed = r.OldPresent ? Json.Set(current.Text, Key, r.OldRaw) : Json.Remove(current.Text, Key);
            if (!r.HadUi) {
                JNode ui = Json.At(Json.Parse(changed), "extensions", "ui");
                if (ui != null && ui.Kind == '{' && ui.Members.Count == 0) changed = Json.Remove(changed, new[] { "extensions", "ui" });
            }
            if (!r.HadExtensions) {
                JNode ext = Json.At(Json.Parse(changed), "extensions");
                if (ext != null && ext.Kind == '{' && ext.Members.Count == 0) changed = Json.Remove(changed, new[] { "extensions" });
            }
            Replace(current, current.Encode(changed), guard);
            r.Restored = true; File.WriteAllText(recordPath, new JavaScriptSerializer().Serialize(r), new UTF8Encoding(false));
        }
    }
}
