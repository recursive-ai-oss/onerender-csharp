namespace InvoiceApp.Components;

/// <summary>One billed line of an invoice.</summary>
/// <param name="Description">What was sold.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">The price of one.</param>
public sealed record InvoiceLine(string Description, int Quantity, decimal UnitPrice);
