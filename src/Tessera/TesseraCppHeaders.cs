using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Tessera;

/// <summary>Writes the C++ headers that the source generator embedded into a model assembly.</summary>
public static class TesseraCppHeaders
{
    /// <summary>
    /// Returns (relative path, content) for every header embedded in <paramref name="assembly"/>: one header per model
    /// type (<c>namespace/Type.tessera.hpp</c>) and the umbrella header that includes them.
    /// </summary>
    public static IReadOnlyList<(string FileName, string Content)> Read(Assembly assembly)
    {
        var list = new List<(string, string)>();
        foreach (var a in assembly.GetCustomAttributes<TesseraCppHeaderAttribute>()) list.Add((a.FileName, a.Content));
        return list;
    }

    /// <summary>
    /// Writes the headers of <paramref name="assembly"/> into <paramref name="directory"/>. Files whose content is
    /// unchanged are not touched, so C++ builds do not rebuild needlessly. Returns the written paths.
    /// </summary>
    public static IReadOnlyList<string> Export(Assembly assembly, string directory)
    {
        var written = new List<string>();
        foreach (var (name, content) in Read(assembly))
        {
            if (!SafeRelativePath(name)) throw new InvalidOperationException($"Unexpected header path: {name}");
            string path = Path.Combine(directory, Path.Combine(name.Split('/')));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) continue;
            File.WriteAllBytes(path, bytes);
            written.Add(path);
        }

        return written;
    }

    // Under the output directory only: '/'-separated segments, no "." or "..", no rooted paths.
    private static bool SafeRelativePath(string name) =>
        name.Length > 0 && name.Split('/').All(s => s.Length > 0 && s != "." && s != ".." && s.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
}
