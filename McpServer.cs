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

// A capture as the agent receives it: the PNG, its size, the window brought
// to the front for it (null: the screen under the mouse).
internal sealed record ScreenLook(byte[] Png, int Width, int Height, string? Window);

// A spot the agent circles (show_me), in pixels of its last look. No
// Width/Height: a single icon or button.
internal sealed record GuideSpot(double X, double Y, double? Width, double? Height, string? Label);

// A zone the agent frames and explains (explain_screen), in pixels of its last look.
internal sealed record GuideArea(double Left, double Top, double Width, double Height, string Title, string? Text);

// What the screen tools act on: the screen and the overlay (App, on its UI thread).
internal interface IScreenGuide
{
    // Front to back
    Task<IReadOnlyList<TopWindow>> ListWindowsAsync();
    // No look when no window matches: the error lists the open ones.
    Task<(ScreenLook? Look, string? Error)> LookAsync(string? window, bool grid);
    // null when shown; otherwise why not.
    Task<string?> ShowAsync(GuideSpot spot);
    // Same; numbered in the list's order.
    Task<string?> ExplainAsync(IReadOnlyList<GuideArea> areas);
}

// A minimal MCP server (Streamable HTTP, JSON responses only) so an AI agent
// — Claude Code, Cursor… — can list, add and edit the Prompt Builder's
// prompts, and show the user where to click on their screen or explain its
// parts. No SDK: the protocol here is three JSON-RPC methods and seven
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
    // One color each (the palette's nine), single-digit numbers
    private const int MaxZones = 9;
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
    private readonly IScreenGuide _screen;

    internal McpServer(IPromptStore store, IScreenGuide screen)
    {
        _store = store;
        _screen = screen;
    }

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
            ["instructions"] = Instructions,
        };
    }

    // For agents, not people: dense, read in a fresh chat that knows nothing
    // of this app. Kept short enough that clients don't truncate it; the
    // per-tool detail and troubleshooting live in the tool descriptions.
    private const string Instructions = """
        ScreenSearchOverlay (LEPASOFT), Windows tray app: OCR screen search (Ctrl+Alt+F), Prompt Builder (Ctrl+Alt+G: selected text + a saved prompt -> ChatGPT). This server = its local MCP bridge: loopback only, started by the user in Prompt Builder ⚙ > MCP server, off at every app start. Calls fail with ECONNREFUSED -> app closed or box unticked: ask the user to tick it again, then retry.

        PROMPTS (list_prompts, add_prompt, update_prompt)
        Prompt = instruction placed before the user's selected text, e.g. "Fix the spelling of this text:". list_prompts before adding: no duplicates. No delete tool, by design.

        SCREEN GUIDE (list_windows, look_at_screen, show_me, explain_screen)
        Why: you guide the user through an app on their PC and words fail ("the icon on the right", among 50) -> you see their window, you draw on their real screen.
        When: "where?", "I can't find it", "show me", "circle it", "explain this screen", user lost in a dense UI (Blender, Unity, game/level editors, settings dialogs, any app). Not when you can answer without seeing.
        Usual flow (a default, not a script):
        1. App name unknown -> list_windows. Obvious name -> skip it.
        2. look_at_screen(window = process name) -> app comes to the front; you get its frontmost window as an image with a coordinate grid.
        3. Locate the target. x/y/left/top = pixels of THAT image; the server maps them to the real screen and monitor.
        4. One spot to click or look at -> show_me. Parts of a screen to teach -> explain_screen, plus the same numbered explanation in chat.
        5. In chat: what you marked, how sure you are, then "Esc or a click closes it, then do it in the app" (a click on the overlay only closes it, it never reaches the app).
        6. User acted (menu opened, dialog, new tab) -> look_at_screen again before the next mark: the screen changed, old coordinates are void, only the latest capture is kept.
        Every app, workflow, UI language and Windows setup differs (themes, scaling, several monitors, localized titles): adapt. Explore with several looks, mark a whole zone when a spot is uncertain, mix tools, ask the user what they see when the image is ambiguous.
        Defaults worth keeping:
        - Always pass window: without it you get the whole monitor under the mouse, usually the chat itself.
        - Process names don't change with the OS language or the open file; titles do ("Paramètres", "Einstellungen"). Prefer the process name.
        - You see pixels, not the app's logic. Unsure what a control does -> say "probably" (label + chat); app source or docs at hand -> check them.
        - Labels, titles, texts: the user's language, which may differ from the app's.
        - Privacy: capture only when it helps; ignore unrelated private content in the image.
        - Cost: a look ~1.5k image tokens. One look per user step; never poll.
        """;

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
            new JsonObject
            {
                ["name"] = "list_windows",
                ["description"] = """
                    Open windows, front to back, one per line: "title (process)". Text only, instant, no image.
                    #1 is usually where the user talks to you (editor, terminal, browser); #2 usually the app they were just using, the one they mean by "my software" or "the game". Not always: judge by the titles.
                    Use it when you don't know the app's name, before look_at_screen. Give look_at_screen the process name (in parentheses), not the title: titles change with the open file or project and the OS language, and can match other windows.
                    Several windows of one process (dialogs, tool windows, several projects): look_at_screen takes the frontmost; a title fragment picks another one.
                    """,
                ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            },
            new JsonObject
            {
                ["name"] = "look_at_screen",
                ["description"] = """
                    Capture one app's window and see it, to find where something is in the app you are guiding the user through.
                    With window: the app comes to the front (restored if minimized, on whichever monitor it is), then only its frontmost window is captured; an open dialog or settings window of that app wins over its main window. Without window: the whole monitor under the mouse (usually the chat: avoid, unless the user asks for their whole screen).
                    Matching: exact process name first (case-insensitive, ".exe" optional), else a fragment of a title, front to back. A fragment can hit the wrong window (an editor whose project is named like the app) -> use the process name from list_windows.
                    Image: <=1568 px on its longest side and <=1.15 MP, small windows at full resolution. Faint magenta grid every 100 px, numbered along the bottom and right edges; grid=false for a clean image (e.g. to read fine text). Coordinates for show_me and explain_screen are pixels of this image.
                    Troubleshooting:
                    - "No open window matches" -> the error lists open windows; retry with a process name.
                    - Wrong window -> retry with the exact process name, or a title fragment unique to the one you want.
                    - Black or empty image -> exclusive-fullscreen game, DRM video or locked session: ask for windowed or borderless mode.
                    - Target not visible (closed menu, collapsed panel, other tab, dropdown, scrolled away, another workspace/mode) -> show_me what reveals it first, look again once the user opened it.
                    - Icons too small or alike to tell apart -> grid=false and describe the target by its neighbours, or circle the small group and name the right one.
                    """,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["window"] = new JsonObject
                        {
                            ["type"] = "string",
                            ["description"] = "App to bring to the front: process name, best taken from list_windows (\"blender\", \"LSDE\"), or a fragment of its window title.",
                        },
                        ["grid"] = new JsonObject { ["type"] = "boolean", ["description"] = "Draw the numbered coordinate grid (default true). false: a clean image, e.g. to read fine text or tiny icons." },
                    },
                },
            },
            new JsonObject
            {
                ["name"] = "show_me",
                ["description"] = """
                    Circle one spot on the user's real screen: an overlay over the latest look_at_screen capture (frozen picture), rest of the screen dimmed, a red ring drawn like a pen stroke then rippling, the label in a bubble beside it.
                    x, y = center of the target, in pixels of that image. Single icon or button: no width/height (fixed ring). Panel, toolbar, list, group of icons, or a spot you are not sure of: width/height of the zone (image px), the ring encloses it.
                    label: short (about 12 words max), the user's language, what to do + what it is: "Click the gear: project settings". Unsure -> "Probably...".
                    One call = one mark; a new call replaces the previous one. Several things at once -> explain_screen. Multi-step task -> one show_me per step, a new look_at_screen after each user action.
                    The overlay is a picture: Esc or a click closes it, the click does not reach the app. Tell the user to close it, then act.
                    User says it is off target -> look_at_screen again (window moved, screen changed), re-locate, retry; widen with width/height when unsure.
                    Errors: "Call look_at_screen first" -> no capture yet; "outside the image" -> coordinates beyond the latest image size (given in the error).
                    """,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["x"] = new JsonObject { ["type"] = "number", ["description"] = "Center of the spot, in pixels of the look_at_screen image." },
                        ["y"] = new JsonObject { ["type"] = "number", ["description"] = "Center of the spot, in pixels of the look_at_screen image." },
                        ["width"] = new JsonObject { ["type"] = "number", ["description"] = "Width of the zone to circle, in image pixels. Omit for one button or icon." },
                        ["height"] = new JsonObject { ["type"] = "number", ["description"] = "Height of the zone to circle, in image pixels. Omit for one button or icon." },
                        ["label"] = new JsonObject { ["type"] = "string", ["description"] = "Short text beside the circle, in the user's language: what to do + what it is, e.g. \"Click the gear: project settings\"." },
                    },
                    ["required"] = new JsonArray("x", "y"),
                },
            },
            new JsonObject
            {
                ["name"] = "explain_screen",
                ["description"] = """
                    Teach how a screen is laid out: an overlay over the latest look_at_screen capture, rest dimmed, each zone framed in its own color with its number and title on a tab, frames drawn one after the other, a legend of your explanations beside them.
                    Zones (1-9) are numbered in the order given: order them the way the screen is read or the workflow runs (often top-left to bottom-right; a workflow may go elsewhere). Colors by number: 1 red, 2 orange, 3 yellow, 4 green, 5 teal, 6 sky blue, 7 indigo, 8 purple, 9 pink; usable in chat ("the orange frame").
                    Each zone: left, top, width, height in image pixels (top-left corner + size; a zone past the image edge is cut at it). title: 1-3 words, on the frame. text: 1-2 sentences, what it is for and when to use it; a zone without text gets no legend line.
                    Good zones: whole functional regions (menu bar, toolbar, side panel, property group, timeline, one row of a list), not overlapping, not nested: an inner frame's tab hides the outer one. 3-6 zones read best; more detail -> a second pass on one region after the user is done.
                    Legend: compact, beside the frames when there is room; over a maximized app it sits at the bottom and slides to the half of the screen the mouse is not on. A click on it keeps the overlay open; Esc or a click elsewhere closes it.
                    Then write the same numbered explanation in the chat: the overlay closes, the chat stays. Unsure what a zone does -> say so in its text.
                    Fits any app and language: describe what you see, in the user's language, and adapt the depth to their level.
                    """,
                ["inputSchema"] = new JsonObject
                {
                    ["type"] = "object",
                    ["properties"] = new JsonObject
                    {
                        ["zones"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["minItems"] = 1,
                            ["maxItems"] = MaxZones,
                            ["items"] = new JsonObject
                            {
                                ["type"] = "object",
                                ["properties"] = new JsonObject
                                {
                                    ["left"] = new JsonObject { ["type"] = "number", ["description"] = "Left edge of the zone, in image pixels." },
                                    ["top"] = new JsonObject { ["type"] = "number", ["description"] = "Top edge of the zone, in image pixels." },
                                    ["width"] = new JsonObject { ["type"] = "number", ["description"] = "Width of the zone, in image pixels." },
                                    ["height"] = new JsonObject { ["type"] = "number", ["description"] = "Height of the zone, in image pixels." },
                                    ["title"] = new JsonObject { ["type"] = "string", ["description"] = "Its name in a few words, shown on the frame, in the user's language." },
                                    ["text"] = new JsonObject { ["type"] = "string", ["description"] = "What it is for, a sentence or two, shown in the legend, in the user's language." },
                                },
                                ["required"] = new JsonArray("left", "top", "width", "height", "title"),
                            },
                        },
                    },
                    ["required"] = new JsonArray("zones"),
                },
            },
        ];
    }

    private async Task<JsonObject> CallToolAsync(JsonNode id, JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var args = parameters?["arguments"] as JsonObject ?? [];
        string? Text(string key) => TextIn(args, key);
        double? Number(string key) => NumberIn(args, key);

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

            case "list_windows":
            {
                var windows = await _screen.ListWindowsAsync();
                return Result(id, ToolText(string.Join("\n", windows.Select(w => $"{w.Title} ({w.Process})"))));
            }

            case "look_at_screen":
            {
                var grid = args["grid"] is not JsonValue g || !g.TryGetValue<bool>(out var drawn) || drawn;
                var (look, error) = await _screen.LookAsync(Text("window"), grid);
                if (look is null)
                    return Result(id, ToolText(error ?? "Capture failed.", isError: true));
                var what = look.Window is null ? "Screen under the mouse" : $"Window \"{look.Window}\"";
                var text = $"{what}, as a {look.Width}×{look.Height} px image"
                    + (grid ? " (grid every 100 px)" : "") + ". show_me takes x/y in these pixels.";
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray(
                        new JsonObject { ["type"] = "text", ["text"] = text },
                        new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(look.Png), ["mimeType"] = "image/png" }),
                    ["isError"] = false,
                });
            }

            case "show_me":
            {
                if (Number("x") is not { } x || Number("y") is not { } y)
                    return Result(id, ToolText("x and y are required.", isError: true));
                var width = Number("width");
                var height = Number("height");
                if (width <= 0 || height <= 0)
                    return Result(id, ToolText("width and height must be positive.", isError: true));
                var error = await _screen.ShowAsync(new GuideSpot(x, y, width, height, Text("label")?.Trim()));
                return error is null
                    ? Result(id, ToolText("Shown on the user's screen. They close it with Esc or a click."))
                    : Result(id, ToolText(error, isError: true));
            }

            case "explain_screen":
            {
                if (args["zones"] is not JsonArray list || list.Count == 0)
                    return Result(id, ToolText("zones is required: one or more {left, top, width, height, title}.", isError: true));
                if (list.Count > MaxZones)
                    return Result(id, ToolText($"At most {MaxZones} zones.", isError: true));
                var areas = new List<GuideArea>();
                foreach (var item in list)
                {
                    if (item is not JsonObject zone
                        || NumberIn(zone, "left") is not { } left || NumberIn(zone, "top") is not { } top
                        || NumberIn(zone, "width") is not { } width || NumberIn(zone, "height") is not { } height
                        || TextIn(zone, "title")?.Trim() is not { Length: > 0 } title)
                        return Result(id, ToolText("Each zone needs left, top, width, height and title.", isError: true));
                    if (width <= 0 || height <= 0)
                        return Result(id, ToolText($"Zone \"{title}\": width and height must be positive.", isError: true));
                    areas.Add(new GuideArea(left, top, width, height, title, TextIn(zone, "text")?.Trim()));
                }
                var error = await _screen.ExplainAsync(areas);
                return error is null
                    ? Result(id, ToolText($"{areas.Count} zones shown on the user's screen. They close it with Esc or a click."))
                    : Result(id, ToolText(error, isError: true));
            }

            default:
                return Error(id, -32602, $"Unknown tool: {name}");
        }
    }

    private static string? TextIn(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static double? NumberIn(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;

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
