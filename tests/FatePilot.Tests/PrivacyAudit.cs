using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.RegularExpressions;

// Regression checks complement manual review; they are not a sandbox or a proof of absence.
internal static class PrivacyAudit
{
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }

    public static void Source(string sourceRoot)
    {
        var files = Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(x => x is "obj" or "bin"));
        foreach (var path in files)
        {
            var source = File.ReadAllText(path).Replace("\r\n", "\n");
            Require(!Regex.IsMatch(source, @"\b(HttpClient|WebClient|WebRequest|Socket|TcpClient|UdpClient|ProcessStartInfo|DllImport|LibraryImport|NativeLibrary|RegistryKey)\b|\bProcess\s*\.|Assembly\s*\.\s*Load|SendChat|SendMessage|ContentId|AccountId"), "Unreviewed sensitive API: " + path);
            var urls = Regex.Matches(source, "https?://[^\\\"\\s]+").Select(m => m.Value).ToArray();
            var file = Path.GetFileName(path);
            Require(urls.Length == 0 || (file == "MainWindow.cs" && urls.SequenceEqual(new[] { "https://ko-fi.com/mokoena" }))
                || (file == "FaloopLink.cs" && urls.SequenceEqual(new[] { "https://faloop.app" })), "Unexpected runtime endpoint: " + path);
            if (source.Contains("OpenLink("))
                Require(Regex.Matches(source, "OpenLink\\(").Count == 1 &&
                    ((file == "MainWindow.cs" && source.Contains("if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Heart, \"Buy my cat a treat\"))\n            Dalamud.Utility.Util.OpenLink(\"https://ko-fi.com/mokoena\");")) ||
                     (file == "MainWindow.Tracker.cs" && source.Contains("var link = FaloopLink.ForWorld(current);") && source.Contains("if (ImGui.Button(L.T(\"Abrir Faloop no navegador\")))\n            Dalamud.Utility.Util.OpenLink(link);"))), "Browser must be opened only by a reviewed fixed-origin button");
            foreach (Match call in Regex.Matches(source, @"File\.(WriteAllText|ReadAllText|Delete)\(([^,)]+)"))
                Require(Path.GetFileName(path) == "Combat.cs" && call.Groups[2].Value == "RecoveryPath", "Unreviewed file access: " + path);
        }
    }

    public static void Binary(string directory)
    {
        foreach (var name in new[] { "FatePilot.dll", "FatePilot.Core.dll" })
        {
            using var input = File.OpenRead(Path.Combine(directory, name));
            using var pe = new PEReader(input);
            var md = pe.GetMetadataReader();
            var typeNames = md.TypeReferences.Select(h => md.GetTypeReference(h))
                .Select(t => md.GetString(t.Namespace) + "." + md.GetString(t.Name)).ToArray();
            foreach (var type in typeNames)
                Require(!type.StartsWith("System.Net.") && !type.StartsWith("System.Reflection.Emit.")
                    && type is not ("System.Diagnostics.Process" or "System.Diagnostics.ProcessStartInfo" or "System.Runtime.InteropServices.NativeLibrary")
                    && !type.StartsWith("Microsoft.Win32.Registry"), "Unexpected binary capability: " + type);
            foreach (var method in md.MethodDefinitions.Select(h => md.GetMethodDefinition(h)))
                Require(!method.Attributes.HasFlag(MethodAttributes.PinvokeImpl), "Plugin declares a native import");
            foreach (var handle in md.MemberReferences)
            {
                var member = md.GetMemberReference(handle);
                var method = md.GetString(member.Name);
                if (member.Parent.Kind != HandleKind.TypeReference) continue;
                var owner = md.GetTypeReference((TypeReferenceHandle)member.Parent);
                var type = md.GetString(owner.Namespace) + "." + md.GetString(owner.Name);
                Require(!(type == "System.Reflection.Assembly" && method.StartsWith("Load")), "Dynamic assembly loading");
                Require(!(method == "get_Name" && (type.EndsWith("IPlayerCharacter") || type.EndsWith("IGameObject"))), "Player-name reading");
                Require(method is not ("get_ContentId" or "get_LocalContentId" or "get_AccountId"), "Account identifier reading");
            }
            var references = md.AssemblyReferences.Select(h => md.GetString(md.GetAssemblyReference(h).Name)).ToArray();
            foreach (var reference in references)
                Require(reference.StartsWith("System.") || reference is "System" or "Dalamud" or "Dalamud.Bindings.ImGui" or "FFXIVClientStructs" or "InteropGenerator.Runtime" or "Lumina" or "Lumina.Excel" or "FatePilot.Core", "Unreviewed dependency: " + reference);
            Console.WriteLine(name + " references: " + string.Join(", ", references));
            if (name != "FatePilot.dll") continue;
            var resources = md.ManifestResources.Select(h => md.GetString(md.GetManifestResource(h).Name)).ToHashSet();
            foreach (var code in new[] { "en", "ja", "de", "fr", "pt-BR", "es" })
                Require(resources.Contains("FatePilot.Localization." + code + ".json"), "Missing embedded language: " + code);
            Require(resources.Contains("FatePilot.Assets.logo.png"), "Logo must be bundled locally");
            Require(resources.Contains("FatePilot.Assets.cat.png"), "Cat avatar must be bundled locally");
        }
    }

    public static void LocalizationCoverage(string sourceRoot)
    {
        var catalog = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(sourceRoot, "FatePilot", "Localization", "pt-BR.json")))!;
        foreach (var path in Directory.EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
            foreach (Match match in Regex.Matches(File.ReadAllText(path), "L\\.T\\(\"((?:[^\"\\\\]|\\\\.)*)\""))
                Require(catalog.ContainsKey(Regex.Unescape(match.Groups[1].Value)), "Missing translation key: " + match.Groups[1].Value);
    }
}
