using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EdgeReminder {
    internal static class Tests {
        static int passed; static string root; static readonly Action NoGuard = delegate { };
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
        static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
        static string Fixture(string id, string content) {
            string folder = Path.Combine(root, id); Directory.CreateDirectory(folder); string file = Path.Combine(folder, "Preferences");
            File.WriteAllText(file, content, new UTF8Encoding(false)); return file;
        }
        static string Value(string json, string value) { return Json.Set(json, Engine.Key, Json.Quote(value)); }
        static string Old = "13433588841014165";
        static string Base = "{\"profile\":{\"name\":\"个人资料\"},\"extensions\":{\"ui\":{\"dev_mode_warning_snooze_end_time\":\"13433588841014165\",\"other\":true}},\"big\":900719925474099312345,\"test\":\"中文 \\\" 引号\"}";
        [STAThread] static int Main(string[] args) {
            try {
                root = Path.GetFullPath(args.Length > 0 ? args[0] : "fixtures"); Directory.CreateDirectory(root);
                string backups = Path.Combine(root, "Backups"); DateTime now = new DateTime(2026, 9, 12, 0, 0, 0, DateTimeKind.Utc);
                Test("1 and 1000 weeks, exact microsecond conversion", delegate {
                    foreach (int weeks in new[] { 1, 2, 999, 1000 }) {
                        long actual = long.Parse(Engine.TimeValue(now, weeks)); Check(Engine.Epoch.AddTicks(actual * 10) == now.AddDays(weeks * 7), "date mismatch");
                    }
                });
                Test("0, negative, and 1001 weeks rejected", delegate { foreach (int weeks in new[] { 0, -1, 1001, int.MaxValue }) Throws<ArgumentOutOfRangeException>(delegate { Engine.TimeValue(now, weeks); }); });
                Test("surgical edit preserves huge integers, Unicode and escapes", delegate {
                    string path = Fixture("precise", Base); Plan p = Engine.Prepare(path, 1000, now);
                    Check(Encoding.UTF8.GetString(p.After) == Base.Replace(Old, p.Record.WrittenValue), "unrelated content changed");
                    Engine.Commit(p, backups, NoGuard); Check(Engine.Raw(Snapshot.Read(path)) == Json.Quote(p.Record.WrittenValue), "not applied");
                });
                Test("only exact nested key modified, unrelated same-named key preserved", delegate {
                    string src = "{\"dev_mode_warning_snooze_end_time\":\"123\",\"nested\":{\"dev_mode_warning_snooze_end_time\":\"456\"}}";
                    string changed = Value(src, "789"); JNode n = Json.Parse(changed);
                    Check(n.Get("dev_mode_warning_snooze_end_time").Text == "123", "top key changed"); Check(Json.At(n,"nested","dev_mode_warning_snooze_end_time").Text == "456", "other key changed");
                });
                Test("missing key, ui and extensions can be added and removed", delegate {
                    int i = 0; foreach (string src in new[] { "{}", "{\"extensions\":{}}", "{\"extensions\":{\"ui\":{\"other\":true}}}", "{\"extensions\":{\"ui\":{}}}" }) {
                        string path = Fixture("missing" + i++, src); Plan p = Engine.Prepare(path, 5, now); string rec = Engine.Commit(p, backups, NoGuard);
                        Engine.Restore(rec, path, NoGuard); Check(File.ReadAllText(path) == src, "restore missing branches mismatch");
                    }
                });
                Test("remove first, middle, last and only JSON member", delegate {
                    foreach (string src in new[] { "{\"target\":1,\"b\":2}", "{\"a\":0,\"target\":1,\"b\":2}", "{\"a\":0,\"target\":1}", "{ \"target\":1 }" }) {
                        JNode n = Json.Parse(Json.Remove(src, new[] { "target" })); Check(n.Member("target") == null, "removal failed");
                    }
                });
                Test("invalid JSON, duplicate keys and wrong parent types rejected", delegate {
                    foreach (string src in new[] { "{", "{\"a\":1,}", "{\"a\":1,\"a\":2}", "{\"x\":01}", "{\"x\":NaN}", "{\"extensions\":null}", "{\"extensions\":{\"ui\":[]}}" }) Throws<FormatException>(delegate { Value(src, "123"); });
                });
                Test("UTF-8 BOM preserved; malformed UTF-8 rejected", delegate {
                    string path = Fixture("bom", Base); File.WriteAllText(path, Base, new UTF8Encoding(true)); Plan p = Engine.Prepare(path, 4, now);
                    Check(p.After.Take(3).SequenceEqual(new byte[] {239,187,191}), "BOM lost");
                    File.WriteAllBytes(path, new byte[] {123,255,125}); Throws<DecoderFallbackException>(delegate { Snapshot.Read(path); });
                });
                Test("backup byte-exact; undo preserves subsequent unrelated changes", delegate {
                    string path = Fixture("undo", Base); Plan p = Engine.Prepare(path, 10, now); string rec = Engine.Commit(p, backups, NoGuard);
                    ChangeRecord record = Engine.ReadRecord(rec); Check(File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(rec),record.BackupFile)).SequenceEqual(p.Before.Bytes), "backup mismatch");
                    string updated = Json.Set(File.ReadAllText(path), new[] { "later" }, "{\"theme\":\"dark\"}"); File.WriteAllText(path, updated, new UTF8Encoding(false));
                    Engine.Restore(rec, path, NoGuard); Snapshot s = Snapshot.Read(path); Check(Engine.Raw(s) == Json.Quote(Old), "time not restored"); Check(Json.At(s.Root,"later","theme").Text == "dark", "later setting lost");
                });
                Test("undo detects reminder modified by somebody else", delegate {
                    string path = Fixture("undoConflict", Base); string rec = Engine.Commit(Engine.Prepare(path, 4, now), backups, NoGuard);
                    string changed = Value(File.ReadAllText(path), "12345"); File.WriteAllText(path, changed, new UTF8Encoding(false));
                    Throws<IOException>(delegate { Engine.Restore(rec,path,NoGuard); }); Check(File.ReadAllText(path) == changed,"changed despite conflict");
                });
                Test("undo stack across two consecutive edits", delegate {
                    string path = Fixture("stack", Base); string a = Engine.Commit(Engine.Prepare(path, 4, now), backups, NoGuard);
                    string b = Engine.Commit(Engine.Prepare(path, 8, now), backups, NoGuard); Check(Engine.LatestRecord(path,backups) == b,"latest wrong");
                    Engine.Restore(b,path,NoGuard); Check(Engine.LatestRecord(path,backups) == a,"previous wrong"); Engine.Restore(a,path,NoGuard); Check(File.ReadAllText(path) == Base,"stack restore mismatch");
                });
                Test("stale file or running-browser guard prevents all writes", delegate {
                    string path = Fixture("concurrent",Base); Plan p = Engine.Prepare(path,2,now); File.AppendAllText(path," ");
                    Throws<IOException>(delegate { Engine.Commit(p,backups,NoGuard); }); Check(File.ReadAllText(path) == Base + " ","stale overwritten");
                    Plan fresh = Engine.Prepare(path,2,now); Throws<IOException>(delegate { Engine.Commit(fresh,backups,delegate {throw new IOException("Edge running");}); });
                });
                Test("read-only and locked files fail without truncation", delegate {
                    string path = Fixture("readonly",Base); Plan p = Engine.Prepare(path,2,now); File.SetAttributes(path,FileAttributes.ReadOnly);
                    try { Throws<IOException>(delegate { Engine.Commit(p,backups,NoGuard); }); } finally { File.SetAttributes(path,FileAttributes.Normal); }
                    Check(File.ReadAllText(path)==Base,"readonly changed"); Check(Engine.LatestRecord(path,backups)==null,"failed write became undo entry");
                    using (FileStream locked = new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Throws<IOException>(delegate { Engine.Commit(p,backups,NoGuard); });
                    Check(File.ReadAllText(path)==Base,"locked file changed");
                });
                Test("Chinese, spaced paths; custom profile names and multiple profiles", delegate {
                    string dir = Path.Combine(root,"另一个用户 自定义磁盘","User Data");
                    string first = Fixture(Path.Combine(dir,"Default"),Base); Fixture(Path.Combine(dir,"Work Profile 中文"),"{}");
                    Check(Discovery.InRoot(dir,"custom").Count==2,"profiles missed"); Check(Discovery.InRoot(first,"custom").Count==1,"file missed");
                    Check(Discovery.InRoot(Path.GetDirectoryName(dir),"custom").Count==2,"parent root missed");
                    Engine.Commit(Engine.Prepare(first,1000,now),backups,NoGuard);
                });
                Test("custom --user-data-dir parsing including quoted spaces", delegate {
                    Check(Discovery.UserDataArgument("\"D:\\Other Edge\\msedge.exe\" --user-data-dir=\"E:\\资料 目录\" --profile-directory=Default")=="E:\\资料 目录","equals argument failed");
                    Check(Discovery.UserDataArgument("msedge.exe --user-data-dir \"D:\\Space Path\"")=="D:\\Space Path","separate argument failed");
                    Check(Discovery.UserDataArgument("msedge.exe --no-startup-window")==null,"false argument");
                });
                Test("Edge policy path variables expanded for current user", delegate {
                    string actual = Discovery.ExpandPolicy("${local_app_data}\\Edge Custom\\${user_name}");
                    Check(actual.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),"local path mismatch"); Check(actual.EndsWith(Environment.UserName),"user mismatch");
                });
                Test("Secure Preferences remains byte-identical", delegate {
                    string path = Fixture("secure", Base); string secure = Path.Combine(Path.GetDirectoryName(path), "Secure Preferences");
                    byte[] original = Encoding.UTF8.GetBytes("{\"extensions\":{\"ui\":{\"developer_mode\":true},\"settings\":{\"my_extension\":{\"disable_reasons\":[]}}}}");
                    File.WriteAllBytes(secure,original); string rec=Engine.Commit(Engine.Prepare(path,5,now),backups,NoGuard); Engine.Restore(rec,path,NoGuard);
                    Check(File.ReadAllBytes(secure).SequenceEqual(original),"secure changed");
                });
                using (System.Drawing.Icon icon = MainForm.BuildIcon()) using (FileStream f=File.Create(Path.Combine(root,"app.ico"))) icon.Save(f);
                Console.WriteLine("ALL " + passed + " TEST GROUPS PASSED"); return 0;
            } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
        }
    }
}
