using System;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

// tessera-cpp export <model assembly> <output directory>
// Reads the TesseraCppHeaderAttribute entries from the assembly metadata (nothing is loaded or executed) and writes the
// headers. Files whose content is unchanged are not touched.
if (args.Length != 3 || args[0] != "export")
{
    Console.Error.WriteLine("usage: Tessera.Cpp export <assembly.dll> <output directory>");
    return 2;
}

string assembly = args[1], output = args[2];
if (!File.Exists(assembly))
{
    Console.Error.WriteLine($"Tessera.Cpp: assembly not found: {assembly}");
    return 1;
}

int written = 0, found = 0;
using (var stream = File.OpenRead(assembly))
using (var pe = new PEReader(stream))
{
    MetadataReader md = pe.GetMetadataReader();
    foreach (CustomAttributeHandle handle in md.GetAssemblyDefinition().GetCustomAttributes())
    {
        CustomAttribute attribute = md.GetCustomAttribute(handle);
        if (!IsHeaderAttribute(md, attribute)) continue;
        BlobReader blob = md.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1) continue;
        string? name = blob.ReadSerializedString();
        string content = blob.ReadSerializedString() ?? "";
        if (!SafeRelativePath(name)) continue;
        found++;
        string path = Path.Combine(output, Path.Combine(name!.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        byte[] bytes = new UTF8Encoding(false).GetBytes(content);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
        File.WriteAllBytes(path, bytes);
        written++;
        Console.WriteLine($"Tessera: wrote {Path.GetFullPath(path)}");
    }
}

if (found == 0) Console.WriteLine($"Tessera: no models in {Path.GetFileName(assembly)}; nothing to export.");
return 0;

// Headers are written under the output directory only: '/'-separated segments, no "." or "..", no rooted paths.
static bool SafeRelativePath(string? name) =>
    !string.IsNullOrEmpty(name) && name.Split('/').All(s => s.Length > 0 && s != "." && s != ".." && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);

static bool IsHeaderAttribute(MetadataReader md, CustomAttribute attribute)
{
    EntityHandle type;
    if (attribute.Constructor.Kind == HandleKind.MemberReference) type = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
    else if (attribute.Constructor.Kind == HandleKind.MethodDefinition) type = md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType();
    else return false;

    return type.Kind switch
    {
        HandleKind.TypeReference => Is(md.GetTypeReference((TypeReferenceHandle)type).Name, md.GetTypeReference((TypeReferenceHandle)type).Namespace),
        HandleKind.TypeDefinition => Is(md.GetTypeDefinition((TypeDefinitionHandle)type).Name, md.GetTypeDefinition((TypeDefinitionHandle)type).Namespace),
        _ => false,
    };

    bool Is(StringHandle name, StringHandle ns) => md.GetString(name) == "TesseraCppHeaderAttribute" && md.GetString(ns) == "Tessera";
}
