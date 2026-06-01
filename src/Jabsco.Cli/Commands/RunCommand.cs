using System.CommandLine;
using Jabsco.Cli.Output;
using Jabsco.Core.Agent;
using Jabsco.Core.Approval;
using Jabsco.Core.Config;
using Jabsco.Core.Credentials;
using Jabsco.Core.Persistence;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Providers;
using Jabsco.Core.Rdp;
using Jabsco.Core.Skills;
using Microsoft.Extensions.Logging;

namespace Jabsco.Cli.Commands;

public static class RunCommand
{
    public static Command Build()
    {
        var cmd = new Command("run", "Run an agent prompt against an RDP host");

        var hostOpt = new Option<string>("--host")
            { Description = "RDP hostname or IP", Required = true };
        var promptOpt = new Option<string>("--prompt")
            { Description = "Prompt for the agent", Required = true };
        var usernameOpt = new Option<string?>("--username")
            { Description = "RDP username (defaults to saved profile)" };
        var passwordOpt = new Option<string?>("--password")
            { Description = "RDP password (defaults to saved profile)" };
        var apiKeyOpt = new Option<string?>("--api-key")
            { Description = "Anthropic API key (overrides config.toml and ANTHROPIC_API_KEY env var)" };
        var modelOpt = new Option<string?>("--model")
            { Description = "Model ID (overrides config.toml model_id)" };
        var maxStepsOpt = new Option<int>("--max-steps")
            { Description = "Maximum agent steps", DefaultValueFactory = _ => 100 };
        var quietOpt = new Option<bool>("--quiet")
            { Description = "Suppress non-final events" };
        var onApprovalOpt = new Option<string>("--on-approval")
            { Description = "Headless approval policy: deny|allow|fail", DefaultValueFactory = _ => "deny" };
        var skillsOpt = new Option<string[]>("--skill")
            { Description = "Skill file to prepend to the prompt (repeatable)", Arity = ArgumentArity.ZeroOrMore };

        cmd.Add(hostOpt); cmd.Add(promptOpt); cmd.Add(usernameOpt); cmd.Add(passwordOpt);
        cmd.Add(apiKeyOpt); cmd.Add(modelOpt); cmd.Add(maxStepsOpt);
        cmd.Add(quietOpt); cmd.Add(onApprovalOpt); cmd.Add(skillsOpt);

        cmd.SetAction(async (parseResult, ct) =>
        {
            var host       = parseResult.GetRequiredValue(hostOpt);
            var rawPrompt  = parseResult.GetRequiredValue(promptOpt);
            var username   = parseResult.GetValue(usernameOpt);
            var password   = parseResult.GetValue(passwordOpt);
            var modelArg   = parseResult.GetValue(modelOpt);
            var maxSteps   = parseResult.GetValue(maxStepsOpt);
            var quiet      = parseResult.GetValue(quietOpt);
            var onApproval = parseResult.GetValue(onApprovalOpt);
            var skills     = parseResult.GetValue(skillsOpt) ?? [];

            JabscoConfig config;
            try { config = ConfigLoader.Load(); }
            catch (InvalidDataException ex) { Console.Error.WriteLine(ex.Message); return; }

            // --api-key overrides the Anthropic key in config
            var apiKeyOverride = parseResult.GetValue(apiKeyOpt);
            if (apiKeyOverride is not null)
                config = config with { AnthropicApiKey = apiKeyOverride };

            // Load defaults from saved profile
            try
            {
                var db = await JabscoDb.OpenAsync(ct: ct);
                var profile = await db.Profiles.FindAsync(host, 3389, username, null, ct);
                if (profile != null)
                {
                    username ??= profile.Username;
                    if (password == null && profile.CredentialRef != null)
                    {
                        var stored = await CreateCredentialStore().GetAsync(profile.CredentialRef, ct);
                        password = stored?.Password;
                    }
                }
            }
            catch { /* DB not available — continue without profile defaults */ }

            // Resolve prompt
            string prompt;
            try
            {
                var resolved = CommandLoader.Resolve(rawPrompt);
                if (resolved != null)
                {
                    prompt = resolved;
                }
                else
                {
                    var blocks = skills.Select(name => SkillLoader.Load(name, args: null)).ToList();
                    prompt = blocks.Count > 0
                        ? string.Join("\n\n", blocks) + "\n\n" + rawPrompt
                        : rawPrompt;
                }
            }
            catch (FileNotFoundException ex) { Console.Error.WriteLine(ex.Message); return; }

            // Connect
            using var loggerFactory = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Warning));
            var rdp = new FreeRdpClient(loggerFactory.CreateLogger<FreeRdpClient>());
            var connectOpts = new ConnectOptions(Host: host, Username: username, Password: password, AcceptAnyCertificate: true);

            Console.Error.WriteLine($"Connecting to {host}...");
            using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await rdp.ConnectAsync(connectOpts, connectCts.Token); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Connection failed: {ex.Message}");
                return;
            }

            // Run
            IApprovalSink approval = onApproval switch
            {
                "allow" => new AutoApproveSink(),
                "fail"  => new FailOnPromptSink(),
                _       => new DenyAllSink()
            };

            IComputerUseProvider provider;
            try { provider = ProviderFactory.Create(config, modelArg); }
            catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return; }
            using var _ = provider as IDisposable;
            var loop = new AgentLoop(new ScreenConnection(rdp), provider, approval);
            var opts = new AgentOptions(MaxSteps: maxSteps, PostActionDelay: TimeSpan.FromMilliseconds(800));
            var writer = new NdjsonEventWriter(Console.Out, quiet);

            await writer.StreamAsync(loop.RunAsync(prompt, opts, ct: ct), ct);

            await rdp.DisconnectAsync(CancellationToken.None);
            await rdp.DisposeAsync();
        });

        return cmd;
    }

    private static ICredentialStore CreateCredentialStore()
    {
        if (OperatingSystem.IsWindows()) return new DpapiCredentialStore();
        if (OperatingSystem.IsLinux())   return new LibsecretCredentialStore();
        if (OperatingSystem.IsMacOS())   return new MacosCredentialStore();
        throw new PlatformNotSupportedException("Jabsco requires Windows, Linux, or macOS");
    }

    private sealed class AutoApproveSink : IApprovalSink
    {
        public Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(ToolDecision.Allow);
    }

    private sealed class DenyAllSink : IApprovalSink
    {
        public Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct)
            => Task.FromResult(ToolDecision.Deny);
    }

    private sealed class FailOnPromptSink : IApprovalSink
    {
        public Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct)
            => Task.FromException<ToolDecision>(new InvalidOperationException($"Approval required for tool '{tool}' but --on-approval=fail"));
    }
}
