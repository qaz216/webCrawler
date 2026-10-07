using System.Text.Json;

namespace Crawler.Contracts;

/// <summary>Serializer settings shared by every producer and consumer of crawl messages.</summary>
public static class MessageJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T message) => JsonSerializer.Serialize(message, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
