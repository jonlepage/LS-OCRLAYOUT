namespace ScreenSearchOverlay;

// The MCP server for AI agents (see McpServer). Off at every start, on only
// while the user wants an agent to add prompts — so it is never saved.
// Fixed port: the install command the agent keeps never changes.
public partial class App : IPromptStore
{
    private McpServer? _mcp;

    internal bool McpEnabled => _mcp is not null;
    internal string McpUrl => McpServer.Url(McpServer.DefaultPort);
    // Why the last start failed (port taken…); null when it didn't.
    internal string? McpError { get; private set; }

    internal event Action? McpChanged;

    internal void SetMcpEnabled(bool enabled)
    {
        StopMcp();
        McpError = null;
        if (enabled)
        {
            var server = new McpServer(this);
            try
            {
                server.Start(McpServer.DefaultPort);
                _mcp = server;
            }
            catch (Exception ex)
            {
                server.Dispose();
                McpError = ex.Message;
            }
        }
        McpChanged?.Invoke();
    }

    private void StopMcp()
    {
        _mcp?.Dispose();
        _mcp = null;
    }

    // ── IPromptStore: called from the server's threads, run on the UI thread
    // (the prompt list belongs to it and the Prompt Builder shows it live) ──

    Task<IReadOnlyList<PromptInfo>> IPromptStore.ListAsync() =>
        Dispatcher.InvokeAsync(() => (IReadOnlyList<PromptInfo>)Prompts.Select(Info).ToList()).Task;

    Task<PromptInfo> IPromptStore.AddAsync(string name, string prompt, string color) =>
        Dispatcher.InvokeAsync(() =>
        {
            var added = new SavedPrompt { Name = name, Prompt = prompt, TitleColor = color };
            Prompts.Insert(0, added);
            SavePromptsInBackground();
            return Info(added);
        }).Task;

    Task<PromptInfo?> IPromptStore.UpdateAsync(string id, string? name, string? prompt, string? color) =>
        Dispatcher.InvokeAsync(() =>
        {
            var target = Prompts.FirstOrDefault(p => p.Id == id);
            if (target is null) return null;
            if (name is not null) target.Name = name;
            if (prompt is not null) target.Prompt = prompt;
            if (color is not null) target.TitleColor = color;
            SavePromptsInBackground();
            return (PromptInfo?)Info(target);
        }).Task;

    private static PromptInfo Info(SavedPrompt p) => new(p.Id, p.Name, p.Prompt, p.TitleColor);
}
