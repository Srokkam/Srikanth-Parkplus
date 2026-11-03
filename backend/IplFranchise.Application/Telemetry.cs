using System.Diagnostics.Metrics;

namespace IplFranchise.Application;

public static class Telemetry
{
    public const string MeterName = "IplFranchise";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> CheckoutOutcomes = Meter.CreateCounter<long>("checkout.outcomes");

    public static readonly Counter<long> IdempotentReplays = Meter.CreateCounter<long>("idempotency.replays");

    public static readonly Counter<long> SweptOrders = Meter.CreateCounter<long>("checkout.swept_orders");

    public static readonly Histogram<double> RequestCharge = Meter.CreateHistogram<double>("cosmos.request_charge", unit: "RU");
}
