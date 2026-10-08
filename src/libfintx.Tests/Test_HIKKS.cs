using System;
using libfintx.FinTS;
using Xunit;

namespace libfintx.Tests;

/// <summary>
/// HIKKS (Kreditkartensaldo rückmelden, FinTS 3.0 change G112, C.12.2), the answer to HKKKS, and its
/// parameters HIKKSS together with the UPD-Verwendung of HIUPA.
/// </summary>
public class Test_HIKKS
{
    private const string Card = "499999XXXXXX1234";

    private static string Message(string hikks) =>
        "HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:3+0020::Auftrag ausgeführt.'"
        + "HIKKS:4:1:3+" + hikks + "'";

    [Fact]
    public void Parses_all_fields()
    {
        var balance = FinTsClient.Parse_CreditCardBalance(
            Message(Card + "+KD-778899+C:1234,56:EUR:20261006:120000+5000,:EUR:D+120,5:EUR+6000,:EUR+20261020"));

        Assert.Equal(Card, balance.CardNumber);
        Assert.Equal("KD-778899", balance.CardAccountNumber);
        Assert.Equal(1234.56m, balance.Balance);
        Assert.Equal("EUR", balance.BalanceCurrency);
        Assert.Equal(new DateTime(2026, 10, 6), balance.BalanceDate);
        Assert.Equal(-5000m, balance.AvailableAmount);
        Assert.Equal(120.5m, balance.OpenAuthorizations);
        Assert.Equal(6000m, balance.CreditLimit);
        Assert.Equal(new DateTime(2026, 10, 20), balance.ExpectedSettlementDate);
    }

    [Fact]
    public void Missing_optional_fields_stay_null()
    {
        var balance = FinTsClient.Parse_CreditCardBalance(Message(Card + "++D:1234,56:EUR:20261006"));

        Assert.Equal(Card, balance.CardNumber);
        Assert.Null(balance.CardAccountNumber);
        Assert.Equal(-1234.56m, balance.Balance);
        Assert.Null(balance.AvailableAmount);
        Assert.Null(balance.OpenAuthorizations);
        Assert.Null(balance.CreditLimit);
        Assert.Null(balance.ExpectedSettlementDate);
    }

    [Fact]
    public void No_hikks_returns_null()
    {
        var message = "HIRMG:2:2+3060::Bitte beachten Sie die enthaltenen Warnungen/Hinweise.'"
            + "HIRMS:3:2:3+3010::Zur Kreditkarte liegen keine Salden vor.'";

        Assert.Null(FinTsClient.Parse_CreditCardBalance(message));
    }

    [Theory]
    [InlineData("+KD-1+D:X:EUR:20261006", "HIKKS balance")]
    [InlineData("+KD-1+D:1,:EUR:20261006+X:EUR:C", "HIKKS available amount")]
    [InlineData("+KD-1+D:1,:EUR:20261006++X:EUR", "HIKKS open authorizations")]
    [InlineData("+KD-1+D:1,:EUR:20261006+++X:EUR", "HIKKS credit limit")]
    public void Malformed_amount_names_the_field_not_the_card(string rest, string field)
    {
        var error = Assert.Throws<FormatException>(() => FinTsClient.Parse_CreditCardBalance(Message(Card + rest)));

        Assert.StartsWith(field + ":", error.Message);
        Assert.DoesNotContain(Card, error.Message);
    }

    [Theory]
    [InlineData("HIKKSS:24:1:4+990+1+0+J", 1, true)]
    [InlineData("HIKKSS:24:1:4+990+1+0+N", 1, false)]
    [InlineData("HIKKSS:24:2:4+990+1+0+J", 0, false)]
    public void Parses_hikkss_parameters(string segment, int version, bool accountRequired)
    {
        var client = TestHelper.CreateTestClient();
        client.Parse_Segments(segment + "'");

        Assert.Equal(version, client.HIKKSS);
        Assert.Equal(accountRequired, client.HIKKSS_AccountRequired);
    }

    [Theory]
    [InlineData("HIUPA:4:4:4+1234567+3+0+Max Mustermann", 0)]
    [InlineData("HIUPA:4:4:4+1234567+3+1", 1)]
    [InlineData("HIUPA:4:4:4+1234567+3", null)]
    public void Parses_upd_usage(string segment, int? usage)
    {
        var client = TestHelper.CreateTestClient();
        client.Parse_Segments(segment + "'");

        Assert.Equal(usage, client.UPDUsage);
    }
}
