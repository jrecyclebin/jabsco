using Jabsco.Daemon.Pipe;
using Jabsco.Daemon.State;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<ConcurrencyGate>();
builder.Services.AddHostedService<PipeServer>();
// TODO: add PlatformServices.Register(builder.Services) once Core DI is finalized

var host = builder.Build();
await host.RunAsync();
