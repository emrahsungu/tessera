using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Bench.Models;
using Newtonsoft.Json.Serialization;
using NJ = Newtonsoft.Json;

namespace Bench;

/// <summary>System.Text.Json's source-generated serializers (its fastest way to write): fields included, nulls left out.</summary>
[JsonSourceGenerationOptions(IncludeFields = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Prefab))]
[JsonSerializable(typeof(World))]
[JsonSerializable(typeof(RecordSet))]
[JsonSerializable(typeof(Series))]
[JsonSerializable(typeof(Scene))]
[JsonSerializable(typeof(Lookup))]
[JsonSerializable(typeof(FeatureCollection))]
internal sealed partial class BenchJson : JsonSerializerContext
{
}

/// <summary>
/// Both JSON libraries write the same JSON, so one C++ reader reads either: public fields in declaration order, nulls
/// left out, and the union's member first as "$type" with the names on <see cref="Component"/>.
/// </summary>
public static class JsonFormats
{
    public static JsonTypeInfo<T> Stj<T>() => (JsonTypeInfo<T>)BenchJson.Default.GetTypeInfo(typeof(T))!;

    private static NJ.JsonSerializer NewtonsoftSerializer() => NJ.JsonSerializer.Create(new NJ.JsonSerializerSettings
    {
        NullValueHandling = NJ.NullValueHandling.Ignore,
        TypeNameHandling = NJ.TypeNameHandling.Auto,
        SerializationBinder = new UnionNames(),
    });

    /// <summary>Newtonsoft.Json into a reused UTF-8 stream (a JsonTextWriter cannot be reset, so each write makes one).</summary>
    public sealed class NewtonsoftWriter
    {
        private readonly NJ.JsonSerializer _serializer = NewtonsoftSerializer();
        private readonly MemoryStream _stream = new(1 << 20);
        private readonly StreamWriter _text;

        public NewtonsoftWriter() => _text = new StreamWriter(_stream, new UTF8Encoding(false), 1 << 16, leaveOpen: true);

        public MemoryStream Write<T>(T value)
        {
            _stream.SetLength(0);
            using (var json = new NJ.JsonTextWriter(_text) { CloseOutput = false }) _serializer.Serialize(json, value, typeof(T));
            _text.Flush();
            return _stream;
        }
    }

    /// <summary>"$type" names for the union's members, as System.Text.Json writes them.</summary>
    private sealed class UnionNames : ISerializationBinder
    {
        private static readonly Dictionary<Type, string> s_names = new()
        {
            [typeof(ImageComponent)] = "Image", [typeof(TextComponent)] = "Text", [typeof(ButtonComponent)] = "Button",
            [typeof(LayoutComponent)] = "Layout", [typeof(ControllerComponent)] = "Controller",
        };

        public Type BindToType(string? assemblyName, string typeName) => throw new NotSupportedException("the benchmark only writes JSON");

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = null;
            typeName = s_names[serializedType];
        }
    }
}
