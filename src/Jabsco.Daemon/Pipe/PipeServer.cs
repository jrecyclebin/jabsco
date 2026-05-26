using System.IO.Pipes;
using Jabsco.Daemon.Rpc;
using Jabsco.Daemon.State;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

namespace Jabsco.Daemon.Pipe;

public sealed class PipeServer : BackgroundService
{
    private const string PipeName = "jabsco";
    private readonly SessionRegistry _registry;
    private readonly ConcurrencyGate _gate;
    private readonly ILogger<PipeServer> _logger;
    private readonly IServiceProvider _services;

    public PipeServer(
        SessionRegistry registry,
        ConcurrencyGate gate,
        ILogger<PipeServer> logger,
        IServiceProvider services)
    {
        _registry = registry;
        _gate = gate;
        _logger = logger;
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Daemon pipe listening on {PipeName}", PipeName);
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken);
                _ = HandleClientAsync(pipe, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipe accept error");
                pipe.Dispose();
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        {
            var target = new RpcTarget(_registry, _gate, _services);
            var rpc = new JsonRpc(new HeaderDelimitedMessageHandler(pipe));
            rpc.AddLocalRpcTarget(target);
            rpc.StartListening();
            try
            {
                await rpc.Completion;
            }
            finally
            {
                rpc.Dispose();
            }
        }
    }
}
