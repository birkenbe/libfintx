using System;
using libfintx.FinTS;
using Xunit;

namespace libfintx.Tests;

/// <summary>
/// DIKKU (Kreditkartenumsätze rückmelden), the answer to DKKKU.
/// </summary>
public class Test_DIKKU
{
    private const string Card = "4999990000001234";

    // A domestic purchase, a foreign-currency purchase with fee text (26 fields), the settlement
    // credit (24 fields) and a record with FinTS escapes and only 23 fields.
    private const string Message =
        "HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:3+0020::Auftrag ausgeführt.'"
        + "DIKKU:4:2:3+" + Card + "++D:1234,5:EUR:20260922+20260904++"
        + Card + ":20260827:20260831::27,99:EUR:D:1,:27,99:EUR:D:MERCHANT IE:::::::::J:20262430027631940001:3246:20260904"
        + "+" + Card + ":20251209:20251210::75,:CAD:D:1,608206:47,53:EUR:D:MERCHANT CA:75,00 CAD, EURO-KURS  1,608206::::::::J:20253440012930940001:9399:20260104::inkl. 1,90% Einsatz Fremdw. EUR   0,89-"
        + "+" + Card + ":20260904:20260904::27,99:EUR:C:1,:27,99:EUR:C:Einzug des Rechnungsbetrages:::::::::J:26247000001130310001::20260904"
        + "+" + Card + ":20260910:20260911::12,5:EUR:D:1,:12,5:EUR:D:Café Müller?: Ulm?+Co:Zeile ?'2??::::::::J:REF4:5812"
        + "'";

    [Fact]
    public void Parses_balance_and_transactions()
    {
        var statement = FinTsClient.Parse_CreditCardTransactions(Message);

        Assert.Equal(Card, statement.CardNumber);
        Assert.Equal(-1234.5m, statement.Balance);
        Assert.Equal("EUR", statement.BalanceCurrency);
        Assert.Equal(new DateTime(2026, 9, 22), statement.BalanceDate);
        Assert.Equal(new DateTime(2026, 9, 4), statement.LastSettlementDate);
        Assert.Null(statement.NextSettlementDate);
        Assert.Equal(4, statement.Transactions.Count);

        var domestic = statement.Transactions[0];
        Assert.Equal(new DateTime(2026, 8, 27), domestic.ReceiptDate);
        Assert.Equal(new DateTime(2026, 8, 31), domestic.BookingDate);
        Assert.Equal(-27.99m, domestic.Amount);
        Assert.Equal(new[] { "MERCHANT IE" }, domestic.Texts);
        Assert.Equal(true, domestic.Settled);
        Assert.Equal("20262430027631940001", domestic.BookingReference);
        Assert.Equal("3246", domestic.MerchantCategoryCode);
        Assert.Equal(new DateTime(2026, 9, 4), domestic.SettlementDate);

        var foreign = statement.Transactions[1];
        Assert.Equal(-75m, foreign.OriginalAmount);
        Assert.Equal("CAD", foreign.OriginalCurrency);
        Assert.Equal(1.608206m, foreign.ExchangeRate);
        Assert.Equal(-47.53m, foreign.Amount);
        Assert.Equal("inkl. 1,90% Einsatz Fremdw. EUR   0,89-", foreign.AdditionalText);

        var settlement = statement.Transactions[2];
        Assert.Equal(27.99m, settlement.Amount);
        Assert.Null(settlement.MerchantCategoryCode);

        var escaped = statement.Transactions[3];
        Assert.Equal(-12.5m, escaped.Amount);
        Assert.Equal(new[] { "Café Müller: Ulm+Co", "Zeile '2?" }, escaped.Texts);
        Assert.Null(escaped.SettlementDate);
    }

    [Fact]
    public void Malformed_settlement_date_stays_null()
    {
        var message = Message.Replace("+20260904++", "+2026-09-04+20261004+");

        var statement = FinTsClient.Parse_CreditCardTransactions(message);

        Assert.Null(statement.LastSettlementDate);
        Assert.Equal(new DateTime(2026, 10, 4), statement.NextSettlementDate);
    }

    [Fact]
    public void Missing_settled_flag_is_null()
    {
        var message = Message.Replace(":J:REF4:", "::REF4:");

        var statement = FinTsClient.Parse_CreditCardTransactions(message);

        Assert.Null(statement.Transactions[3].Settled);
    }

    [Fact]
    public void Malformed_amount_names_the_field_not_the_content()
    {
        var message = Message.Replace(":27,99:EUR:D:MERCHANT IE", ":X:EUR:D:MERCHANT IE");

        var error = Assert.Throws<FormatException>(() => FinTsClient.Parse_CreditCardTransactions(message));

        Assert.Contains("transaction 1, field 9", error.Message);
        Assert.DoesNotContain(Card, error.Message);
    }
}
