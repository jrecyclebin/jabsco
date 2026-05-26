using System.Runtime.CompilerServices;
using System.Text.Json;
using Jabsco.Common.Events;
using Jabsco.Core.Approval;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using Jabsco.Core.Skills;
using SkiaSharp;

namespace Jabsco.Core.Agent;

public sealed class AgentLoop
{
    private readonly IRdpClient _rdp;
    private readonly IComputerUseProvider _provider;
    private readonly IApprovalSink _approval;
    private readonly List<ConversationTurn> _history;
    private readonly PolicyMatcher? _matcher;

    public IReadOnlyList<ConversationTurn> History => _history;

    public AgentLoop(IRdpClient rdp, IComputerUseProvider provider, IApprovalSink approval,
        IEnumerable<ConversationTurn>? initialHistory = null, PolicyMatcher? matcher = null)
    {
        _rdp = rdp;
        _provider = provider;
        _approval = approval;
        _history = initialHistory?.ToList() ?? [];
        _matcher = matcher;
    }

    public async IAsyncEnumerable<AgentEvent> RunAsync(
        string prompt,
        AgentOptions options,
        ToolPolicy? policy = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var started = DateTimeOffset.UtcNow;
        int step = 0;
        int inputTokens = 0;
        int outputTokens = 0;
        int cachedInputTokens = 0;
        var currentTurns = new List<ToolTurn>();
        StoppedReason stopped = StoppedReason.ModelDone;
        int cursorX = -1, cursorY = -1;

        while (true)
        {
            if (ct.IsCancellationRequested) { stopped = StoppedReason.UserCancel; break; }
            step++;

            if (step > options.MaxSteps) { stopped = StoppedReason.StepBudget; break; }
            if (options.TimeBudget.HasValue && DateTimeOffset.UtcNow - started > options.TimeBudget.Value) { stopped = StoppedReason.TimeBudget; break; }

            byte[] png;
            ErrorEvent? err = null;
            try { png = await _rdp.CaptureScreenshotPngAsync(ct); }
            catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
            catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); png = []; }
            if (err != null) { yield return err; yield break; }

            yield return new ScreenshotEvent(png, DateTimeOffset.UtcNow, step);

            // Annotate with agent cursor so the model can see its own position
            var annotated = cursorX >= 0 ? DrawCursorOnPng(png, cursorX, cursorY) : png;

            var request = new ProviderRequest(annotated, prompt, _history, currentTurns, new ProviderOptions());
            ProviderResponse? response = null;
            err = null;
            try { response = await _provider.NextActionAsync(request, ct); }
            catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
            catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); }
            if (err != null) { yield return err; yield break; }

            inputTokens       += response!.Usage.InputTokens;
            outputTokens      += response.Usage.OutputTokens;
            cachedInputTokens += response.Usage.CachedInputTokens;

            if (response.Thinking is { } thinking)
                yield return new ThinkingEvent(thinking, DateTimeOffset.UtcNow, step);

            if (response.Action is DoneAction done)
            {
                _history.Add(new ConversationTurn(prompt, currentTurns, done.Response));
                var stats = new AgentStats(step, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds, inputTokens, outputTokens, cachedInputTokens, _provider.ModelId, StoppedReason.ModelDone);
                yield return new FinalEvent(done.Response, stats, DateTimeOffset.UtcNow, step);
                yield break;
            }

            yield return new ActionEvent(response.Action, DateTimeOffset.UtcNow, step);

            if (policy != null && _matcher != null)
            {
                var payloadStr = JsonSerializer.Serialize(response.Action, response.Action.GetType());
                var decision = _matcher.Match(policy, ActionToolName(response.Action), payloadStr);

                if (decision == ToolDecision.Deny)
                {
                    currentTurns.Add(new ToolTurn(response.Action, "denied by policy", response.ToolUseId));
                    yield return new ToolResultEvent("denied by policy", DateTimeOffset.UtcNow, step);
                    continue;
                }

                if (decision == ToolDecision.Prompt)
                {
                    JsonElement payloadElement;
                    using (var doc = JsonDocument.Parse(payloadStr))
                        payloadElement = doc.RootElement.Clone();
                    yield return new ApprovalRequestEvent(ActionToolName(response.Action), payloadElement, DateTimeOffset.UtcNow, step);
                    var sinkDecision = await _approval.RequestAsync(ActionToolName(response.Action), payloadStr, TimeSpan.FromSeconds(30), ct);
                    if (sinkDecision == ToolDecision.Deny)
                    {
                        currentTurns.Add(new ToolTurn(response.Action, "denied by user", response.ToolUseId));
                        yield return new ToolResultEvent("denied by user", DateTimeOffset.UtcNow, step);
                        continue;
                    }
                }
            }

            string summary = string.Empty;
            err = null;
            try { summary = await ExecuteActionAsync(response.Action, ct); }
            catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
            catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); }

            if (err != null) { yield return err; yield break; }

            (cursorX, cursorY) = response.Action switch
            {
                ClickAction c     => (c.X, c.Y),
                MouseMoveAction m => (m.X, m.Y),
                DragAction d      => (d.EndX, d.EndY),
                _                 => (cursorX, cursorY)
            };

            currentTurns.Add(new ToolTurn(response.Action, summary, response.ToolUseId));

            if (options.PostActionDelay > TimeSpan.Zero)
            {
                try { await Task.Delay(options.PostActionDelay, ct); }
                catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
            }

            yield return new ToolResultEvent(summary, DateTimeOffset.UtcNow, step);
        }

        if (currentTurns.Count > 0)
            _history.Add(new ConversationTurn(prompt, currentTurns, FinalResponse: null));

        var finalStats = new AgentStats(step, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds, inputTokens, outputTokens, cachedInputTokens, _provider.ModelId, stopped);
        yield return new FinalEvent(string.Empty, finalStats, DateTimeOffset.UtcNow, step);
    }

    private static string ActionToolName(AgentAction action) => action switch
    {
        ClickAction      => "click",
        TypeAction       => "type",
        KeyAction        => "key",
        ScrollAction     => "scroll",
        MouseMoveAction  => "move",
        DragAction       => "drag",
        ScreenshotAction => "screenshot",
        LoadSkillAction  => "load_skill",
        _                => action.GetType().Name.ToLowerInvariant()
    };

    // Composites a red arrow cursor onto the PNG at (x, y) so the model can see its position.
    private static byte[] DrawCursorOnPng(byte[] png, int x, int y)
    {
        using var bitmap = SKBitmap.Decode(png);
        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height));
        using var canvas = surface.Canvas;
        canvas.DrawBitmap(bitmap, 0, 0);

        // Scale cursor to ~2% of image height for visibility at full RDP resolution
        float s = bitmap.Height / 800f * 1.5f;

        using var path = new SKPath();
        path.MoveTo(x,           y);
        path.LineTo(x,           y + 17 * s);
        path.LineTo(x + 4  * s, y + 13 * s);
        path.LineTo(x + 7  * s, y + 20 * s);
        path.LineTo(x + 9  * s, y + 19 * s);
        path.LineTo(x + 6  * s, y + 12 * s);
        path.LineTo(x + 11 * s, y + 12 * s);
        path.Close();

        using var fill   = new SKPaint { Color = new SKColor(220, 20, 60), IsAntialias = true };
        using var stroke = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f * s, IsAntialias = true };
        canvas.DrawPath(path, fill);
        canvas.DrawPath(path, stroke);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private async Task<string> ExecuteActionAsync(AgentAction action, CancellationToken ct) => action switch
    {
        ScreenshotAction => "screenshot taken",
        LoadSkillAction ls => ExecuteLoadSkill(ls),
        ClickAction c => await ExecuteClickAsync(c, ct),
        MouseMoveAction m => await ExecuteMouseMoveAsync(m, ct),
        DragAction d => await ExecuteDragAsync(d, ct),
        ScrollAction s => await ExecuteScrollAsync(s, ct),
        KeyAction k => await ExecuteKeyAsync(k, ct),
        TypeAction t => await ExecuteTypeAsync(t, ct),
        _ => "unknown action"
    };

    private static string ExecuteLoadSkill(LoadSkillAction ls)
    {
        try { return SkillLoader.Load(ls.SkillName); }
        catch (FileNotFoundException ex) { return $"Error: {ex.Message}"; }
    }

    private async Task<string> ExecuteClickAsync(ClickAction c, CancellationToken ct)
    {
        for (int i = 0; i < c.Clicks; i++)
        {
            await _rdp.MouseClickAsync(c.Button, c.X, c.Y, ct);
            if (i < c.Clicks - 1) await Task.Delay(50, ct);
        }
        return $"{c.Button} click at ({c.X},{c.Y})";
    }

    private async Task<string> ExecuteMouseMoveAsync(MouseMoveAction m, CancellationToken ct)
    {
        await _rdp.MouseMoveAsync(m.X, m.Y, ct);
        return $"mouse moved to ({m.X},{m.Y})";
    }

    private async Task<string> ExecuteDragAsync(DragAction d, CancellationToken ct)
    {
        await _rdp.MouseMoveAsync(d.StartX, d.StartY, ct);
        await _rdp.MouseClickAsync(MouseButton.Left, d.StartX, d.StartY, ct);
        await _rdp.MouseMoveAsync(d.EndX, d.EndY, ct);
        await _rdp.MouseClickAsync(MouseButton.Left, d.EndX, d.EndY, ct);
        return $"drag from ({d.StartX},{d.StartY}) to ({d.EndX},{d.EndY})";
    }

    private async Task<string> ExecuteScrollAsync(ScrollAction s, CancellationToken ct)
    {
        await _rdp.MouseScrollAsync(s.X, s.Y, s.Direction, s.Amount, ct);
        return $"scroll {s.Direction} {s.Amount}x at ({s.X},{s.Y})";
    }

    private async Task<string> ExecuteKeyAsync(KeyAction k, CancellationToken ct)
    {
        await _rdp.KeyPressAsync(k.Keys, ct);
        return $"key: {k.Keys}";
    }

    private async Task<string> ExecuteTypeAsync(TypeAction t, CancellationToken ct)
    {
        await _rdp.TypeTextAsync(t.Text, ct);
        return $"typed: {t.Text[..Math.Min(t.Text.Length, 30)]}";
    }
}
