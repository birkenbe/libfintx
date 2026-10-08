using System;
using System.Linq;
using libfintx.FinTS;
using Xunit;

namespace libfintx.Tests;

/// <summary>
/// HIKKU (Kreditkartenumsätze rückmelden, FinTS 3.0 change G112, C.12.1), the answer to HKKKU.
/// </summary>
public class Test_HIKKU
{
    // Some institutes mask the card number in HIKKU differently than in the UPD.
    private const string Card = "499999XXXXXX1234";

    // One "Umsatz Kreditkartenkonto" with its 29 components at their flat positions.
    private static string Kku(params (int Position, string Value)[] fields)
    {
        var components = Enumerable.Repeat("", 29).ToArray();
        foreach (var (position, value) in fields)
            components[position] = value;
        return string.Join(":", components);
    }

    private static readonly string Domestic = Kku((0, Card), (1, "20260827"), (2, "20260831"), (4, "20260831"),
        (9, "27,99"), (10, "EUR"), (11, "D"), (12, "REWE MARKT"), (13, "MUENCHEN"),
        (20, "276"), (21, "REWE Markt GmbH"), (22, "T-0815"), (23, "N"), (24, "20260831120000000001"));

    private static readonly string Foreign = Kku((0, Card), (1, "20251209"), (2, "20251210"), (3, "20260104"), (4, "20251210"),
        (5, "75,"), (6, "CAD"), (7, "D"), (8, "1,608206"), (9, "47,53"), (10, "EUR"), (11, "D"),
        (12, "AMAZON.CA"), (13, "75,00 CAD KURS 1,608206"), (20, "124"), (21, "Amazon.ca"), (23, "J"),
        (24, "20251210093000000002"), (26, "01-2026"), (28, "AEE 0,89 EUR 20251210093000000002"));

    private static readonly string Fee = Kku((0, Card), (1, "20251209"), (2, "20251210"), (3, "20260104"),
        (9, "0,89"), (10, "EUR"), (11, "D"), (12, "Auslandseinsatzentgelt"), (23, "J"),
        (24, "20251210093000000002"), (25, "AEE1"), (26, "01-2026"));

    // FinTS escapes, an alphabetic country code and two components beyond the 29 specified ones.
    private static readonly string Escaped = Kku((0, Card), (1, "20260910"), (2, "20260911"),
        (9, "12,5"), (10, "EUR"), (11, "D"), (12, "Café Müller?: Ulm?+Co"), (13, "Zeile ?'2??"),
        (20, "DE"), (21, "Café Müller"), (23, "N"), (24, "REF4")) + ":EXTRA:COMPONENTS";

    // The bank sends one all-empty transaction after the last one.
    private static readonly string Empty = Kku();

    private static string Message(params string[] transactions) =>
        "HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:3+0020::Auftrag ausgeführt.'"
        + "HIKKU:4:1:3+" + Card + "+KD-778899+D:1234,5:EUR:20260922+20260104+20260204"
        + string.Concat(transactions.Select(t => "+" + t))
        + "'";

    [Fact]
    public void Parses_balance_and_transactions()
    {
        var statement = FinTsClient.Parse_CreditCardTransactions_HIKKU(Message(Domestic, Foreign, Fee, Escaped, Empty));

        Assert.Equal(Card, statement.CardNumber);
        Assert.Equal(-1234.5m, statement.Balance);
        Assert.Equal("EUR", statement.BalanceCurrency);
        Assert.Equal(new DateTime(2026, 9, 22), statement.BalanceDate);
        Assert.Equal(new DateTime(2026, 1, 4), statement.LastSettlementDate);
        Assert.Equal(new DateTime(2026, 2, 4), statement.NextSettlementDate);
        Assert.Equal(4, statement.Transactions.Count);

        var domestic = statement.Transactions[0];
        Assert.Equal(Card, domestic.CardNumber);
        Assert.Equal(new DateTime(2026, 8, 27), domestic.ReceiptDate);
        Assert.Equal(new DateTime(2026, 8, 31), domestic.BookingDate);
        Assert.Equal(new DateTime(2026, 8, 31), domestic.ValueDate);
        Assert.Null(domestic.SettlementDate);
        Assert.Null(domestic.OriginalAmount);
        Assert.Equal(-27.99m, domestic.Amount);
        Assert.Equal("EUR", domestic.Currency);
        Assert.Equal(new[] { "REWE MARKT", "MUENCHEN" }, domestic.Texts);
        Assert.Equal("276", domestic.CountryCode);
        Assert.Equal("REWE Markt GmbH", domestic.MerchantName);
        Assert.Equal("T-0815", domestic.TerminalId);
        Assert.Equal(false, domestic.Settled);
        Assert.Equal("20260831120000000001", domestic.BookingReference);

        var foreign = statement.Transactions[1];
        Assert.Equal(-75m, foreign.OriginalAmount);
        Assert.Equal("CAD", foreign.OriginalCurrency);
        Assert.Equal(1.608206m, foreign.ExchangeRate);
        Assert.Equal(-47.53m, foreign.Amount);
        Assert.Equal(new DateTime(2026, 1, 4), foreign.SettlementDate);
        Assert.Equal(true, foreign.Settled);
        Assert.Equal("01-2026", foreign.SettlementPeriod);
        Assert.Equal("AEE 0,89 EUR 20251210093000000002", foreign.ForeignFee);
        Assert.Null(foreign.CashFee);

        var fee = statement.Transactions[2];
        Assert.Equal(-0.89m, fee.Amount);
        Assert.Equal("AEE1", fee.FeeKey);
        Assert.Null(fee.CountryCode);

        var escaped = statement.Transactions[3];
        Assert.Equal(-12.5m, escaped.Amount);
        Assert.Equal(new[] { "Café Müller: Ulm+Co", "Zeile '2?" }, escaped.Texts);
        Assert.Equal("DE", escaped.CountryCode);
        Assert.Equal("Café Müller", escaped.MerchantName);
        Assert.Null(escaped.ForeignFee);
    }

    [Theory]
    [InlineData("+20260104+20260204", "2026-01-04", "2026-02-04")]
    [InlineData("++", null, null)]
    [InlineData("+2026010+20260231", null, null)]
    [InlineData("", null, null)]
    public void Settlement_dates(string dates, string last, string next)
    {
        var message = Message().Replace("+20260104+20260204", dates);

        var statement = FinTsClient.Parse_CreditCardTransactions_HIKKU(message);

        Assert.Equal(last == null ? null : (DateTime?)DateTime.Parse(last), statement.LastSettlementDate);
        Assert.Equal(next == null ? null : (DateTime?)DateTime.Parse(next), statement.NextSettlementDate);
    }

    [Fact]
    public void Missing_settled_flag_is_null()
    {
        var statement = FinTsClient.Parse_CreditCardTransactions_HIKKU(Message(Kku((0, Card), (9, "1,"), (10, "EUR"), (11, "D"))));

        Assert.Null(Assert.Single(statement.Transactions).Settled);
    }

    [Fact]
    public void Ignores_dikku_segments()
    {
        var message = Message(Domestic).Replace("HIKKU:4:1:3", "DIKKU:4:2:3");

        Assert.Empty(FinTsClient.Parse_CreditCardTransactions_HIKKU(message).Transactions);
    }

    [Fact]
    public void Malformed_amount_names_the_field_not_the_content()
    {
        var message = Message(Domestic.Replace(":27,99:EUR:D:", ":X:EUR:D:"));

        var error = Assert.Throws<FormatException>(() => FinTsClient.Parse_CreditCardTransactions_HIKKU(message));

        Assert.Contains("HIKKU transaction 1, field 10", error.Message);
        Assert.DoesNotContain(Card, error.Message);
    }

    [Theory]
    [InlineData("HIRMS:3:2:3+3040::Es liegen weitere Informationen vor.:PAGE?:2'HNHBS:5:1+2'", "PAGE?:2")]
    [InlineData("HIRMS:3:2:3+3040::Es liegen weitere Informationen vor.:PAGE?+'HNHBS:5:1+2'", "PAGE?+")]
    [InlineData("HIRMS:3:2:3+3040::Es liegen weitere Informationen vor.'HNHBS:5:1+2'", "")]
    public void Continuation_point(string message, string expected)
    {
        Assert.Equal(expected, FinTsClient.Parse_CreditCardTransactions_Startpoint(message));
    }
}
