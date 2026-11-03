namespace IplFranchise.Domain;

public enum StockStatus
{
    InStock,
    LowStock,
    OutOfStock,
}

public static class StockPolicy
{
    public static StockStatus StatusFor(int available) => available switch
    {
        <= 0 => StockStatus.OutOfStock,
        <= Limits.LowStockThreshold => StockStatus.LowStock,
        _ => StockStatus.InStock,
    };
}
