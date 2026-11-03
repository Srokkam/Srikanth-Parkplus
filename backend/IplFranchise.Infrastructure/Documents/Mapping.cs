using IplFranchise.Application;
using IplFranchise.Domain;

namespace IplFranchise.Infrastructure.Documents;

internal static class Mapping
{
    public static Product ToDomain(this ProductDoc doc) =>
        new(doc.Id, doc.FranchiseId, doc.Name, doc.Type, doc.Description, doc.PricePaise, doc.LimitedEdition, doc.MaxPerCustomer);

    public static Cart ToDomain(this CartDoc doc) =>
        new(doc.UserId, doc.Lines.Select(l => new CartLine(l.ProductId, l.FranchiseId, l.Quantity, l.AcceptedUnitPricePaise)).ToList());

    public static CartDoc ToDocument(this Cart cart, DateTimeOffset now) =>
        new()
        {
            UserId = cart.UserId,
            Lines = cart.Lines.Select(l => new CartLineDoc(l.ProductId, l.FranchiseId, l.Quantity, l.AcceptedUnitPricePaise)).ToList(),
            UpdatedUtc = Timestamps.ToText(now),
        };

    public static Order ToDomain(this OrderDoc doc) =>
        new(
            doc.Id,
            doc.UserId,
            Timestamps.Parse(doc.CreatedUtc),
            doc.Lines.Select(l => new OrderLine(l.ProductId, l.FranchiseId, l.Name, l.UnitPricePaise, l.Quantity)).ToList(),
            doc.ShippingPaise);

    public static OrderDoc ToDocument(this Order order) =>
        new()
        {
            Id = order.Id,
            UserId = order.UserId,
            CheckoutId = order.Id,
            CreatedUtc = Timestamps.ToText(order.CreatedUtc),
            Lines = order.Lines
                .Select(l => new OrderLineDoc(l.ProductId, l.FranchiseId, l.Name, l.UnitPricePaise, l.Quantity, l.LineTotalPaise))
                .ToList(),
            SubtotalPaise = order.SubtotalPaise,
            ShippingPaise = order.ShippingPaise,
            TotalPaise = order.TotalPaise,
        };

    public static AuditEntry ToDomain(this AuditDoc doc) =>
        new(doc.Id, doc.ProductId, doc.Field, doc.OldValue, doc.NewValue, doc.Delta, doc.Reason, doc.Actor, Timestamps.Parse(doc.TsUtc));
}
