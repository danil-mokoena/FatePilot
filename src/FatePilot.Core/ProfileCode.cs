using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace FatePilot.Core;

// Shareable profile codes: "FATEPILOT1:" + base64(gzip(json)). Only session settings travel;
// the job queue (gearsets of one character) is left out. Nothing is sent anywhere: the player
// copies and pastes the text. Decoding is bounded and every value is validated before use.
public static class ProfileCode
{
    public const string Prefix = "FATEPILOT1:";
    public const int MaxCodeLength = 16384;
    public const int MaxNameLength = 40;
    private const int MaxJsonBytes = 131072;
    private const int MaxListEntries = 500;
    private static readonly JsonSerializerOptions Json = new() { MaxDepth = 16 };

    private sealed class Payload
    {
        public string? Name { get; set; }
        public RunOptions? Options { get; set; }
    }

    public static string Encode(string name, RunOptions options)
    {
        var copy = JsonSerializer.Deserialize<RunOptions>(JsonSerializer.Serialize(options))!;
        copy.JobQueue = []; copy.UseJobQueue = false;
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Payload { Name = CleanName(name, MaxNameLength), Options = copy });
        using var buffer = new MemoryStream();
        using (var zip = new GZipStream(buffer, CompressionLevel.SmallestSize, true)) zip.Write(bytes);
        return Prefix + Convert.ToBase64String(buffer.ToArray());
    }

    public static bool TryDecode(string? code, out string name, out RunOptions options, out string error)
    {
        name = ""; options = new(); error = "";
        var text = (code ?? "").Trim();
        if (text.Length > MaxCodeLength) { error = L.T("Código de perfil grande demais."); return false; }
        if (!text.StartsWith(Prefix, StringComparison.Ordinal)) { error = L.T("Código de perfil inválido."); return false; }
        try
        {
            var packed = Convert.FromBase64String(text[Prefix.Length..]);
            using var zip = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress);
            using var json = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = zip.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (json.Length + read > MaxJsonBytes) { error = L.T("Código de perfil grande demais."); return false; }
                json.Write(chunk, 0, read);
            }
            var payload = JsonSerializer.Deserialize<Payload>(json.ToArray(), Json);
            if (payload?.Options is not { } o) { error = L.T("Código de perfil inválido."); return false; }
            Sanitize(o);
            o.Validate();
            name = CleanName(payload.Name ?? "", MaxNameLength);
            if (name.Length == 0) name = L.T("Perfil importado");
            options = o;
            return true;
        }
        catch (InvalidOperationException ex) { error = ex.Message; return false; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or NotSupportedException or ArgumentException)
        { error = L.T("Código de perfil inválido."); return false; }
    }

    // JSON may carry nulls or oversized lists; keep only well-formed, bounded values.
    private static void Sanitize(RunOptions o)
    {
        o.Route = (o.Route ?? []).Where(d => d != null).Take(30).Select(d => d with { Name = CleanName(d.Name ?? "", 80) }).ToList();
        if (o.ReturnDestination is { } r) o.ReturnDestination = r with { Name = CleanName(r.Name ?? "", 80) };
        if (o.GemstoneDestination is { } g) o.GemstoneDestination = g with { Name = CleanName(g.Name ?? "", 80) };
        o.ExcludedFates = (o.ExcludedFates ?? []).Distinct().Take(MaxListEntries).ToList();
        o.PriorityFates = (o.PriorityFates ?? []).Distinct().Take(MaxListEntries).ToList();
        o.JobQueue = []; o.UseJobQueue = false;
        o.PurchaseQueue = (o.PurchaseQueue ?? []).Where(x => x != null).Take(50).ToList();
        foreach (var order in o.PurchaseQueue) { order.Name = CleanName(order.Name ?? "", 80); order.Bought = 0; }
    }

    // Printable text only, no line breaks or control characters, trimmed to a length.
    public static string CleanName(string value, int max)
    {
        var builder = new StringBuilder();
        foreach (var ch in value)
            if (!char.IsControl(ch) && !char.IsSurrogate(ch) && ch is not ('​' or '‮' or '‭')) builder.Append(ch);
        var text = builder.ToString().Trim();
        return text.Length <= max ? text : text[..max].TrimEnd();
    }
}
