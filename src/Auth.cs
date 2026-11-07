using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace OneCraft
{
    /// <summary>A signed-in Minecraft account: what the game needs to start.</summary>
    public class Account
    {
        public string Name, Uuid, AccessToken, Xuid, RefreshToken;
        public DateTime Expires;
    }

    /// <summary>Thrown when Mojang hasn't approved the launcher's Azure app yet (403 from login_with_xbox).</summary>
    public class NotApprovedException : Exception
    {
        public NotApprovedException() : base("Mojang hasn't approved One Craft Launcher for sign-in yet.") { }
    }

    /// <summary>
    /// Microsoft sign-in for Minecraft: browser login (PKCE, loopback redirect) -> Xbox Live -> XSTS -> Minecraft services.
    /// The refresh token is kept encrypted with Windows DPAPI so only this Windows user can read it.
    /// </summary>
    public static class Auth
    {
        const string Scope = "XboxLive.signin offline_access";
        const string Authority = "https://login.microsoftonline.com/consumers/oauth2/v2.0/";

        static string AccountFile { get { return Path.Combine(Installer.Data, "account.dat"); } }

        // ---------------------------------------------------------------- saved account

        public static Account Load()
        {
            try
            {
                if (!File.Exists(AccountFile)) return null;
                var raw = ProtectedData.Unprotect(File.ReadAllBytes(AccountFile), null, DataProtectionScope.CurrentUser);
                var d = Installer.Json.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(raw));
                return new Account
                {
                    Name = Installer.Str(d, "name", null),
                    Uuid = Installer.Str(d, "uuid", null),
                    Xuid = Installer.Str(d, "xuid", ""),
                    AccessToken = Installer.Str(d, "access", null),
                    RefreshToken = Installer.Str(d, "refresh", null),
                    Expires = new DateTime(long.Parse(Installer.Str(d, "expires", "0")), DateTimeKind.Utc)
                };
            }
            catch (Exception e) { Installer.Log("Couldn't read saved account: " + e.Message); return null; }
        }

        static void Save(Account a)
        {
            var d = new Dictionary<string, object>
            {
                { "name", a.Name }, { "uuid", a.Uuid }, { "xuid", a.Xuid }, { "access", a.AccessToken },
                { "refresh", a.RefreshToken }, { "expires", a.Expires.Ticks.ToString() }
            };
            Directory.CreateDirectory(Installer.Data);
            File.WriteAllBytes(AccountFile, ProtectedData.Protect(Encoding.UTF8.GetBytes(Installer.Json.Serialize(d)), null, DataProtectionScope.CurrentUser));
        }

        public static void SignOut()
        {
            try { if (File.Exists(AccountFile)) File.Delete(AccountFile); } catch { }
        }

        // ---------------------------------------------------------------- sign in

        /// <summary>Opens the browser for a Microsoft login and waits (up to 5 minutes) for the redirect back.</summary>
        public static Account SignIn(string clientId, Action<string> status)
        {
            var listener = new TcpListener(IPAddress.IPv6Any, 0);
            listener.Server.DualMode = true;
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://localhost:" + port;
            string verifier = Random(64), state = Random(24);
            string challenge = Base64Url(SHA256.Create().ComputeHash(Encoding.ASCII.GetBytes(verifier)));
            string url = Authority + "authorize?client_id=" + clientId + "&response_type=code&redirect_uri=" + Uri.EscapeDataString(redirect) +
                         "&scope=" + Uri.EscapeDataString(Scope) + "&code_challenge=" + challenge + "&code_challenge_method=S256&state=" + state + "&prompt=select_account";
            status("Finish signing in in your browser...");
            Process.Start(url);

            string code = null;
            try
            {
                var accept = listener.BeginAcceptTcpClient(null, null);
                if (!accept.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(5))) throw new Exception("Sign-in timed out. Press Sign in to try again.");
                using (var client = listener.EndAcceptTcpClient(accept))
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII);
                    string line = reader.ReadLine() ?? "";
                    // GET /?code=...&state=... HTTP/1.1
                    var query = ParseQuery(line.Split(' ').Length > 1 ? line.Split(' ')[1] : "");
                    string error;
                    query.TryGetValue("error_description", out error);
                    string gotState;
                    if (query.TryGetValue("state", out gotState) && gotState == state) query.TryGetValue("code", out code);
                    string page = code != null
                        ? Page("Signed in!", "You can close this tab and go back to the One Craft Launcher.")
                        : Page("Sign-in didn't finish", error ?? "Go back to the launcher and try again.");
                    var body = Encoding.UTF8.GetBytes(page);
                    var head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    stream.Write(head, 0, head.Length);
                    stream.Write(body, 0, body.Length);
                    if (code == null) throw new Exception("Sign-in was cancelled. " + (error ?? ""));
                }
            }
            finally { listener.Stop(); }

            status("Signing in to Minecraft...");
            var tokens = PostForm(Authority + "token", "client_id=" + clientId + "&grant_type=authorization_code&code=" + Uri.EscapeDataString(code) +
                                                         "&redirect_uri=" + Uri.EscapeDataString(redirect) + "&code_verifier=" + verifier + "&scope=" + Uri.EscapeDataString(Scope));
            return Finish(Installer.Str(tokens, "access_token", null), Installer.Str(tokens, "refresh_token", null));
        }

        /// <summary>Returns a usable account, refreshing the Minecraft token if it has run out. Null if a fresh sign-in is needed.</summary>
        public static Account Refresh(string clientId, Account a)
        {
            if (a == null) return null;
            if (a.Expires > DateTime.UtcNow.AddMinutes(10)) return a;
            if (a.RefreshToken == null) return null;
            try
            {
                var tokens = PostForm(Authority + "token", "client_id=" + clientId + "&grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(a.RefreshToken) + "&scope=" + Uri.EscapeDataString(Scope));
                return Finish(Installer.Str(tokens, "access_token", null), Installer.Str(tokens, "refresh_token", a.RefreshToken));
            }
            catch (NotApprovedException) { throw; }
            catch (Exception e) { Installer.Log("Refresh failed: " + e.Message); return null; }
        }

        static Account Finish(string msToken, string refresh)
        {
            if (msToken == null) throw new Exception("Microsoft didn't return a sign-in token.");

            var xbl = PostJson("https://user.auth.xboxlive.com/user/authenticate",
                "{\"Properties\":{\"AuthMethod\":\"RPS\",\"SiteName\":\"user.auth.xboxlive.com\",\"RpsTicket\":\"d=" + msToken + "\"},\"RelyingParty\":\"http://auth.xboxlive.com\",\"TokenType\":\"JWT\"}", null);
            string xblToken = Installer.Str(xbl, "Token", null);

            Dictionary<string, object> xsts;
            try
            {
                xsts = PostJson("https://xsts.auth.xboxlive.com/xsts/authorize",
                    "{\"Properties\":{\"SandboxId\":\"RETAIL\",\"UserTokens\":[\"" + xblToken + "\"]},\"RelyingParty\":\"rp://api.minecraftservices.com/\",\"TokenType\":\"JWT\"}", null);
            }
            catch (WebException e)
            {
                string body = Body(e);
                if (body.Contains("2148916233")) throw new Exception("This Microsoft account has no Xbox profile yet. Sign in once at xbox.com, then try again.");
                if (body.Contains("2148916238")) throw new Exception("This is a child account. An adult needs to add it to a Microsoft Family group first.");
                if (body.Contains("2148916235")) throw new Exception("Xbox Live isn't available in your country.");
                throw;
            }
            string xstsToken = Installer.Str(xsts, "Token", null);
            string uhs = "", xuid = "";
            object claims;
            if (xsts.TryGetValue("DisplayClaims", out claims))
            {
                var xui = Installer.List((Dictionary<string, object>)claims, "xui");
                if (xui.Count > 0) { uhs = Installer.Str(xui[0], "uhs", ""); xuid = Installer.Str(xui[0], "xid", ""); }
            }

            Dictionary<string, object> mc;
            try
            {
                mc = PostJson("https://api.minecraftservices.com/authentication/login_with_xbox", "{\"identityToken\":\"XBL3.0 x=" + uhs + ";" + xstsToken + "\"}", null);
            }
            catch (WebException e)
            {
                var res = e.Response as HttpWebResponse;
                if (res != null && res.StatusCode == HttpStatusCode.Forbidden) throw new NotApprovedException();
                throw;
            }
            string mcToken = Installer.Str(mc, "access_token", null);
            int seconds;
            int.TryParse(Installer.Str(mc, "expires_in", "86400"), out seconds);

            Dictionary<string, object> profile;
            try { profile = GetJson("https://api.minecraftservices.com/minecraft/profile", mcToken); }
            catch (WebException e)
            {
                var res = e.Response as HttpWebResponse;
                if (res != null && res.StatusCode == HttpStatusCode.NotFound) throw new Exception("This Microsoft account doesn't own Minecraft: Java Edition.");
                throw;
            }

            var a = new Account
            {
                Name = Installer.Str(profile, "name", "Player"),
                Uuid = Installer.Str(profile, "id", ""),
                Xuid = xuid,
                AccessToken = mcToken,
                RefreshToken = refresh,
                Expires = DateTime.UtcNow.AddSeconds(seconds)
            };
            Save(a);
            Installer.Log("Signed in as " + a.Name);
            return a;
        }

        // ---------------------------------------------------------------- http helpers

        static HttpWebRequest Make(string url, string method, string bearer)
        {
            var r = (HttpWebRequest)WebRequest.Create(url);
            r.Method = method;
            r.UserAgent = "OneCraftLauncher/" + LauncherForm.Version;
            r.Accept = "application/json";
            r.Timeout = 30000;
            if (bearer != null) r.Headers["Authorization"] = "Bearer " + bearer;
            return r;
        }

        static Dictionary<string, object> Send(HttpWebRequest r, string body, string type)
        {
            if (body != null)
            {
                var data = Encoding.UTF8.GetBytes(body);
                r.ContentType = type;
                r.ContentLength = data.Length;
                using (var s = r.GetRequestStream()) s.Write(data, 0, data.Length);
            }
            using (var res = r.GetResponse())
            using (var reader = new StreamReader(res.GetResponseStream(), Encoding.UTF8))
                return Installer.Json.Deserialize<Dictionary<string, object>>(reader.ReadToEnd());
        }

        static Dictionary<string, object> PostForm(string url, string form)
        {
            try { return Send(Make(url, "POST", null), form, "application/x-www-form-urlencoded"); }
            catch (WebException e)
            {
                string body = Body(e);
                Installer.Log("Token request failed: " + body);
                if (body.Contains("invalid_grant")) throw new Exception("Your sign-in ran out. Please sign in again.");
                throw new Exception("Microsoft sign-in failed. Details are in the launcher log.");
            }
        }

        static Dictionary<string, object> PostJson(string url, string json, string bearer)
        {
            return Send(Make(url, "POST", bearer), json, "application/json");
        }

        static Dictionary<string, object> GetJson(string url, string bearer)
        {
            return Send(Make(url, "GET", bearer), null, null);
        }

        static string Body(WebException e)
        {
            try
            {
                if (e.Response == null) return e.Message;
                using (var r = new StreamReader(e.Response.GetResponseStream())) return r.ReadToEnd();
            }
            catch { return e.Message; }
        }

        static Dictionary<string, string> ParseQuery(string path)
        {
            var d = new Dictionary<string, string>();
            int q = path.IndexOf('?');
            if (q < 0) return d;
            foreach (var pair in path.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) d[pair.Substring(0, eq)] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return d;
        }

        static string Random(int bytes)
        {
            var b = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            return Base64Url(b);
        }

        static string Base64Url(byte[] b)
        {
            return Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        static string Page(string title, string text)
        {
            return "<!doctype html><html><head><meta charset='utf-8'><title>One Craft Launcher</title></head>" +
                   "<body style='margin:0;height:100vh;display:flex;align-items:center;justify-content:center;background:#0b1424;font-family:Segoe UI,sans-serif;color:#e8e1cf'>" +
                   "<div style='text-align:center;border:2px solid #a07122;border-radius:10px;padding:40px 56px;background:#121f36'>" +
                   "<h1 style='color:#e3b04b;margin:0 0 12px'>" + WebUtility.HtmlEncode(title) + "</h1><p style='margin:0'>" + WebUtility.HtmlEncode(text) + "</p></div></body></html>";
        }
    }
}
