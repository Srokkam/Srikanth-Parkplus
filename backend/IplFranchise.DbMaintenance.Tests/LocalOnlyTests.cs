using IplFranchise.DbMaintenance.Core;

namespace IplFranchise.DbMaintenance.Tests;

public class LocalOnlyTests
{
    [Theory]
    [InlineData("AccountEndpoint=http://localhost:8081/;AccountKey=a2V5;")]
    [InlineData("AccountEndpoint=https://127.0.0.1:8081/;AccountKey=a2V5;")]
    [InlineData("AccountEndpoint=http://cosmos:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;")]
    public void Allows_the_local_emulator(string connectionString) =>
        LocalOnly.EnsureLocalEndpoint(connectionString, "provision");

    [Theory]
    [InlineData("AccountEndpoint=https://ipl-store.documents.azure.com:443/;AccountKey=a2V5;")]
    [InlineData("AccountEndpoint=https://ipl-store.documents.azure.com:443/;")]
    public void Refuses_any_other_endpoint(string connectionString) =>
        Assert.Throws<NonLocalEndpointException>(() => LocalOnly.EnsureLocalEndpoint(connectionString, "provision"));
}
