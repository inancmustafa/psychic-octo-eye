using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

// Oturum durumları; sıralamada yüksek değer önce gösterilir.
internal enum SessionState { None = 0, Finished = 1, Working = 2, Waiting = 3 }

internal sealed class SessionInfo
{
    public string Key;
    public string SessionId;             // Claude Code oturum kimliği (kayıt dosyasının adı)
    public string Project;
    public string Title;                 // masaüstü uygulamasındaki oturum başlığı; yoksa null
    public SessionState State;
    public DateTime LastWriteUtc;
}

// Claude Code'un yerel oturum kayıtlarını (JSONL) salt okunur tarar.
// Yalnızca kayıt türü, oturum başlığı, klasör adı ve zaman kullanılır; konuşma içeriği saklanmaz.
// Durum sezgiseldir: izin bekleyen bir araç çağrısı ile uzun süren bir komut ayırt edilemez.
internal sealed class SessionScanner
{
    public double ToolWaitSeconds = 30;
    public double TextSettleSeconds = 8;
    public double StaleSeconds = 900;
    public double WaitExpirySeconds = 2700;
    public TimeSpan Horizon = TimeSpan.FromHours(3);
    public int MaxSessions = 8;

    private readonly Func<string, Dictionary<string, object>> parse;
    private readonly string claudeHome;
    private readonly Dictionary<string, Entry> cache = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    private sealed class Entry
    {
        public DateTime Write;
        public long Length;
        public string Kind;
        public string Cwd;
        public string Title;
        public bool HeadSearched;
    }

    public SessionScanner(Func<string, Dictionary<string, object>> parse, string claudeHome)
    {
        this.parse = parse;
        this.claudeHome = claudeHome;
    }

    public List<SessionInfo> Scan(DateTime nowUtc)
    {
        var result = new List<SessionInfo>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (FileInfo file in ClaudeFiles(nowUtc).OrderByDescending(f => f.LastWriteTimeUtc).Take(MaxSessions))
        {
            seen.Add(file.FullName);
            Entry entry;
            if (!cache.TryGetValue(file.FullName, out entry) || entry.Write != file.LastWriteTimeUtc || entry.Length != file.Length)
            {
                entry = Inspect(file, entry);
                cache[file.FullName] = entry;
            }
            double age = Math.Max(0, (nowUtc - file.LastWriteTimeUtc).TotalSeconds);
            result.Add(new SessionInfo
            {
                Key = file.FullName,
                SessionId = Path.GetFileNameWithoutExtension(file.Name),
                Project = ProjectName(entry.Cwd, file),
                Title = entry.Title,
                State = Resolve(entry.Kind, age),
                LastWriteUtc = file.LastWriteTimeUtc
            });
        }
        // Artık izlenmeyen dosyaların önbelleğini bırak
        foreach (string key in cache.Keys.Where(k => !seen.Contains(k)).ToList()) cache.Remove(key);
        return result.OrderByDescending(s => (int)s.State).ThenByDescending(s => s.LastWriteUtc).ToList();
    }

    private List<FileInfo> ClaudeFiles(DateTime nowUtc)
    {
        var list = new List<FileInfo>();
        if (String.IsNullOrEmpty(claudeHome)) return list;
        var projects = new DirectoryInfo(Path.Combine(claudeHome, "projects"));
        if (!projects.Exists) return list;
        DateTime min = nowUtc - Horizon;
        foreach (DirectoryInfo dir in SafeDirectories(projects))
            foreach (FileInfo file in SafeFiles(dir))
                if (file.LastWriteTimeUtc >= min) list.Add(file);
        return list;
    }

    private static IEnumerable<DirectoryInfo> SafeDirectories(DirectoryInfo root)
    {
        try { return root.GetDirectories(); }
        catch (IOException) { return new DirectoryInfo[0]; }
        catch (UnauthorizedAccessException) { return new DirectoryInfo[0]; }
    }

    private static IEnumerable<FileInfo> SafeFiles(DirectoryInfo dir)
    {
        try { return dir.GetFiles("*.jsonl", SearchOption.TopDirectoryOnly); }
        catch (IOException) { return new FileInfo[0]; }
        catch (UnauthorizedAccessException) { return new FileInfo[0]; }
    }

    public SessionState Resolve(string kind, double age)
    {
        switch (kind)
        {
            case "done": return SessionState.Finished;
            case "wait": return age < WaitExpirySeconds ? SessionState.Waiting : SessionState.Finished;
            case "tool":
                if (age < ToolWaitSeconds) return SessionState.Working;
                return age < WaitExpirySeconds ? SessionState.Waiting : SessionState.Finished;
            case "text": return age < TextSettleSeconds ? SessionState.Working : SessionState.Finished;
            case "active": return age < StaleSeconds ? SessionState.Working : SessionState.Finished;
            default: return age < TextSettleSeconds * 2 ? SessionState.Working : SessionState.Finished;
        }
    }

    private Entry Inspect(FileInfo file, Entry previous)
    {
        var entry = new Entry { Write = file.LastWriteTimeUtc, Length = file.Length };
        if (previous != null) { entry.Cwd = previous.Cwd; entry.Title = previous.Title; entry.HeadSearched = previous.HeadSearched; }
        try
        {
            // Son satır çok uzun olabilir (araç çıktısı); tam satır bulunamazsa okuma penceresi büyütülür.
            foreach (int size in new[] { 65536, 524288, 4194304 })
            {
                bool whole;
                string text = ReadTail(file.FullName, size, out whole);
                // Masaüstü uygulaması başlığı "custom-title" kaydı olarak düzenli aralıklarla yeniden yazar; en sonuncusu geçerlidir.
                string title = ExtractString(text, "customTitle", true);
                if (title != null) entry.Title = title;
                string[] lines = text.Split('\n');
                int first = whole ? 0 : 1;
                int parsed = 0;
                for (int i = lines.Length - 1; i >= first && parsed < 80; i--)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] != '{') continue;
                    Dictionary<string, object> item;
                    try { item = parse(line); }
                    catch { continue; }
                    if (item == null) continue;
                    parsed++;
                    if (entry.Cwd == null) entry.Cwd = Str(item, "cwd");
                    string kind = ClaudeKind(item);
                    if (kind != null) { entry.Kind = kind; break; }
                }
                if (entry.Kind != null || whole || parsed > 0) break;
            }
            // Son satırlarda klasör yoksa dosyanın başından (ilk kayıtlar cwd taşır) okunur
            if (entry.Cwd == null) entry.Cwd = ExtractString(ReadHead(file.FullName, 65536), "cwd");
            // Başlık sonda yoksa ilk başlık kaydı dosyanın başında aranır; başlıksız (terminal) oturumlarda bir kez denenir.
            if (entry.Title == null && !entry.HeadSearched)
            {
                entry.HeadSearched = true;
                entry.Title = ExtractString(ReadHead(file.FullName, 1048576), "customTitle");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return entry;
    }

    // Claude Code kaydı: type user/assistant, message.content blokları
    public static string ClaudeKind(Dictionary<string, object> item)
    {
        string type = Str(item, "type");
        if (type != "user" && type != "assistant") return null;
        if (Flag(item, "isSidechain") || Flag(item, "isMeta")) return null;
        var message = Map(Get(item, "message"));
        object content = Get(message, "content");
        if (type == "assistant")
        {
            string stop = Str(message, "stop_reason");
            List<Dictionary<string, object>> blocks = Blocks(content);
            string last = blocks.Count > 0 ? Str(blocks[blocks.Count - 1], "type") : null;
            if (last == "tool_use" || stop == "tool_use") return "tool";
            if (stop == "end_turn" || stop == "stop_sequence") return "done";
            if (last == "thinking" || last == "redacted_thinking") return "active";
            if (last == "text") return "text";
            return blocks.Count > 0 ? "active" : null;
        }
        string plain = content as string;
        if (plain != null)
        {
            if (plain.Contains("[Request interrupted")) return "done";
            if (plain.StartsWith("<local-command-stdout>") || plain.StartsWith("<local-command-stderr>")) return "done";
            return "active";
        }
        List<Dictionary<string, object>> parts = Blocks(content);
        if (parts.Count == 0) return null;
        foreach (var part in parts)
        {
            string text = Str(part, "text") ?? (Get(part, "content") as string);
            if (text != null && text.Contains("[Request interrupted")) return "done";
        }
        return "active";
    }

    // Claude masaüstü uygulaması her Code oturumu için %APPDATA%\Claude\claude-code-sessions\<kuruluş>\<hesap>\local_<kimlik>.json yazar;
    // içindeki cliSessionId, ~/.claude/projects altındaki kayıt dosyasının adıdır. Bulunamazsa (terminal oturumu) null.
    public static string DesktopSessionId(string sessionId)
    {
        if (String.IsNullOrEmpty(sessionId)) return null;
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
        if (!Directory.Exists(root)) return null;
        try
        {
            foreach (string path in Directory.EnumerateFiles(root, "local_*.json", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (ExtractString(text, "cliSessionId") != sessionId) continue;
                string id = Path.GetFileNameWithoutExtension(path);
                // Uygulamanın bağlantı işleyicisi yalnızca bu biçimi kabul eder
                return System.Text.RegularExpressions.Regex.IsMatch(id, "^local_[A-Za-z0-9-]{1,64}$") ? id : null;
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return null;
    }

    public static string ProjectName(string cwd, FileInfo file)
    {
        if (!String.IsNullOrEmpty(cwd))
        {
            string trimmed = cwd.TrimEnd('\\', '/');
            int cut = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
            string name = cut >= 0 ? trimmed.Substring(cut + 1) : trimmed;
            if (name.Length > 0) return name;
        }
        if (file != null && file.Directory != null)
        {
            // Klasör adı yolun kodlanmış hâlidir (C--Users-ad-proje); son parça gösterilir.
            string dir = file.Directory.Name;
            int dash = dir.LastIndexOf('-');
            return dash >= 0 && dash < dir.Length - 1 ? dir.Substring(dash + 1) : dir;
        }
        return "Claude oturumu";
    }

    private static string ReadTail(string path, int max, out bool whole)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            long length = stream.Length;
            long start = Math.Max(0, length - max);
            whole = start == 0;
            stream.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[length - start];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
    }

    private static string ReadHead(string path, int max)
    {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            var buffer = new byte[(int)Math.Min(max, stream.Length)];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return Encoding.UTF8.GetString(buffer, 0, read);
        }
    }

    // Tam JSON ayrıştırması yapmadan "anahtar":"değer" çiftinden metin çıkarır (çok büyük ilk satırlar için).
    // fromEnd: metnin sonundan başlayarak arar, yani en son yazılmış değeri döndürür.
    public static string ExtractString(string text, string key, bool fromEnd = false)
    {
        if (String.IsNullOrEmpty(text)) return null;
        string token = "\"" + key + "\"";
        int at = fromEnd ? text.LastIndexOf(token, StringComparison.Ordinal) : text.IndexOf(token, StringComparison.Ordinal);
        while (at >= 0)
        {
            int i = at + token.Length;
            while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
            if (i < text.Length && text[i] == ':')
            {
                i++;
                while (i < text.Length && Char.IsWhiteSpace(text[i])) i++;
                if (i < text.Length && text[i] == '"')
                {
                    var value = new StringBuilder();
                    for (i++; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == '"') return value.Length > 0 ? value.ToString() : null;
                        if (c == '\\' && i + 1 < text.Length)
                        {
                            char n = text[++i];
                            if (n == 'u' && i + 4 < text.Length)
                            {
                                int code;
                                if (Int32.TryParse(text.Substring(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out code)) value.Append((char)code);
                                i += 4;
                            }
                            else value.Append(n == 'n' ? '\n' : n == 't' ? '\t' : n);
                        }
                        else value.Append(c);
                    }
                    return null;
                }
            }
            if (!fromEnd) at = text.IndexOf(token, at + token.Length, StringComparison.Ordinal);
            else at = at > 0 ? text.LastIndexOf(token, at - 1, StringComparison.Ordinal) : -1;
        }
        return null;
    }

    private static object Get(Dictionary<string, object> map, string key)
    {
        object value;
        return map != null && map.TryGetValue(key, out value) ? value : null;
    }

    private static Dictionary<string, object> Map(object value)
    {
        return value as Dictionary<string, object> ?? new Dictionary<string, object>();
    }

    private static string Str(Dictionary<string, object> map, string key)
    {
        string value = Get(map, key) as string;
        return String.IsNullOrEmpty(value) ? null : value;
    }

    private static bool Flag(Dictionary<string, object> map, string key)
    {
        object value = Get(map, key);
        return value is bool && (bool)value;
    }

    private static List<Dictionary<string, object>> Blocks(object content)
    {
        var list = new List<Dictionary<string, object>>();
        if (content == null || content is string) return list;
        var sequence = content as IEnumerable;
        if (sequence == null) return list;
        foreach (object item in sequence)
        {
            var block = item as Dictionary<string, object>;
            if (block != null) list.Add(block);
        }
        return list;
    }
}
