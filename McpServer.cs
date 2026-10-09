using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ScreenSearchOverlay;

// A saved prompt as an agent sees it.
internal sealed record PromptInfo(string Id, string Name, string Prompt, string Color);

// What the MCP tools act on: the live prompt list (App, on its UI thread).
internal interface IPromptStore
{
    Task<IReadOnlyList<PromptInfo>> ListAsync();
    Task<PromptInfo> AddAsync(string name, string prompt, string color);
    Task<PromptInfo?> UpdateAsync(string id, string? name, string? prompt, string? color);
}

// A minimal MCP server (Streamable HTTP, JSON responses only) so an AI agent
// — Claude Code, Cursor… — can list, add and edit the Prompt Builder's
// prompts. No SDK: the protocol here is three JSON-RPC methods and three
// tools. No delete tool on purpose: an agent that gets it wrong can't lose
// anything.
//
// Loopback only (http.sys "localhost" prefix: no admin rights needed, not
// reachable from the network). A request carrying an Origin header comes
// from a web page, never from an agent: refused, so no site can write here.
internal sealed class McpServer : IDisposable
{
    internal const int DefaultPort = 47821;
    internal const string ServerName = "ls-ocrlayout";
    private static readonly string[] ProtocolVersions = ["2025-06-18", "2025-03-26", "2024-11-05"];

    // The palette of the Prompt Builder, by name for the agents.
    private static readonly Dictionary<string, string> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = "",
        ["red"] = "#F87171",
        ["orange"] = "#FB923C",
        ["yellow"] = "#FACC15",
        ["green"] = "#4ADE80",
        ["teal"] = "#2DD4BF",
        ["sky"] = "#38BDF8",
        ["indigo"] = "#818CF8",
        ["purple"] = "#C084FC",
        ["pink"] = "#F472B6",
    };

    private readonly HttpListener _listener = new();
    private readonly IPromptStore _store;

    internal McpServer(IPromptStore store) => _store = store;

    internal static string Url(int port) => $"http://localhost:{port}/mcp";

    // Throws when the port is taken (HttpListenerException).
    internal void Start(int port)
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();
        _ = Task.Run(LoopAsync);
    }

    public void Dispose()
    {
        try { _listener.Close(); } catch { }
    }

    private async Task LoopAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch { return; } // stopped
            _ = Task.Run(() => HandleAsync(context));
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        try
        {
            var path = request.Url?.AbsolutePath.TrimEnd('/') ?? "";
            if (path != "/mcp") { response.StatusCode = 404; return; }
            if (request.Headers["Origin"] is not null) { response.StatusCode = 403; return; }
            if (request.HttpMethod != "POST")
            {
                // No server-to-client stream: every answer comes with its request.
                response.StatusCode = 405;
                response.AddHeader("Allow", "POST");
                return;
            }

            string body;
            using (var reader = new StreamReader(request.InputStream, Encoding.UTF8))
                body = await reader.ReadToEndAsync();

            JsonNode? message;
            try { message = JsonNode.Parse(body); }
            catch (JsonException) { await WriteAsync(response, Error(null, -32700, "Parse error")); return; }
            if (message is not JsonObject call) { await WriteAsync(response, Error(null, -32600, "Invalid request")); return; }

            // A notification (no id) gets no answer.
            var id = call["id"]?.DeepClone();
            if (id is null) { response.StatusCode = 202; return; }

            var method = call["method"]?.GetValue<string>() ?? "";
            var parameters = call["params"] as JsonObject;
            var reply = method switch
            {
                "initialize" => Result(id, Initialize(parameters)),
                "ping" => Result(id, new JsonObject()),
                "tools/list" => Result(id, new JsonObject { ["tools"] = Tools() }),
                "tools/call" => await CallToolAsync(id, parameters),
                _ => Error(id, -32601, $"Method not found: {method}"),
            };
            await WriteAsync(response, reply);
        }
        catch
        {
            response.StatusCode = 500;
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    private static JsonObject Initialize(JsonObject? parameters)
    {
        var asked = parameters?["protocolVersion"]?.GetValue<string>();
        return new JsonObject
        {
            ["protocolVersion"] = ProtocolVersions.Contains(asked) ? asked : ProtocolVersions[0],
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = Updater.Current.ToString() },
            ["instructions"] = "Prompts of the ScreenSearchOverlay Prompt Builder. A prompt is an instruction sent to ChatGPT "
                + "before the user's selected text, e.g. \"Fix the spelling of this text:\". Call list_prompts before adding, "
                + "to avoid duplicates.",
        };
    }

    private static JsonArray Tools()
    {
        var color = new JsonObject
        {
            ["type"] = "string",
            ["description"] = "Title color: default, red, orange, yellow, green, teal, sky, indigo, purple, pink, or #RRGGBB.",
        };
        return
        [
            new JsonObject
            {
                ["name"] = "list_prompts",
                ["description"] = "List the saved prompts (id, name, prompt text, color), in the order the user sees them.",
                ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            },
            new JsonObject
            {
                ["name"] = "add_prompt",
                ["description"] = "Add a prompt at the top of the list. The prompt is the instruction placed before the user's text; "
                    + "end it with a colon, e.g. \"Translate this text into Spanish:\".",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["name"] = new JsonObject { ["type"] = "string", ["description"] = "Short title shown in the list." },
                        ["prompt"] = new JsonObject { ["type"] = "string", ["description"] = "The instruction text." },
                        ["color"] = color.DeepClone(),
                    },
                    ["required"] = new JsonArray("name", "prompt"),
                },
            },
            new JsonObject
            {
                ["name"] = "update_prompt",
                ["description"] = "Change a prompt's name, text or color. Only the fields given change.",
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["id"] = new JsonObject { ["type"] = "string", ["description"] = "The id from list_prompts." },
                        ["name"] = new JsonObject { ["type"] = "string" },
                        ["prompt"] = new JsonObject { ["type"] = "string" },
                        ["color"] = color.DeepClone(),
                    },
                    ["required"] = new JsonArray("id"),
                },
            },
        ];
    }

    private async Task<JsonObject> CallToolAsync(JsonNode id, JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var args = parameters?["arguments"] as JsonObject ?? [];
        string? Text(string key) => args[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

        switch (name)
        {
            case "list_prompts":
                return Result(id, ToolText(JsonSerializer.Serialize(await _store.ListAsync(), JsonOptions)));

            case "add_prompt":
            {
                var title = Text("name")?.Trim();
                var prompt = Text("prompt")?.Trim();
                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(prompt))
                    return Result(id, ToolText("name and prompt are required.", isError: true));
                if (!TryColor(Text("color"), out var hex))
                    return Result(id, ToolText("Unknown color. Use a palette name or #RRGGBB.", isError: true));
                var added = await _store.AddAsync(title, prompt, hex ?? "");
                return Result(id, ToolText("Added: " + JsonSerializer.Serialize(added, JsonOptions)));
            }

            case "update_prompt":
            {
                var target = Text("id");
                if (string.IsNullOrEmpty(target))
                    return Result(id, ToolText("id is required.", isError: true));
                if (!TryColor(Text("color"), out var hex))
                    return Result(id, ToolText("Unknown color. Use a palette name or #RRGGBB.", isError: true));
                var updated = await _store.UpdateAsync(target, Text("name")?.Trim(), Text("prompt")?.Trim(), hex);
                return updated is null
                    ? Result(id, ToolText($"No prompt with id {target}. Call list_prompts.", isError: true))
                    : Result(id, ToolText("Updated: " + JsonSerializer.Serialize(updated, JsonOptions)));
            }

            default:
                return Error(id, -32602, $"Unknown tool: {name}");
        }
    }

    // null in: no change (hex null). A palette name or #RRGGBB: its hex.
    private static bool TryColor(string? value, out string? hex)
    {
        hex = null;
        if (value is null) return true;
        if (NamedColors.TryGetValue(value.Trim(), out var named)) { hex = named; return true; }
        var v = value.Trim();
        if (v.Length == 7 && v[0] == '#' && v[1..].All(Uri.IsHexDigit)) { hex = v.ToUpperInvariant(); return true; }
        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static JsonObject ToolText(string text, bool isError = false) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    private static JsonObject Result(JsonNode id, JsonNode result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private static async Task WriteAsync(HttpListenerResponse response, JsonObject reply)
    {
        var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString(JsonOptions));
        response.StatusCode = 200;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
    }
}
