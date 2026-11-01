using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var paths = new Regex(@"(?<![A-Za-z0-9_:])(?:[A-Z]:[\\/](?!/)[^\x00\r\n""<>]{1,250}|https?://[^\x00\r\n""<>]{1,250}|/(?!/)[\p{L}_][\p{L}\p{N}_.-]*(?:/[\p{L}\p{N}_.~ -]+)+)", RegexOptions.IgnoreCase);
if (args is ["--self-test"])
{
    string[] rootedPaths = ["/Users/builder/project/File.cs", "/workspace/build/File.cs",
        "/tmp/vellumtext-source/src/File.cs", "/_/src/File.cs", @"C:\Users\builder\File.cs",
        "https://raw.githubusercontent.com/lurebat/VellumText/commit/src/File.cs"];
    foreach (var path in rootedPaths)
    {
        if (paths.Match(path).Value != path)
        {
            Console.Error.WriteLine($"Path scanner missed: {path}");
            return 1;
        }
    }
    string[] methodNames = ["Build:/Themes/RichTextToolbar.axaml", "Populate:/Themes/VellumText.axaml",
        "NamespaceInfo:/Themes/VellumText.axaml"];
    foreach (var name in methodNames)
    {
        if (paths.IsMatch(name))
        {
            Console.Error.WriteLine($"Path scanner misclassified an Avalonia generated method: {name}");
            return 1;
        }
    }
    Console.WriteLine("Package path scanner: 6 rooted paths and 3 generated-method controls passed.");
    return 0;
}
if (args.Length != 5)
{
    Console.Error.WriteLine("Usage: PackageInspector <artifact-root> <version> <commit> <source-root> <build-root>");
    return 2;
}
var root = Path.GetFullPath(args[0]);
var version = args[1];
var commit = args[2];
var sourceRoot = Path.GetFullPath(args[3]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
var buildRoot = Path.GetFullPath(args[4]).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
var repository = "https://github.com/lurebat/VellumText";
var target = $"https://raw.githubusercontent.com/lurebat/VellumText/{commit}/*";
string[] ids = ["VellumText.Core", "VellumText.Avalonia", "VellumText.Interop.Html",
    "VellumText.Interop.Rtf", "VellumText.Interop.Markdown", "VellumText.Interop.Json"];
var failures = new List<string>();
var reports = new List<object>();
var hashes = new List<object>();
var matches = new HashSet<string>(StringComparer.Ordinal);
var classifiedPaths = new HashSet<string>(StringComparer.Ordinal);
var forbidden = new Regex(@"[A-Z]:[\\/]Users[\\/]|[\\/]worktrees[\\/]|\.copilot[\\/]session-state|refs/heads/user[\\/]|/home/(?!runner/)[^/\s]+/", RegexOptions.IgnoreCase);
Require(Regex.IsMatch(commit, "^[0-9a-f]{40}$"), "Expected a full source SHA.");
var packages = Directory.GetFiles(Path.Combine(root, "packages"), "*.nupkg");
Require(packages.Length == ids.Length && Directory.GetFiles(Path.Combine(root, "packages"), "*.snupkg").Length == ids.Length,
    "Expected exactly six package/symbol pairs.");
foreach (var id in ids)
{
    var package = Path.Combine(root, "packages", $"{id}.{version}.nupkg");
    using var zip = ZipFile.OpenRead(package);
    using var specStream = zip.Entries.Single(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
    var spec = XDocument.Load(specStream);
    var metadata = spec.Root!.Elements().Single(e => e.Name.LocalName == "metadata");
    Require(metadata.Elements().Single(e => e.Name.LocalName == "id").Value == id, $"{id}: id");
    Require(metadata.Elements().Single(e => e.Name.LocalName == "version").Value == version, $"{id}: version");
    var repo = metadata.Elements().Single(e => e.Name.LocalName == "repository");
    Require((string?)repo.Attribute("url") == repository && (string?)repo.Attribute("commit") == commit, $"{id}: repository provenance");
    Require(repo.Attribute("branch") == null, $"{id}: branch must be omitted");
    var dependencies = metadata.Descendants().Where(e => e.Name.LocalName == "dependency")
        .Select(e => new { id = (string?)e.Attribute("id"), version = (string?)e.Attribute("version") }).ToArray();
    foreach (var dependency in dependencies.Where(d => d.id?.StartsWith("VellumText.", StringComparison.Ordinal) == true))
    {
        Require(dependency.version == version && ids.Contains(dependency.id), $"{id}: dependency stamp");
    }
    Require(zip.GetEntry("README.md") != null, $"{id}: readme");
    var dll = Read(zip.GetEntry($"lib/net10.0/{id}.dll")!);
    using var symbols = ZipFile.OpenRead(Path.ChangeExtension(package, ".snupkg"));
    var pdb = Read(symbols.GetEntry($"lib/net10.0/{id}.pdb")!);
    Require(dll.SequenceEqual(File.ReadAllBytes(Path.Combine(buildRoot, "bin", id, "release", $"{id}.dll"))), $"{id}: validated DLL bytes");
    Require(pdb.SequenceEqual(File.ReadAllBytes(Path.Combine(buildRoot, "bin", id, "release", $"{id}.pdb"))), $"{id}: validated PDB bytes");
    using var pe = new PEReader(new MemoryStream(dll));
    var assembly = pe.GetMetadataReader();
    var assemblyVersion = new Version(Version.Parse(version.Split('-')[0]).Major, Version.Parse(version.Split('-')[0]).Minor,
        Version.Parse(version.Split('-')[0]).Build, 0);
    Require(assembly.GetAssemblyDefinition().Version == assemblyVersion, $"{id}: assembly version");
    var information = assembly.GetAssemblyDefinition().GetCustomAttributes().Select(h => assembly.GetCustomAttribute(h))
        .Where(a => AttributeName(assembly, a) == "AssemblyInformationalVersionAttribute")
        .Select(a => { var r = assembly.GetBlobReader(a.Value); r.ReadUInt16(); return r.ReadSerializedString(); }).Single();
    Require(information == $"{version}+{commit}", $"{id}: informational version/commit");
    var codeViewEntry = pe.ReadDebugDirectory().Single(e => e.Type == DebugDirectoryEntryType.CodeView);
    var codeView = pe.ReadCodeViewDebugDirectoryData(codeViewEntry);
    using var provider = MetadataReaderProvider.FromPortablePdbStream(new MemoryStream(pdb));
    var reader = provider.GetMetadataReader();
    var pdbId = new BlobContentId(reader.DebugMetadataHeader!.Id);
    Require(codeView.Guid == pdbId.Guid && codeViewEntry.Stamp == pdbId.Stamp && codeView.Age == 1, $"{id}: PE/PDB pair");
    Require(pe.ReadDebugDirectory().Any(e => e.Type == DebugDirectoryEntryType.Reproducible), $"{id}: reproducible PE");
    Require(codeView.Path.StartsWith("/_/build/", StringComparison.Ordinal) || codeView.Path.StartsWith(buildRoot, StringComparison.Ordinal),
        $"{id}: CodeView outside neutral build root");
    Scan($"{id}:CodeView", Encoding.UTF8.GetBytes(codeView.Path));
    var docs = reader.Documents.Select(h => reader.GetString(reader.GetDocument(h).Name)).ToArray();
    foreach (var doc in docs)
    {
        Scan($"{id}:document", Encoding.UTF8.GetBytes(doc));
        Require(doc.StartsWith("/_/", StringComparison.Ordinal) || doc.StartsWith(sourceRoot, StringComparison.Ordinal) ||
            doc.StartsWith(buildRoot, StringComparison.Ordinal), $"{id}: unknown document root: {doc}");
    }
    var sourceLinks = new List<string>();
    var embeddedSources = 0;
    foreach (var handle in reader.CustomDebugInformation)
    {
        var item = reader.GetCustomDebugInformation(handle);
        var kind = reader.GetGuid(item.Kind);
        var bytes = reader.GetBlobBytes(item.Value);
        Scan($"{id}:custom-debug-info", bytes);
        if (kind == new Guid("cc110556-a091-4d38-9fec-25ab9a351a6a"))
        {
            var json = Encoding.UTF8.GetString(bytes);
            sourceLinks.Add(json);
            using var document = JsonDocument.Parse(json);
            foreach (var property in document.RootElement.GetProperty("documents").EnumerateObject())
            {
                Require((property.Name == "/_/*" || property.Name == sourceRoot + "*") && property.Value.GetString() == target,
                    $"{id}: exact SourceLink target");
            }
        }
        if (kind == new Guid("0e8a571b-6926-466e-b4ad-8ab04611f5fe"))
        {
            embeddedSources++;
            var length = BitConverter.ToInt32(bytes);
            if (length == 0)
            {
                Scan($"{id}:embedded-source", bytes.AsSpan(4).ToArray());
            }
            else
            {
                using var compressed = new DeflateStream(new MemoryStream(bytes, 4, bytes.Length - 4), CompressionMode.Decompress);
                using var expanded = new MemoryStream();
                compressed.CopyTo(expanded);
                Require(expanded.Length == length, $"{id}: embedded-source length");
                Scan($"{id}:embedded-source", expanded.ToArray());
            }
        }
    }
    Require(sourceLinks.Count == 1, $"{id}: one SourceLink record");
    if (sourceLinks.Count == 1)
    {
        using var document = JsonDocument.Parse(sourceLinks[0]);
        foreach (var doc in docs.Where(d => !d.StartsWith("/_/build/", StringComparison.Ordinal) && !d.StartsWith(buildRoot, StringComparison.Ordinal)))
        {
            Require(document.RootElement.GetProperty("documents").EnumerateObject()
                .Any(p => doc.StartsWith(p.Name.TrimEnd('*'), StringComparison.Ordinal)), $"{id}: source coverage: {doc}");
        }
    }
    foreach (var archive in new[] { zip, symbols })
    {
        foreach (var entry in archive.Entries)
        {
            Scan($"{id}:entry-name", Encoding.UTF8.GetBytes(entry.FullName));
            Scan($"{id}:{entry.FullName}", Read(entry));
        }
    }
    reports.Add(new { id, version, commit, informationalVersion = information, codeViewPath = codeView.Path,
        pdbId = pdbId.Guid, pdbStamp = pdbId.Stamp, documents = docs, sourceLinks, embeddedSources, dependencies });
    foreach (var file in new[] { package, Path.ChangeExtension(package, ".snupkg") })
    {
        hashes.Add(new { file = Path.GetFileName(file), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant(),
            bytes = new FileInfo(file).Length });
    }
}
Console.WriteLine(JsonSerializer.Serialize(new { repository, version, commit, passed = failures.Count == 0, failures,
    packages = reports, hashes, identityMatches = matches.Order().ToArray(),
    classifiedPaths = classifiedPaths.Order().ToArray() }, new JsonSerializerOptions { WriteIndented = true }));
return failures.Count == 0 ? 0 : 1;

void Require(bool condition, string failure)
{
    if (!condition)
    {
        failures.Add(failure);
    }
}
void Scan(string origin, byte[] data)
{
    foreach (var encoding in new[] { Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
    {
        var text = encoding.GetString(data);
        foreach (Match match in forbidden.Matches(text))
        {
            matches.Add($"{origin}: {match.Value}");
            failures.Add($"{origin}: personal builder path '{match.Value}'");
        }
        foreach (Match match in paths.Matches(text))
        {
            var path = match.Value;
            var classification = path.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "content-or-repository-URL" :
                path.StartsWith("/_/", StringComparison.Ordinal) || path.StartsWith(sourceRoot, StringComparison.Ordinal) ||
                path.StartsWith(buildRoot, StringComparison.Ordinal) ? "neutral-build-path" : "unknown-rooted-path";
            classifiedPaths.Add($"{origin}: {classification}: {path}");
            Require(classification != "unknown-rooted-path", $"{origin}: unclassified embedded path '{path}'");
        }
    }
}
static byte[] Read(ZipArchiveEntry entry)
{
    using var stream = entry.Open();
    using var memory = new MemoryStream();
    stream.CopyTo(memory);
    return memory.ToArray();
}
static string AttributeName(MetadataReader reader, CustomAttribute attribute)
{
    if (attribute.Constructor.Kind != HandleKind.MemberReference)
    {
        return "";
    }
    var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
    return member.Parent.Kind == HandleKind.TypeReference ? reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name) : "";
}
