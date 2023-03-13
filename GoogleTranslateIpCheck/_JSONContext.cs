using System.Text.Json.Serialization;

namespace GoogleTranslateIpCheck;

[JsonSerializable(typeof(Config))]
public partial class _JSONContext : JsonSerializerContext { }
