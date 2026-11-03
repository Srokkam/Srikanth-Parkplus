using IplFranchise.DbMaintenance.Core;

namespace IplFranchise.DbMaintenance.Tests;

/// <summary>The runner's per-migration decision (steps 1 to 3 of the runner algorithm).</summary>
public class ClaimPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void No_record_means_create_a_running_record() =>
        Assert.Equal(ClaimDecision.Create, ClaimPolicy.Decide(null, Now));

    [Fact]
    public void Completed_is_skipped() =>
        Assert.Equal(ClaimDecision.Skip, ClaimPolicy.Decide(Record(MigrationStatus.Completed, Now.AddDays(-3)), Now));

    [Theory]
    [InlineData(0)]
    [InlineData(14)]
    public void Running_and_younger_than_15_minutes_means_another_runner_has_it(int minutesAgo) =>
        Assert.Equal(ClaimDecision.Busy, ClaimPolicy.Decide(Record(MigrationStatus.Running, Now.AddMinutes(-minutesAgo)), Now));

    [Theory]
    [InlineData(15)]
    [InlineData(120)]
    public void Running_for_15_minutes_or_more_is_stale_and_taken_over(int minutesAgo) =>
        Assert.Equal(ClaimDecision.TakeOver, ClaimPolicy.Decide(Record(MigrationStatus.Running, Now.AddMinutes(-minutesAgo)), Now));

    [Fact]
    public void Failed_is_taken_over_immediately() =>
        Assert.Equal(ClaimDecision.TakeOver, ClaimPolicy.Decide(Record(MigrationStatus.Failed, Now.AddSeconds(-1)), Now));

    [Fact]
    public void Unknown_status_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => ClaimPolicy.Decide(Record("paused", Now), Now));

    private static MigrationRecord Record(string status, DateTime startedUtc) =>
        new("0002-backfill-limited-edition", status, "test", startedUtc, null, null);
}
