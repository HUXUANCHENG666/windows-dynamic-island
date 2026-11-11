using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland.Core.Ipc;

public sealed class ClientCommand
{
    [JsonPropertyName("cmd")]
    public string Cmd { get; set; } = "show";

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "";

    [JsonPropertyName("minutes")]
    public int Minutes { get; set; }

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ClientCommand FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<ClientCommand>(json, Options) ?? new ClientCommand();
        }
        catch (JsonException)
        {
            return new ClientCommand();
        }
    }

    public static ClientCommand ParseArgs(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
            return new ClientCommand { Cmd = "show" };

        string head = args[0].Trim().TrimStart('-').ToLowerInvariant();
        return head switch
        {
            "toast" => new ClientCommand
            {
                Cmd = "toast",
                Title = args.Count > 1 ? args[1] : "通知",
                Body = args.Count > 2 ? string.Join(' ', args.Skip(2)) : ""
            }.Sanitized(),
            "timer" => ParseTimer(args).Sanitized(),
            _ => new ClientCommand { Cmd = "show" }
        };
    }

    public ClientCommand Sanitized()
    {
        static string Cut(string? value, int max)
        {
            value ??= "";
            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return value.Length <= max ? value : value[..max];
        }

        return new ClientCommand
        {
            Cmd = string.IsNullOrWhiteSpace(Cmd) ? "show" : Cmd.Trim().ToLowerInvariant(),
            Title = Cut(Title, 48),
            Body = Cut(Body, 96),
            Mode = (Mode ?? "").Trim().ToLowerInvariant(),
            Minutes = Math.Clamp(Minutes, 0, 600)
        };
    }

    static ClientCommand ParseTimer(IReadOnlyList<string> args)
    {
        if (args.Count < 2)
            return new ClientCommand { Cmd = "timer", Mode = "stopwatch" };

        string arg = args[1].Trim().ToLowerInvariant();
        if (arg is "stopwatch" or "秒表")
            return new ClientCommand { Cmd = "timer", Mode = "stopwatch" };
        if (int.TryParse(arg, out int minutes))
            return new ClientCommand { Cmd = "timer", Mode = "countdown", Minutes = minutes };
        return new ClientCommand { Cmd = "timer", Mode = "countdown", Minutes = 25 };
    }
}
