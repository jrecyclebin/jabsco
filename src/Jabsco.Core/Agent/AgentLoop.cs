using System.Runtime.CompilerServices;
using System.Text.Json;
using Jabsco.Common.Events;
using Jabsco.Core.Approval;
using Jabsco.Core.Config;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using Jabsco.Core.Skills;
using SkiaSharp;

namespace Jabsco.Core.Agent;

public sealed class AgentLoop
{
    private readonly IConnection _connection;
    private readonly IComputerUseProvider _provider;
    private readonly IApprovalSink _approval;
    private readonly List<ConversationTurn> _history;
    private readonly PolicyMatcher? _matcher;

    public IReadOnlyList<ConversationTurn> History => _history;

    public AgentLoop(IConnection connection, IComputerUseProvider provider, IApprovalSink approval,
        IEnumerable<ConversationTurn>? initialHistory = null, PolicyMatcher? matcher = null)
    {
        _connection = connection;
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
        byte[]? promptScreenshot = null;

        while (true)
        {
            if (ct.IsCancellationRequested) { stopped = StoppedReason.UserCancel; break; }
            step++;

            if (step > options.MaxSteps) { stopped = StoppedReason.StepBudget; break; }
            if (options.TimeBudget.HasValue && DateTimeOffset.UtcNow - started > options.TimeBudget.Value) { stopped = StoppedReason.TimeBudget; break; }

            var screen = _connection.Screen;
            ErrorEvent? err = null;
            string? observation = null;

            if (screen is not null)
            {
                byte[] png;
                try { png = await screen.CaptureScreenshotPngAsync(ct); }
                catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
                catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); png = []; }
                if (err != null) { yield return err; yield break; }

                // Annotate with agent cursor so the model can see its own position
                var annotated = cursorX >= 0 ? DrawCursorOnPng(png, cursorX, cursorY) : png;
                // The prompt screenshot is the round's first frame, captured once. Once it's
                // pruned below it stays gone rather than being re-captured as a later frame.
                if (step == 1) promptScreenshot = annotated;
                yield return new ScreenshotEvent(annotated, DateTimeOffset.UtcNow, step);

                // This capture is the result of the previous action; keep it on that turn if the
                // strategy can use it, then drop any screenshots the strategy won't show again.
                if (currentTurns.Count > 0 && currentTurns[^1].ScreenshotPng is null
                    && ScreenshotPlan.Records(currentTurns[^1].Action, options.ModelStrategy))
                    currentTurns[^1] = currentTurns[^1] with { ScreenshotPng = annotated };

                var retained = ScreenshotPlan.Retained(_history, currentTurns, promptScreenshot, options.ModelStrategy);
                FreePrunedScreenshots(currentTurns, retained);
                // The prompt screenshot isn't on a record yet, so prune it here too; what reaches
                // the provider is exactly what survives, which the provider attaches on presence.
                if (!retained.Contains("p:current"))
                    promptScreenshot = null;
            }
            else
            {
                // No screen: the model perceives the connection through text instead of a frame.
                try { observation = await _connection.DescribeAsync(ct); }
                catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
                catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); }
                if (err != null) { yield return err; yield break; }
            }

            var request = new ProviderRequest(prompt, _history, currentTurns,
                new ProviderOptions(Strategy: options.ModelStrategy, HasScreen: screen is not null, HasVmHost: _connection.HasVmHost),
                promptScreenshot, observation);
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
                _history.Add(new ConversationTurn(prompt, currentTurns, done.Response, promptScreenshot));
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
                    currentTurns.Add(new ToolTurn(response.Action, "denied by policy", response.ToolUseId!));
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
                        currentTurns.Add(new ToolTurn(response.Action, "denied by user", response.ToolUseId!));
                        yield return new ToolResultEvent("denied by user", DateTimeOffset.UtcNow, step);
                        continue;
                    }
                }
            }

            string summary = string.Empty;
            err = null;
            if (screen is null && RequiresScreen(response.Action))
            {
                summary = "No screen is connected. Use the switch tool to connect to a VM or RDP profile before using computer tools.";
            }
            else
            {
                try { summary = await ExecuteActionAsync(response.Action, screen, ct); }
                catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
                catch (Exception ex) { err = new ErrorEvent(ex.Message, ex.GetType().Name, DateTimeOffset.UtcNow, step); }
            }

            if (err != null) { yield return err; yield break; }

            (cursorX, cursorY) = response.Action switch
            {
                ClickAction c     => (c.X, c.Y),
                MouseMoveAction m => (m.X, m.Y),
                DragAction d      => (d.EndX, d.EndY),
                _                 => (cursorX, cursorY)
            };

            currentTurns.Add(new ToolTurn(response.Action, summary, response.ToolUseId!));

            if (options.PostActionDelay > TimeSpan.Zero)
            {
                try { await Task.Delay(options.PostActionDelay, ct); }
                catch (OperationCanceledException) { stopped = StoppedReason.UserCancel; break; }
            }

            yield return new ToolResultEvent(summary, DateTimeOffset.UtcNow, step);
        }

        if (currentTurns.Count > 0)
        {
            _history.Add(new ConversationTurn(prompt, currentTurns, FinalResponse: null, promptScreenshot));
        }

        var finalStats = new AgentStats(step, (long)(DateTimeOffset.UtcNow - started).TotalMilliseconds, inputTokens, outputTokens, cachedInputTokens, _provider.ModelId, stopped);
        yield return new FinalEvent(string.Empty, finalStats, DateTimeOffset.UtcNow, step);
    }

    // Release screenshots the strategy has pruned for good. Pruning is monotonic, so a
    // screenshot the plan no longer keeps will never be wanted again. Current turns are
    // freed in place; history rounds are rewritten so the freed bytes drop once adopted.
    private void FreePrunedScreenshots(List<ToolTurn> currentTurns, HashSet<string> retained)
    {
        for (int i = 0; i < currentTurns.Count; i++)
            if (currentTurns[i].ScreenshotPng != null && !retained.Contains($"t:{currentTurns[i].ToolUseId}"))
                currentTurns[i] = currentTurns[i] with { ScreenshotPng = null };

        for (int r = 0; r < _history.Count; r++)
        {
            var round = _history[r];
            bool dropPrompt = round.PromptScreenshotPng != null && !retained.Contains($"p:{r}");
            List<ToolTurn>? turns = null;
            for (int j = 0; j < round.Turns.Count; j++)
                if (round.Turns[j].ScreenshotPng != null && !retained.Contains($"t:{round.Turns[j].ToolUseId}"))
                {
                    turns ??= [.. round.Turns];
                    turns[j] = turns[j] with { ScreenshotPng = null };
                }
            if (dropPrompt || turns != null)
                _history[r] = round with
                {
                    Turns = turns ?? round.Turns,
                    PromptScreenshotPng = dropPrompt ? null : round.PromptScreenshotPng
                };
        }
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
        WaitAction       => "wait",
        SwitchAction     => "switch",
        VmAction         => "vm_action",
        VmSetupAction    => "vm_setup",
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

    // Actions that drive the screen. With no screen connected these are refused before
    // execution, so the screen-action helpers below always receive a live screen.
    private static bool RequiresScreen(AgentAction action) => action is
        ClickAction or TypeAction or KeyAction or ScrollAction or
        MouseMoveAction or DragAction or ScreenshotAction;

    private async Task<string> ExecuteActionAsync(AgentAction action, IRdpClient? screen, CancellationToken ct) => action switch
    {
        ScreenshotAction => "screenshot taken",
        WaitAction w => await ExecuteWaitAsync(w, ct),
        LoadSkillAction ls => ExecuteLoadSkill(ls),
        SwitchAction sw => await ExecuteSwitchAsync(sw, ct),
        VmAction va => await ExecuteVmActionAsync(va, ct),
        VmSetupAction vs => await ExecuteVmSetupAsync(vs, ct),
        ClickAction c => await ExecuteClickAsync(screen!, c, ct),
        MouseMoveAction m => await ExecuteMouseMoveAsync(screen!, m, ct),
        DragAction d => await ExecuteDragAsync(screen!, d, ct),
        ScrollAction s => await ExecuteScrollAsync(screen!, s, ct),
        KeyAction k => await ExecuteKeyAsync(screen!, k, ct),
        TypeAction t => await ExecuteTypeAsync(screen!, t, ct),
        _ => "unknown action"
    };

    private static async Task<string> ExecuteWaitAsync(WaitAction w, CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(w.Seconds), ct);
        return $"waited {w.Seconds}s";
    }

    private static string ExecuteLoadSkill(LoadSkillAction ls)
    {
        try { return SkillLoader.Load(ls.SkillName); }
        catch (FileNotFoundException ex) { return $"Error: {ex.Message}"; }
    }

    // Switch failures are reported back to the model as a tool result so it can recover,
    // rather than ending the run. Cancellation still propagates.
    private async Task<string> ExecuteSwitchAsync(SwitchAction sw, CancellationToken ct)
    {
        try
        {
            if (sw.Disconnect) { await _connection.DisconnectAsync(ct); return "disconnected"; }
            if (sw.VmId is { } vmId) { await _connection.SwitchToVmAsync(vmId, ct); return $"connected to VM {vmId}"; }
            if (!string.IsNullOrEmpty(sw.Profile)) { await _connection.SwitchToProfileAsync(sw.Profile, ct); return $"connected to profile '{sw.Profile}'"; }
            return "Error: switch needs a profile, vm_id, or disconnect.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ExecuteVmActionAsync(VmAction va, CancellationToken ct)
    {
        try { return await _connection.RunVmActionAsync(va.Operation, va.VmId, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ExecuteVmSetupAsync(VmSetupAction vs, CancellationToken ct)
    {
        try { return await _connection.RunVmSetupAsync(vs.Spec, vs.VmId, ct); }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static async Task<string> ExecuteClickAsync(IRdpClient screen, ClickAction c, CancellationToken ct)
    {
        for (int i = 0; i < c.Clicks; i++)
        {
            await screen.MouseClickAsync(c.Button, c.X, c.Y, ct);
            if (i < c.Clicks - 1) await Task.Delay(50, ct);
        }
        return $"{c.Button} click at ({c.X},{c.Y})";
    }

    private static async Task<string> ExecuteMouseMoveAsync(IRdpClient screen, MouseMoveAction m, CancellationToken ct)
    {
        await screen.MouseMoveAsync(m.X, m.Y, ct);
        return $"mouse moved to ({m.X},{m.Y})";
    }

    private static async Task<string> ExecuteDragAsync(IRdpClient screen, DragAction d, CancellationToken ct)
    {
        await screen.MouseMoveAsync(d.StartX, d.StartY, ct);
        await screen.MouseClickAsync(MouseButton.Left, d.StartX, d.StartY, ct);
        await screen.MouseMoveAsync(d.EndX, d.EndY, ct);
        await screen.MouseClickAsync(MouseButton.Left, d.EndX, d.EndY, ct);
        return $"drag from ({d.StartX},{d.StartY}) to ({d.EndX},{d.EndY})";
    }

    private static async Task<string> ExecuteScrollAsync(IRdpClient screen, ScrollAction s, CancellationToken ct)
    {
        await screen.MouseScrollAsync(s.X, s.Y, s.Direction, s.Amount, ct);
        return $"scroll {s.Direction} {s.Amount}x at ({s.X},{s.Y})";
    }

    private static async Task<string> ExecuteKeyAsync(IRdpClient screen, KeyAction k, CancellationToken ct)
    {
        await screen.KeyPressAsync(k.Keys, ct);
        return $"key: {k.Keys}";
    }

    private static async Task<string> ExecuteTypeAsync(IRdpClient screen, TypeAction t, CancellationToken ct)
    {
        await screen.TypeTextAsync(t.Text, ct);
        return $"typed: {t.Text[..Math.Min(t.Text.Length, 30)]}";
    }
}
