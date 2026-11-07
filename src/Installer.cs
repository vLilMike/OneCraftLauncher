using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace OneCraft
{
    /// <summary>Everything the launcher does to get a player ready: NeoForge, Java, mods, the launcher profile.</summary>
    public class Installer
    {
        public const string DefaultManifest = "https://raw.githubusercontent.com/vLilMike/OneCraftLauncher/main/manifest.json";
        public const string ProfileKey = "onecraft";
        const string StorePackage = "Microsoft.4297127D64EC6_8wekyb3d8bbwe";

        public Action<string> Status = delegate { };
        public Action<double> Progress = delegate { };

        public static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        public static readonly string LocalData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        public static readonly string Minecraft = Path.Combine(AppData, ".minecraft");
        public static readonly string GameDir = Path.Combine(AppData, ".onecraft");
        public static readonly string Data = Path.Combine(GameDir, "launcher");

        public static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        static Installer()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; // TLS 1.2
        }

        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Data);
                File.AppendAllText(Path.Combine(Data, "launcher.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch { }
        }

        // ---------------------------------------------------------------- manifest

        public static Dictionary<string, object> LoadManifest(string source)
        {
            string cache = Path.Combine(Data, "manifest.json");
            try
            {
                string text = source.StartsWith("http") ? FetchText(source + (source.Contains("?") ? "&" : "?") + "t=" + DateTime.UtcNow.Ticks) : File.ReadAllText(source);
                var m = Json.Deserialize<Dictionary<string, object>>(text);
                Directory.CreateDirectory(Data);
                File.WriteAllText(cache, text);
                return m;
            }
            catch (Exception e)
            {
                Log("Manifest fetch failed: " + e.Message);
                if (File.Exists(cache)) return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(cache));
                throw new Exception("Couldn't reach the One Craft update server. Check your internet and try again.");
            }
        }

        public static string Str(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            return d != null && d.TryGetValue(key, out v) && v != null ? v.ToString() : fallback;
        }

        public static List<Dictionary<string, object>> List(Dictionary<string, object> d, string key)
        {
            var o = new List<Dictionary<string, object>>();
            object v;
            if (d != null && d.TryGetValue(key, out v) && v is IEnumerable)
                foreach (var e in (IEnumerable)v)
                    if (e is Dictionary<string, object>) o.Add((Dictionary<string, object>)e);
            return o;
        }

        // ---------------------------------------------------------------- web

        static HttpWebRequest Request(string url)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.UserAgent = "OneCraftLauncher/" + LauncherForm.Version;
            r.AllowAutoRedirect = true;
            r.Timeout = 30000;
            r.ReadWriteTimeout = 60000;
            return r;
        }

        public static string FetchText(string url)
        {
            using (var res = Request(url).GetResponse())
            using (var s = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return s.ReadToEnd();
        }

        public void Download(string url, string target, string label)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            string part = target + ".part";
            Status(label);
            Log("Download " + url);
            using (var res = Request(url).GetResponse())
            using (var input = res.GetResponseStream())
            using (var output = File.Create(part))
            {
                long total = res.ContentLength, done = 0;
                var buf = new byte[81920];
                int n;
                while ((n = input.Read(buf, 0, buf.Length)) > 0)
                {
                    output.Write(buf, 0, n);
                    done += n;
                    if (total > 0) Progress((double)done / total);
                }
            }
            if (File.Exists(target)) File.Delete(target);
            File.Move(part, target);
        }

        public static string Sha1(string file)
        {
            using (var sha = SHA1.Create())
            using (var s = File.OpenRead(file))
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(s)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ---------------------------------------------------------------- the whole install

        public void Run(Dictionary<string, object> manifest, int memoryGb)
        {
            string neo = Str(manifest, "neoforge", "21.1.265");
            Directory.CreateDirectory(GameDir);
            Directory.CreateDirectory(Path.Combine(GameDir, "mods"));
            EnsureLauncherFiles();

            if (!NeoForgeInstalled(neo)) InstallNeoForge(neo);
            SyncMods(manifest);
            WriteServers(manifest);
            WriteProfile(manifest, neo, memoryGb);
            Progress(1);
        }

        /// <summary>Repair: forget NeoForge and every managed mod so the next run fetches them fresh.</summary>
        public static void CleanForRepair(Dictionary<string, object> manifest)
        {
            string id = VersionId(Str(manifest, "neoforge", "21.1.265"));
            string dir = Path.Combine(Minecraft, "versions", id);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            foreach (var mod in List(manifest, "mods"))
            {
                string f = Path.Combine(GameDir, "mods", Str(mod, "file", "?"));
                if (File.Exists(f)) File.Delete(f);
            }
        }

        void EnsureLauncherFiles()
        {
            Directory.CreateDirectory(Minecraft);
            string profiles = Path.Combine(Minecraft, "launcher_profiles.json");
            if (!File.Exists(profiles)) File.WriteAllText(profiles, "{\"profiles\":{},\"settings\":{},\"version\":3}");
        }

        public static string VersionId(string neo)
        {
            return "neoforge-" + neo;
        }

        public static bool NeoForgeInstalled(string neo)
        {
            string id = VersionId(neo);
            return File.Exists(Path.Combine(Minecraft, "versions", id, id + ".json"));
        }

        void InstallNeoForge(string neo)
        {
            string java = FindJava();
            string jar = Path.Combine(Data, "cache", "neoforge-" + neo + "-installer.jar");
            Download("https://maven.neoforged.net/releases/net/neoforged/neoforge/" + neo + "/neoforge-" + neo + "-installer.jar", jar, "Downloading NeoForge " + neo + "...");
            Status("Installing NeoForge " + neo + " (this takes a minute the first time)...");
            Progress(-1);

            int code = RunJava(java, "-jar \"" + jar + "\" --install-client \"" + Minecraft + "\"");
            if (code != 0 || !NeoForgeInstalled(neo))
            {
                Log("--install-client gave " + code + ", trying --installClient");
                code = RunJava(java, "-jar \"" + jar + "\" --installClient \"" + Minecraft + "\"");
            }
            if (!NeoForgeInstalled(neo))
                throw new Exception("NeoForge didn't install (code " + code + "). Details are in " + Path.Combine(Data, "launcher.log"));
        }

        int RunJava(string java, string args)
        {
            Log("Run " + java + " " + args);
            var psi = new ProcessStartInfo(java, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.Combine(Data, "cache")
            };
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (s, e) => { if (e.Data != null) Log("  " + e.Data); };
                p.ErrorDataReceived += (s, e) => { if (e.Data != null) Log("  ! " + e.Data); };
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        // ---------------------------------------------------------------- java

        string FindJava()
        {
            var tries = new List<string>
            {
                Path.Combine(Minecraft, @"runtime\java-runtime-delta\windows-x64\java-runtime-delta\bin\java.exe"),
                Path.Combine(LocalData, @"Packages\" + StorePackage + @"\LocalCache\Local\runtime\java-runtime-delta\windows-x64\java-runtime-delta\bin\java.exe")
            };
            string own = Path.Combine(Data, "java");
            if (Directory.Exists(own))
                foreach (var d in Directory.GetDirectories(own)) tries.Add(Path.Combine(d, @"bin\java.exe"));
            foreach (var t in tries) if (File.Exists(t)) return t;

            string zip = Path.Combine(Data, "cache", "java21.zip");
            Download("https://api.adoptium.net/v3/binary/latest/21/ga/windows/x64/jre/hotspot/normal/eclipse", zip, "Downloading Java 21 (one time only)...");
            Status("Unpacking Java...");
            Progress(-1);
            if (Directory.Exists(own)) Directory.Delete(own, true);
            ZipFile.ExtractToDirectory(zip, own);
            File.Delete(zip);
            foreach (var d in Directory.GetDirectories(own))
            {
                string j = Path.Combine(d, @"bin\java.exe");
                if (File.Exists(j)) return j;
            }
            throw new Exception("Java couldn't be set up. Details are in " + Path.Combine(Data, "launcher.log"));
        }

        // ---------------------------------------------------------------- mods

        void SyncMods(Dictionary<string, object> manifest)
        {
            string dir = Path.Combine(GameDir, "mods");
            string trackFile = Path.Combine(Data, "installed-mods.json");
            var tracked = File.Exists(trackFile) ? Json.Deserialize<List<string>>(File.ReadAllText(trackFile)) : new List<string>();
            var wanted = new List<string>();

            var mods = List(manifest, "mods");
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                string file = Str(mod, "file", null), url = Str(mod, "url", null), sha = Str(mod, "sha1", "").ToLowerInvariant();
                string name = Str(mod, "name", file);
                if (file == null || url == null) continue;
                wanted.Add(file);
                string target = Path.Combine(dir, file);
                if (File.Exists(target) && (sha.Length == 0 || Sha1(target) == sha)) continue;
                Download(url, target, "Downloading " + name + " (" + (i + 1) + "/" + mods.Count + ")...");
                if (sha.Length > 0 && Sha1(target) != sha)
                {
                    File.Delete(target);
                    throw new Exception(name + " downloaded wrong. Press Play to try again.");
                }
            }

            // Old versions we put there before; mods the player added themselves are left alone.
            foreach (var old in tracked)
                if (!wanted.Contains(old))
                {
                    string f = Path.Combine(dir, old);
                    if (File.Exists(f)) { File.Delete(f); Log("Removed old mod " + old); }
                }
            File.WriteAllText(trackFile, Json.Serialize(wanted));
        }

        // ---------------------------------------------------------------- multiplayer list

        void WriteServers(Dictionary<string, object> manifest)
        {
            var servers = List(manifest, "servers");
            string file = Path.Combine(GameDir, "servers.dat");
            if (servers.Count == 0 || File.Exists(file)) return;
            using (var w = new BinaryWriter(File.Create(file)))
            {
                w.Write((byte)10); NbtString(w, "");
                w.Write((byte)9); NbtString(w, "servers");
                w.Write((byte)10); NbtInt(w, servers.Count);
                foreach (var s in servers)
                {
                    w.Write((byte)8); NbtString(w, "name"); NbtString(w, Str(s, "name", "One Craft"));
                    w.Write((byte)8); NbtString(w, "ip"); NbtString(w, Str(s, "ip", ""));
                    w.Write((byte)0);
                }
                w.Write((byte)0);
            }
        }

        static void NbtString(BinaryWriter w, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            w.Write((byte)(b.Length >> 8)); w.Write((byte)b.Length); w.Write(b);
        }

        static void NbtInt(BinaryWriter w, int v)
        {
            w.Write((byte)(v >> 24)); w.Write((byte)(v >> 16)); w.Write((byte)(v >> 8)); w.Write((byte)v);
        }

        // ---------------------------------------------------------------- launcher profile

        void WriteProfile(Dictionary<string, object> manifest, string neo, int memoryGb)
        {
            Status("Adding One Craft to the Minecraft Launcher...");
            string file = Path.Combine(Minecraft, "launcher_profiles.json");
            var root = Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(file)) ?? new Dictionary<string, object>();
            object p;
            var profiles = root.TryGetValue("profiles", out p) && p is Dictionary<string, object> ? (Dictionary<string, object>)p : new Dictionary<string, object>();
            root["profiles"] = profiles;

            string now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            object existing;
            var profile = profiles.TryGetValue(ProfileKey, out existing) && existing is Dictionary<string, object> ? (Dictionary<string, object>)existing : new Dictionary<string, object>();
            if (!profile.ContainsKey("created")) profile["created"] = now;
            profile["name"] = Str(manifest, "name", "One Craft");
            profile["type"] = "custom";
            profile["lastVersionId"] = VersionId(neo);
            profile["gameDir"] = GameDir;
            profile["lastUsed"] = now;
            profile["javaArgs"] = "-Xmx" + memoryGb + "G -Xms2G -XX:+UseG1GC -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20 -XX:MaxGCPauseMillis=50 -XX:G1HeapRegionSize=32M";
            string icon = LauncherForm.IconDataUri();
            if (icon != null) profile["icon"] = icon;
            profiles[ProfileKey] = profile;

            File.Copy(file, file + ".onecraft-backup", true);
            File.WriteAllText(file, Json.Serialize(root));
        }

        // ---------------------------------------------------------------- opening the official launcher

        /// <summary>Returns false when no Minecraft Launcher could be found.</summary>
        public static bool OpenMinecraftLauncher()
        {
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var exes = new[]
            {
                Path.Combine(pf86, @"Minecraft Launcher\MinecraftLauncher.exe"),
                @"C:\XboxGames\Minecraft Launcher\Content\Minecraft.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Minecraft Launcher\MinecraftLauncher.exe")
            };
            foreach (var exe in exes)
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                    return true;
                }
            if (Directory.Exists(Path.Combine(LocalData, @"Packages\" + StorePackage)))
            {
                Process.Start("explorer.exe", "shell:AppsFolder\\" + StorePackage + "!Minecraft");
                return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- self update

        public static int CompareVersions(string a, string b)
        {
            var x = a.Split('.');
            var y = b.Split('.');
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = 0, q = 0;
                if (i < x.Length) int.TryParse(x[i], out p);
                if (i < y.Length) int.TryParse(y[i], out q);
                if (p != q) return p.CompareTo(q);
            }
            return 0;
        }

        /// <summary>Downloads the newer launcher, swaps it in after this one closes and starts it again.</summary>
        public void SelfUpdate(string url)
        {
            string self = Process.GetCurrentProcess().MainModule.FileName;
            string fresh = self + ".new";
            Download(url, fresh, "Updating One Craft Launcher...");
            string script = Path.Combine(Path.GetTempPath(), "onecraft-update.cmd");
            File.WriteAllText(script,
                "@echo off\r\n" +
                "ping 127.0.0.1 -n 3 > nul\r\n" +
                "move /y \"" + fresh + "\" \"" + self + "\" > nul\r\n" +
                "start \"\" \"" + self + "\"\r\n" +
                "del \"%~f0\"\r\n");
            Process.Start(new ProcessStartInfo("cmd.exe", "/c \"" + script + "\"") { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
