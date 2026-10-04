using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

// Claude kota okuyucusu; Node.js gerektirmeden widget'ın içinde arka planda çalışır.
// Mevcut Claude Code oturumu (~/.claude/.credentials.json) yalnızca kota adresi için bellekte kullanılır:
// oturum yenilenmez, kopyalanmaz, değiştirilmez. usage.json'a yalnızca filtrelenmiş kota bilgisi yazılır.
internal static class Collector
{
    private const string Endpoint = "https://api.anthropic.com/api/oauth/usage";
    private const double IntervalMs = 300000, CooldownMs = 60000;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly DateTime Epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // Sunucudaki pencere anahtarı, kartta görünen ad ve pencere süresi (dk)
    private static readonly string[] Ids = { "five_hour", "seven_day", "seven_day_sonnet", "seven_day_opus" };
    private static readonly string[] Labels = { "Oturum · 5 saat", "Haftalık · tüm modeller", "Haftalık · Sonnet", "Haftalık · Opus" };
    private static readonly int[] Minutes = { 300, 10080, 10080, 10080 };

    private sealed class Failure : Exception
    {
        public readonly string Kind;
        public readonly double RetryMs;
        public Failure(string kind, double retryMs = IntervalMs) : base(kind) { Kind = kind; RetryMs = retryMs; }
    }

    // Tek ölçüm turu: gerekiyorsa sorgular ve usage.json'u atomik yazar. Başka bir tur sürüyorsa null döner.
    public static string Run(string stateDir, string claudeHome, bool force)
    {
        Directory.CreateDirectory(stateDir);
        // Kilit, ikinci bir pencerenin ya da elle yenilemenin aynı anda istek atmasını önler.
        string lockPath = Path.Combine(stateDir, "collect.lock");
        FileStream lockFile;
        try
        {
            try { if (File.Exists(lockPath) && (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath)).TotalMilliseconds > CooldownMs) File.Delete(lockPath); }
            catch { }
            lockFile = new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
        }
        catch (IOException) { return null; }
        using (lockFile)
        {
            var json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            string statePath = Path.Combine(stateDir, "usage.json");
            var old = Get(ReadJson(json, statePath), "claude") as Dictionary<string, object>;
            long writtenAt = Now();
            Dictionary<string, object> claude = State(json, old, claudeHome, force);
            var result = new Dictionary<string, object> { { "schema", 1 }, { "writtenAt", writtenAt }, { "claude", claude } };
            string temporary = statePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, json.Serialize(result), new UTF8Encoding(false));
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    if (File.Exists(statePath)) File.Replace(temporary, statePath, null);
                    else File.Move(temporary, statePath);
                    break;
                }
                catch (IOException)
                {
                    // Widget dosyayı o anda okuyor olabilir; kısa bekleyip yeniden dene
                    if (attempt >= 4) { File.Delete(temporary); throw; }
                    Thread.Sleep(100);
                }
            }
            return Get(claude, "status") as string;
        }
    }

    private static Dictionary<string, object> State(JavaScriptSerializer json, Dictionary<string, object> old, string claudeHome, bool force)
    {
        if (old != null)
        {
            long now = Now();
            double? next = ToDouble(Get(old, "nextAttemptAt")), attempted = ToDouble(Get(old, "attemptedAt"));
            bool retryBlocked = Get(old, "error") as string == "rate_limited" && next.HasValue && now < next.Value;
            bool cooldown = attempted.HasValue && attempted.Value != 0 && now - attempted.Value < CooldownMs;
            if (retryBlocked || cooldown || (!force && next.HasValue && now < next.Value)) return old;
        }
        try { return Result(Usage(json, claudeHome)); }
        catch (Failure failure) { return FailureResult(failure.Kind, old, failure.RetryMs); }
        catch (Exception) { return FailureResult("connection", old, IntervalMs); }
    }

    private static List<object> Usage(JavaScriptSerializer json, string claudeHome)
    {
        // Sahibi olan CLI'nin oturumu her seferinde yeniden okunur.
        var oauth = Get(ReadJson(json, Path.Combine(claudeHome, ".credentials.json")), "claudeAiOauth") as Dictionary<string, object>;
        string token = Get(oauth, "accessToken") as string;
        if (String.IsNullOrEmpty(token)) throw new Failure("login_required");

        // Hedef çerçeve özniteliği olmayan exe eski TLS varsayılanlarıyla açılır; TLS 1.2 açıkça eklenir.
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
        var request = (HttpWebRequest)WebRequest.Create(Endpoint);
        request.Method = "GET";
        request.AllowAutoRedirect = false;
        request.Timeout = 15000;
        request.ReadWriteTimeout = 15000;
        request.Accept = "application/json";
        request.UserAgent = "UsageNotch";
        request.Headers[HttpRequestHeader.Authorization] = "Bearer " + token;
        request.Headers["anthropic-beta"] = "oauth-2025-04-20";
        HttpWebResponse response;
        try { response = (HttpWebResponse)request.GetResponse(); }
        catch (WebException error)
        {
            response = error.Response as HttpWebResponse;
            if (response == null) throw new Failure("connection");
        }
        using (response)
        {
            int code = (int)response.StatusCode;
            bool isJson = (response.ContentType ?? "").IndexOf("json", StringComparison.OrdinalIgnoreCase) >= 0;
            if (code == 401) throw new Failure("login_required");
            // Kurum güvenlik duvarının HTML engelleme sayfası da 403 döner; o durumda hesap izni değil bağlantı sorunudur.
            if (code == 403) throw new Failure(isJson ? "forbidden" : "connection");
            if (code == 429) throw new Failure("rate_limited", RetryDelay(response.Headers["Retry-After"]));
            if (code < 200 || code > 299) throw new Failure("connection");
            string body;
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8)) body = reader.ReadToEnd();
            List<object> windows = Normalize(json.Deserialize<Dictionary<string, object>>(body));
            if (windows.Count == 0) throw new Failure("no_quota");
            return windows;
        }
    }

    private static double RetryDelay(string header)
    {
        double seconds;
        DateTimeOffset at;
        double delay = Double.NaN;
        if (!String.IsNullOrEmpty(header))
        {
            if (Double.TryParse(header, NumberStyles.Float, Inv, out seconds)) delay = seconds * 1000;
            else if (DateTimeOffset.TryParse(header, Inv, DateTimeStyles.AssumeUniversal, out at)) delay = (at.UtcDateTime - DateTime.UtcNow).TotalMilliseconds;
        }
        return Double.IsNaN(delay) ? 900000 : Math.Max(delay, IntervalMs);
    }

    // Eksik pencere atlanır; gerçek %0 ile "veri yok" ayrı tutulur.
    private static List<object> Normalize(Dictionary<string, object> data)
    {
        var windows = new List<object>();
        for (int i = 0; i < Ids.Length; i++)
        {
            var w = Get(data, Ids[i]) as Dictionary<string, object>;
            double? used = Percent(Get(w, "utilization"));
            if (!used.HasValue) continue;
            windows.Add(new Dictionary<string, object>
            {
                { "id", Ids[i] }, { "label", Labels[i] }, { "usedPercent", used.Value },
                { "resetsAt", ResetsAt(Get(w, "resets_at")) }, { "durationMins", Minutes[i] }
            });
        }
        return windows;
    }

    private static Dictionary<string, object> Result(List<object> windows)
    {
        long now = Now();
        return new Dictionary<string, object>
        {
            { "name", "Claude" }, { "status", "ok" }, { "windows", windows }, { "updatedAt", now }, { "attemptedAt", now },
            { "error", null }, { "nextAttemptAt", now + (long)IntervalMs }
        };
    }

    // Ağ hatasında son ölçüm zamanıyla birlikte korunur; giriş, izin ve kota yokluğunda silinir.
    private static Dictionary<string, object> FailureResult(string kind, Dictionary<string, object> old, double retryMs)
    {
        long now = Now();
        bool clear = kind == "login_required" || kind == "forbidden" || kind == "no_quota";
        return new Dictionary<string, object>
        {
            { "name", "Claude" }, { "status", clear ? kind : "stale" },
            { "windows", clear ? new object[0] : (Get(old, "windows") ?? new object[0]) },
            { "updatedAt", clear ? null : Get(old, "updatedAt") }, { "attemptedAt", now }, { "error", kind },
            { "nextAttemptAt", now + (long)Math.Max(CooldownMs, retryMs) }
        };
    }

    private static object ResetsAt(object value)
    {
        string text = value as string;
        DateTimeOffset at;
        if (String.IsNullOrEmpty(text) || !DateTimeOffset.TryParse(text, Inv, DateTimeStyles.AssumeUniversal, out at)) return null;
        return (long)(at.UtcDateTime - Epoch).TotalMilliseconds;
    }

    // Yalnızca sonlu, negatif olmayan sayılar kota sayılır; metin veya eksik değer sıfıra çevrilmez.
    private static double? Percent(object value)
    {
        double? n = ToDouble(value);
        return n.HasValue && !Double.IsNaN(n.Value) && !Double.IsInfinity(n.Value) && n.Value >= 0 ? (double?)Math.Min(100, n.Value) : null;
    }

    private static double? ToDouble(object value)
    {
        if (value == null || value is string || value is bool) return null;
        try { return Convert.ToDouble(value, Inv); }
        catch { return null; }
    }

    private static long Now() { return (long)(DateTime.UtcNow - Epoch).TotalMilliseconds; }

    private static Dictionary<string, object> ReadJson(JavaScriptSerializer json, string path)
    {
        try { return json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path)); }
        catch { return null; }
    }

    private static object Get(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) ? value : null;
    }
}
