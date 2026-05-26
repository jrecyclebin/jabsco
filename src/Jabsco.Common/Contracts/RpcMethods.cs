namespace Jabsco.Common.Contracts;

public static class RpcMethods
{
    public const string SessionCreate = "session.create";
    public const string SessionList = "session.list";
    public const string SessionPrompt = "session.prompt";
    public const string SessionCancel = "session.cancel";
    public const string SessionClose = "session.close";
    public const string SessionAttach = "session.attach";
    public const string DaemonStatus = "daemon.status";
    public const string DaemonShutdown = "daemon.shutdown";
}
