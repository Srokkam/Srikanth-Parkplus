namespace IplFranchise.DbMaintenance.Core;

/// <summary>
/// Guard for commands that create containers. Those are only allowed against the local emulator;
/// real environments get containers from Infrastructure/main.bicep.
/// </summary>
public static class LocalOnly
{
    public static void EnsureLocalEndpoint(string connectionString, string command)
    {
        if (!CosmosClientFactory.IsLocalEndpoint(connectionString))
        {
            throw new NonLocalEndpointException(
                $"'{command}' only runs against the local emulator (a loopback endpoint). " +
                "Real environments get their containers from Infrastructure/main.bicep.");
        }
    }
}

public sealed class NonLocalEndpointException(string message) : InvalidOperationException(message);
