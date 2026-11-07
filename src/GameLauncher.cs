using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OneCraft
{
    /// <summary>
    /// Starts Minecraft itself (no official launcher): merges the NeoForge and vanilla version files,
    /// downloads libraries, the client jar and assets into .minecraft, then runs Java with the signed-in account.
    /// </summary>
    public class GameLauncher
    {
        readonly Installer job;
        static readonly string Root = Installer.Minecraft;

        public GameLauncher(Installer job)
        {
            this.job = job;
            ServicePointManager.DefaultConnectionLimit = 24;
        }

        static Dictionary<string, object> ReadJson(string file)
        {
            return Installer.Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file));
        }

        static Dictionary<string, object> Obj(Dictionary<string, object> d, string key)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        static IEnumerable<object> Arr(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is IEnumerable && !(v is string))
                foreach (var e in (IEnumerable)v) yield return e;
        }

        // ---------------------------------------------------------------- rules

        static bool Allowed(Dictionary<string, object> holder)
        {
            object r;
            if (holder == null || !holder.TryGetValue("rules", out r) || !(r is IEnumerable)) return true;
            bool allowed = false;
            foreach (var o in (IEnumerable)r)
            {
                var rule = o as Dictionary<string, object>;
                if (rule == null) continue;
                if (rule.ContainsKey("features")) continue; // no demo mode, custom resolution or quick play
                var os = Obj(rule, "os");
                bool match = true;
                if (os != null)
                {
                    string name = Installer.Str(os, "name", null), arch = Installer.Str(os, "arch", null);
                    if (name != null && name != "windows") match = false;
                    if (arch == "x86" && Environment.Is64BitOperatingSystem) match = false;
                }
                if (match) allowed = Installer.Str(rule, "action", "allow") == "allow";
            }
            return allowed;
        }

        // ---------------------------------------------------------------- version files

        Dictionary<string, object> Vanilla(string id)
        {
            string file = Path.Combine(Root, "versions", id, id + ".json");
            if (!File.Exists(file))
            {
                var index = Installer.Json.Deserialize<Dictionary<string, object>>(Installer.FetchText("https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"));
                string url = null;
                foreach (var v in Installer.List(index, "versions"))
                    if (Installer.Str(v, "id", "") == id) url = Installer.Str(v, "url", null);
                if (url == null) throw new Exception("Minecraft " + id + " wasn't found on Mojang's servers.");
                job.Download(url, file, "Getting Minecraft " + id + "...");
            }
            return ReadJson(file);
        }

        class Lib
        {
            public string Key, Path, Url, Sha1;
        }

        static Lib ToLib(Dictionary<string, object> l)
        {
            if (!Allowed(l) || l.ContainsKey("natives")) return null;
            string name = Installer.Str(l, "name", "");
            var parts = name.Split(':');
            if (parts.Length < 3) return null;
            string classifier = parts.Length > 3 ? parts[3] : null;
            var art = Obj(Obj(l, "downloads"), "artifact");
            string path = art != null ? Installer.Str(art, "path", null) : null;
            if (path == null)
            {
                string ext = "jar";
                if (classifier != null && classifier.Contains("@")) { ext = classifier.Split('@')[1]; classifier = classifier.Split('@')[0]; }
                path = parts[0].Replace('.', '/') + "/" + parts[1] + "/" + parts[2] + "/" + parts[1] + "-" + parts[2] + (classifier != null ? "-" + classifier : "") + "." + ext;
            }
            string url = art != null ? Installer.Str(art, "url", "") : Installer.Str(l, "url", "https://libraries.minecraft.net/").TrimEnd('/') + "/" + path;
            return new Lib
            {
                Key = parts[0] + ":" + parts[1] + (classifier != null ? ":" + classifier : ""),
                Path = Path.Combine(Root, "libraries", path.Replace('/', '\\')),
                Url = url,
                Sha1 = art != null ? Installer.Str(art, "sha1", "") : ""
            };
        }

        // ---------------------------------------------------------------- downloads

        void Fetch(List<KeyValuePair<string, string>> files, string label)
        {
            if (files.Count == 0) return;
            int done = 0;
            job.Status(label + " (0/" + files.Count + ")");
            Exception failure = null;
            Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = 12 }, f =>
            {
                if (failure != null) return;
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(f.Value));
                        using (var wc = new WebClient())
                        {
                            wc.Headers[HttpRequestHeader.UserAgent] = "OneCraftLauncher/" + LauncherForm.Version;
                            wc.DownloadFile(f.Key, f.Value + ".part");
                        }
                        if (File.Exists(f.Value)) File.Delete(f.Value);
                        File.Move(f.Value + ".part", f.Value);
                        break;
                    }
                    catch (Exception e)
                    {
                        if (attempt >= 2) { failure = new Exception("Couldn't download " + Path.GetFileName(f.Value) + ": " + e.Message); return; }
                        Thread.Sleep(800);
                    }
                }
                int n = Interlocked.Increment(ref done);
                if (n % 20 == 0 || n == files.Count)
                {
                    job.Status(label + " (" + n + "/" + files.Count + ")");
                    job.Progress((double)n / files.Count);
                }
            });
            if (failure != null) throw failure;
        }

        // ---------------------------------------------------------------- launch

        /// <summary>Gets every game file in place and starts Minecraft. Returns the running game process.</summary>
        public Process Launch(Dictionary<string, object> manifest, Account account, int memoryGb, string clientId)
        {
            string neo = Installer.Str(manifest, "neoforge", "21.1.265");
            string id = Installer.VersionId(neo);
            var child = ReadJson(Path.Combine(Root, "versions", id, id + ".json"));
            string parentId = Installer.Str(child, "inheritsFrom", Installer.Str(manifest, "minecraft", "1.21.1"));
            var parent = Vanilla(parentId);

            // Libraries: NeoForge's first, so its versions win over vanilla's.
            var libs = new List<Lib>();
            var seen = new HashSet<string>();
            foreach (var src in new[] { child, parent })
                foreach (var o in Arr(src, "libraries"))
                {
                    var l = ToLib(o as Dictionary<string, object>);
                    if (l != null && seen.Add(l.Key)) libs.Add(l);
                }
            var missing = new List<KeyValuePair<string, string>>();
            foreach (var l in libs)
                if (!File.Exists(l.Path) && !string.IsNullOrEmpty(l.Url)) missing.Add(new KeyValuePair<string, string>(l.Url, l.Path));
            Fetch(missing, "Downloading game libraries");
            foreach (var l in libs)
                if (!File.Exists(l.Path)) throw new Exception("A game file is missing: " + Path.GetFileName(l.Path) + ". Try Repair install.");

            // Client jar. Like the official launcher, the NeoForge version gets its own copy (NeoForge ignores it by name).
            string vanillaJar = Path.Combine(Root, "versions", parentId, parentId + ".jar");
            var client = Obj(Obj(parent, "downloads"), "client");
            if (!File.Exists(vanillaJar) || (client != null && new FileInfo(vanillaJar).Length.ToString() != Installer.Str(client, "size", "")))
                job.Download(Installer.Str(client, "url", ""), vanillaJar, "Downloading Minecraft " + parentId + "...");
            string gameJar = Path.Combine(Root, "versions", id, id + ".jar");
            if (!File.Exists(gameJar) || new FileInfo(gameJar).Length != new FileInfo(vanillaJar).Length) File.Copy(vanillaJar, gameJar, true);

            // Assets.
            var assetIndex = Obj(parent, "assetIndex");
            string assetId = Installer.Str(assetIndex, "id", parentId);
            string assetsDir = Path.Combine(Root, "assets");
            string indexFile = Path.Combine(assetsDir, "indexes", assetId + ".json");
            if (!File.Exists(indexFile)) job.Download(Installer.Str(assetIndex, "url", ""), indexFile, "Getting the asset list...");
            var objects = Obj(ReadJson(indexFile), "objects");
            var needAssets = new List<KeyValuePair<string, string>>();
            if (objects != null)
                foreach (var kv in objects)
                {
                    var o = kv.Value as Dictionary<string, object>;
                    string hash = Installer.Str(o, "hash", "");
                    if (hash.Length < 2) continue;
                    string path = Path.Combine(assetsDir, "objects", hash.Substring(0, 2), hash);
                    long size;
                    long.TryParse(Installer.Str(o, "size", "-1"), out size);
                    if (!File.Exists(path) || new FileInfo(path).Length != size)
                        needAssets.Add(new KeyValuePair<string, string>("https://resources.download.minecraft.net/" + hash.Substring(0, 2) + "/" + hash, path));
                }
            Fetch(needAssets, "Downloading sounds and textures");

            // Logging config.
            string logArg = null;
            var log = Obj(Obj(parent, "logging"), "client");
            if (log != null)
            {
                var lf = Obj(log, "file");
                string logFile = Path.Combine(assetsDir, "log_configs", Installer.Str(lf, "id", "client.xml"));
                if (!File.Exists(logFile)) job.Download(Installer.Str(lf, "url", ""), logFile, "Getting log settings...");
                logArg = Installer.Str(log, "argument", "").Replace("${path}", logFile);
            }

            // Java.
            string java = job.FindJava();
            string javaw = Path.Combine(Path.GetDirectoryName(java), "javaw.exe");
            if (File.Exists(javaw)) java = javaw;

            string natives = Path.Combine(Root, "versions", id, "natives");
            Directory.CreateDirectory(natives);
            Directory.CreateDirectory(Installer.GameDir);

            var cp = new List<string>();
            foreach (var l in libs) cp.Add(l.Path);
            cp.Add(gameJar);

            var vars = new Dictionary<string, string>
            {
                { "auth_player_name", account.Name }, { "version_name", id }, { "game_directory", Installer.GameDir },
                { "assets_root", assetsDir }, { "assets_index_name", assetId }, { "auth_uuid", account.Uuid },
                { "auth_access_token", account.AccessToken }, { "clientid", clientId ?? "" }, { "auth_xuid", account.Xuid ?? "" },
                { "user_type", "msa" }, { "version_type", "release" }, { "natives_directory", natives },
                { "launcher_name", "OneCraftLauncher" }, { "launcher_version", LauncherForm.Version },
                { "classpath", string.Join(";", cp) }, { "library_directory", Path.Combine(Root, "libraries") },
                { "classpath_separator", ";" }, { "user_properties", "{}" }
            };

            var args = new List<string>();
            args.Add("-Xmx" + memoryGb + "G");
            args.Add("-Xms2G");
            args.AddRange(Installer.GcArgs.Split(' '));
            if (logArg != null) args.Add(logArg);
            foreach (var src in new[] { parent, child }) args.AddRange(Expand(Obj(src, "arguments"), "jvm", vars));
            args.Add(Installer.Str(child, "mainClass", Installer.Str(parent, "mainClass", "")));
            foreach (var src in new[] { parent, child }) args.AddRange(Expand(Obj(src, "arguments"), "game", vars));

            // Java reads its arguments from a file, which avoids Windows' command-line length limit.
            string argFile = Path.Combine(Installer.Data, "game-args.txt");
            var sb = new StringBuilder();
            foreach (var a in args) sb.Append('"').Append(a.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"\n");
            File.WriteAllText(argFile, sb.ToString(), new UTF8Encoding(false));
            Installer.Log("Launching " + id + " as " + account.Name);

            job.Status("Starting Minecraft...");
            job.Progress(1);
            var psi = new ProcessStartInfo(java, "\"@" + argFile + "\"")
            {
                UseShellExecute = false,
                WorkingDirectory = Installer.GameDir
            };
            var p = Process.Start(psi);
            // The access token is in that file; remove it once Java has read it.
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(15000); try { File.Delete(argFile); } catch { } });
            return p;
        }

        static IEnumerable<string> Expand(Dictionary<string, object> arguments, string kind, Dictionary<string, string> vars)
        {
            foreach (var o in Arr(arguments, kind))
            {
                if (o is string) { yield return Fill((string)o, vars); continue; }
                var d = o as Dictionary<string, object>;
                if (d == null || !Allowed(d)) continue;
                object v;
                if (!d.TryGetValue("value", out v)) continue;
                if (v is string) yield return Fill((string)v, vars);
                else if (v is IEnumerable) foreach (var s in (IEnumerable)v) yield return Fill(s.ToString(), vars);
            }
        }

        static string Fill(string s, Dictionary<string, string> vars)
        {
            foreach (var kv in vars) s = s.Replace("${" + kv.Key + "}", kv.Value);
            return s;
        }
    }
}
