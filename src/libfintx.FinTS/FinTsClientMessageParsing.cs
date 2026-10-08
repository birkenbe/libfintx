using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using libfintx.FinTS.Camt;
using libfintx.FinTS.Data.Segment;
using Microsoft.Extensions.Logging;

namespace libfintx.FinTS;

public partial class FinTsClient
{
    /// <summary>
    /// Regex pattern for HIRMG/HIRMS messages.
    /// </summary>
    private const string PatternResultMessage = @"(\d{4}):.*?:(.+)";

    /// <summary>
    /// FinTS amounts always use a decimal comma and no thousands separator, independent of the current culture.
    /// </summary>
    private static readonly NumberFormatInfo AmountFormat = NumberFormatInfo.ReadOnly(new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "" });

    private const NumberStyles AmountStyles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite | NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;

    private Segment Parse_Segment(string segmentCode)
    {
        Segment segment = null;
        try
        {
            segment = SegmentParserFactory.ParseSegment(segmentCode);
        }
        catch (Exception ex)
        {
            // The segment itself may carry personal data (e.g. camt bookings, card numbers): Debug only.
            Logger.LogInformation($"Couldn't parse segment: {ex.Message}");
            Logger.LogDebug(segmentCode);
        }
        return segment;
    }

    /// <summary>
    /// Parsing segment -> UPD, BPD
    /// </summary>
    /// <param name="Message"></param>
    /// <returns></returns>
    internal List<HBCIBankMessage> Parse_Segments(string Message)
    {
        Logger.LogInformation("Parsing segments ...");

        try
        {
            List<HBCIBankMessage> result = new List<HBCIBankMessage>();

            List<string> rawSegments = Helper.SplitEncryptedSegments(Message);

            List<Segment> segments = new List<Segment>();
            foreach (var item in rawSegments)
            {
                Segment segment = Parse_Segment(item);
                if (segment != null)
                    segments.Add(segment);
            }

            // BPD
            string rawBpd = string.Empty;
            var bpaMatch = Regex.Match(Message, @"(HIBPA.+?)\b(HITAN|HNHBS|HISYN|HIUPA)\b");
            if (bpaMatch.Success)
                rawBpd = bpaMatch.Groups[1].Value;
            if (rawBpd.Length > 0)
            {
                if (rawBpd.EndsWith("''"))
                    rawBpd = rawBpd.Substring(0, rawBpd.Length - 1);

                this.BdpStore.SaveBPD(280, ConnectionDetails.Blz, rawBpd)
                    .Wait();
                this.BPD = BankParameterData.BPD.Parse(rawBpd, Logger);
            }

            // UPD
            string upd = string.Empty;
            var upaMatch = Regex.Match(Message, @"(HIUPA.+?)\b(HITAN|HNHBS|HIKIM)\b");
            if (upaMatch.Success)
                upd = upaMatch.Groups[1].Value;
            if (upd.Length > 0)
            {
                Logger.LogInformation("Saving UPD ...");
                Helper.SaveUPD(ConnectionDetails.Blz, ConnectionDetails.UserId, upd);
                UPD.ParseUpd(upd, Logger);
            }

            if (UPD.AccountList != null)
            {
                //Add BIC to Account information (Not retrieved bz UPD??)
                foreach (AccountInformation accInfo in UPD.AccountList)
                    accInfo.AccountBic = ConnectionDetails.Bic;
            }

            foreach (var segment in segments)
            {
                if (segment.Name == "HIRMG")
                {
                    // HIRMG:2:2+9050::Die Nachricht enthÃ¤lt Fehler.+9800::Dialog abgebrochen+9010::Initialisierung fehlgeschlagen, Auftrag nicht bearbeitet.
                    // HIRMG:2:2+9800::Dialogabbruch.

                    string[] HIRMG_messages = segment.Payload.Split('+');
                    foreach (var HIRMG_message in HIRMG_messages)
                    {
                        var message = Parse_BankCode_Message(HIRMG_message);
                        if (message != null)
                            result.Add(message);
                    }
                }

                if (segment.Name == "HIRMS")
                {
                    // HIRMS:3:2:2+9942::PIN falsch. Zugang gesperrt.'
                    string[] HIRMS_messages = segment.Payload.Split('+');
                    foreach (var HIRMS_message in HIRMS_messages)
                    {
                        var message = Parse_BankCode_Message(HIRMS_message);
                        if (message != null)
                            result.Add(message);
                    }

                    var securityMessage = result.FirstOrDefault(m => m.Code == "3920");
                    if (securityMessage != null)
                    {
                        string message = securityMessage.Message;

                        string TAN = string.Empty;
                        string TANf = string.Empty;

                        string[] procedures = Regex.Split(message, @"\D+");

                        foreach (string value in procedures)
                        {
                            if (!string.IsNullOrEmpty(value) && int.TryParse(value, out int i))
                            {
                                if (value.StartsWith("9"))
                                {
                                    if (string.IsNullOrEmpty(TAN))
                                        TAN = i.ToString();

                                    if (string.IsNullOrEmpty(TANf))
                                        TANf = i.ToString();
                                    else
                                        TANf += $";{i}";
                                }
                            }
                        }
                        if (string.IsNullOrEmpty(this.HIRMS))
                        {
                            this.HIRMS = TAN;
                        }
                        else
                        {
                            if (!TANf.Contains(this.HIRMS))
                                throw new Exception($"Invalid HIRMS/Tan-Mode {this.HIRMS} detected. Please choose one of the allowed modes: {TANf}");
                        }
                        this.HIRMSf = TANf;

                        // Parsing TAN processes
                        if (!string.IsNullOrEmpty(this.HIRMS))
                            Parse_TANProcesses(rawBpd);

                    }
                }

                if (segment.Name == "HNHBK")
                {
                    if (segment.DataElements.Count < 3)
                        throw new InvalidOperationException($"Expected segment '{segment}' to contain at least 3 data elements in payload.");

                    var dialogId = segment.DataElements[2];
                    this.HNHBK = dialogId;
                }

                if (segment.Name == "HISYN")
                {
                    this.SystemId = segment.Payload;
                    Logger.LogInformation("Customer System ID: " + this.SystemId);
                }

                if (segment.Name == "HNHBS")
                {
                    if (segment.Payload == null || segment.Payload == "0")
                        this.HNHBS = 2;
                    else
                        this.HNHBS = Convert.ToInt32(segment.Payload) + 1;
                }

                if (segment.Name == "HISALS")
                {
                    if (this.HISALS < segment.Version)
                        this.HISALS = segment.Version;
                }

                if (segment.Name == "HITANS")
                {
                    var hitans = (HITANS) segment;
                    if (this.HIRMS == null)
                    {
                        // Die höchste HKTAN-Version auswählen, welche in den erlaubten TAN-Verfahren (3920) enthalten ist.
                        var tanProcessesHirms = this.HIRMSf.Split(';').Select(tp => Convert.ToInt32(tp));
                        if (hitans.TanProcesses.Select(tp => tp.TanCode).Intersect(tanProcessesHirms).Any())
                            this.HITANS = segment.Version;
                    }
                    else
                    {
                        if (hitans.TanProcesses.Any(tp => tp.TanCode == Convert.ToInt32(this.HIRMS)))
                            this.HITANS = segment.Version;
                    }
                }

                if (segment.Name == "HITAN")
                {
                    // HITAN:5:7:3+S++8578-06-23-13.22.43.709351
                    // HITAN:5:7:4+4++8578-06-23-13.22.43.709351+Bitte Auftrag in Ihrer App freigeben.
                    if (segment.DataElements.Count < 3)
                        throw new InvalidOperationException($"Invalid HITAN segment '{segment}'. Payload must have at least 3 data elements.");
                    this.HITAN = segment.DataElements[2];
                }

                if (segment.Name == "HIKAZS")
                {
                    if (this.HIKAZS == 0)
                    {
                        this.HIKAZS = segment.Version;
                    }
                    else
                    {
                        if (segment.Version > this.HIKAZS)
                            this.HIKAZS = segment.Version;
                    }
                }

                if (segment.Name == "HICAZS")
                {
                    if (segment.Payload.Contains("camt.052.001.02"))
                        this.HICAZS_Camt = CamtScheme.Camt052_001_02;
                    else if (segment.Payload.Contains("camt.052.001.08"))
                        this.HICAZS_Camt = CamtScheme.Camt052_001_08;
                    else // Fallback
                        this.HICAZS_Camt = CamtScheme.Camt052_001_02;
                }

                if (segment.Name == "HISPAS")
                {
                    var hispas = segment as HISPAS;
                    if (this.HISPAS < segment.Version)
                    {
                        this.HISPAS = segment.Version;

                        if (hispas.Payload.Contains("pain.001.001.03"))
                            this.HISPAS_Pain = 1;
                        else if (hispas.Payload.Contains("pain.001.002.03"))
                            this.HISPAS_Pain = 2;
                        else if (hispas.Payload.Contains("pain.001.003.03"))
                            this.HISPAS_Pain = 3;

                        if (this.HISPAS_Pain == 0)
                            this.HISPAS_Pain = 3; // -> Fallback. Most banks accept the newest pain version

                        this.HISPAS_AccountNationalAllowed = hispas.IsAccountNationalAllowed;
                    }
                }

                // Only version 1 (FinTS 3.0 change G112, C.12.1) is implemented (HKKKU.cs); any other version counts as not supported.
                if (segment.Name == "HIKKUS" && segment.Version == 1)
                {
                    // HIKKUS:25:1:4+990+0+0+370:N:J:J -> Speicherzeitraum:Eingabe Anzahl Einträge erlaubt:Angabe Zeitraum erlaubt:Kontoverbindung benötigt
                    this.HIKKUS = segment.Version;
                    var parameters = segment.DataElements.Count > 3 ? SplitDataElementGroup(segment.DataElements[3]) : new List<string>();
                    this.HIKKUS_MaxDays = parameters.Count > 0 && int.TryParse(parameters[0], out int days) ? days : 0;
                    this.HIKKUS_PeriodAllowed = parameters.Count > 2 && parameters[2] == "J";
                    this.HIKKUS_AccountRequired = parameters.Count > 3 && parameters[3] == "J";
                }

                // Only version 1 (FinTS 3.0 change G112, C.12.2) is implemented (HKKKS.cs); any other version counts as not supported.
                if (segment.Name == "HIKKSS" && segment.Version == 1)
                {
                    // HIKKSS:24:1:4+990+1+0+J -> Kontoverbindung benötigt
                    this.HIKKSS = segment.Version;
                    var parameters = segment.DataElements.Count > 3 ? SplitDataElementGroup(segment.DataElements[3]) : new List<string>();
                    this.HIKKSS_AccountRequired = parameters.Count > 0 && parameters[0] == "J";
                }

                if (segment.Name == "HIUPA")
                {
                    // HIUPA:4:4:4+Benutzerkennung+UPD-Version+UPD-Verwendung+Benutzername
                    this.UPDUsage = segment.DataElements.Count > 2 && int.TryParse(segment.DataElements[2], out int usage) ? usage : null;
                }
            }

            // Fallback if HIKAZS is not delivered by BPD (eg. Postbank)
            if (this.HIKAZS == 0)
                this.HIKAZS = 0;

            // If HITANS wasn't set from the response (e.g. bank skipped BPD because version is unchanged),
            // fall back to the cached BPD file so that HKTAN is included in the next INI message.
            if (this.HITANS == 0)
            {
                var cachedBpd = this.BPD;
                if (cachedBpd?.HITANS?.Count > 0)
                {
                    if (!string.IsNullOrEmpty(this.HIRMS) && int.TryParse(this.HIRMS, out int hirmsCode))
                    {
                        foreach (var hitansEntry in cachedBpd.HITANS)
                        {
                            if (hitansEntry.TanProcesses.Any(tp => tp.TanCode == hirmsCode))
                            {
                                this.HITANS = hitansEntry.Version;
                                break;
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(this.HIRMSf))
                    {
                        var tanCodes = this.HIRMSf.Split(';').Where(s => int.TryParse(s, out _)).Select(s => Convert.ToInt32(s)).ToList();
                        foreach (var hitansEntry in cachedBpd.HITANS)
                        {
                            if (hitansEntry.TanProcesses.Select(tp => tp.TanCode).Intersect(tanCodes).Any())
                            {
                                this.HITANS = hitansEntry.Version;
                                break;
                            }
                        }
                    }
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogInformation(ex.ToString());

            throw new InvalidOperationException($"Software error: {ex.Message}", ex);
        }
    }

    internal List<Segment> Parse_Message(string message)
    {
        List<string> values = Helper.SplitEncryptedSegments(message);

        List<Segment> segments = new List<Segment>();
        foreach (var item in values)
        {
            Segment segment = Parse_Segment(item);
            if (segment != null)
                segments.Add(segment);
        }

        foreach (var segment in segments)
        {
            if (segment.Name == "HNHBS")
            {
                if (segment.Payload == null || segment.Payload == "0")
                    this.HNHBS = 2;
                else
                    this.HNHBS = Convert.ToInt32(segment.Payload) + 1;
            }

            if (segment.Name == "HITAN")
            {
                // HITAN:5:7:3+S++8578-06-23-13.22.43.709351
                // HITAN:5:7:4+4++8578-06-23-13.22.43.709351+Bitte Auftrag in Ihrer App freigeben.
                // HITAN:5:6:4+4++76ma3j/MKH0BAABsRcJNhG?+owAQA+Eine neue TAN steht zur Abholung bereit.  Die TAN wurde reserviert am  16.11.2021 um 13?:54?:59 Uhr. Eine Push-Nachricht wurde versandt.  Bitte geben Sie die TAN ein.'
                if (segment.DataElements.Count < 3)
                    throw new InvalidOperationException($"Invalid HITAN segment '{segment}'. Payload must have at least 3 data elements.");
                this.HITAN = segment.DataElements[2];
            }
        }

        return segments;
    }

    /// <summary>
    /// Split a data element group at unescaped ':' and remove the FinTS escaping ('?x' becomes 'x').
    /// </summary>
    private static List<string> SplitDataElementGroup(string dataElement)
    {
        var elements = new List<string>();
        var current = new System.Text.StringBuilder();
        for (int i = 0; i < dataElement.Length; i++)
        {
            var c = dataElement[i];
            if (c == '?' && i + 1 < dataElement.Length)
                current.Append(dataElement[++i]);
            else if (c == ':')
            {
                elements.Add(current.ToString());
                current.Clear();
            }
            else
                current.Append(c);
        }
        elements.Add(current.ToString());
        return elements;
    }

    /// <summary>
    /// Parse the credit card transactions (HIKKU v1, FinTS 3.0 change G112, C.12.1) of all HIKKU segments of a
    /// message: Kreditkartennummer + Kreditkartenkontonummer/Kundennummer
    /// + Aktueller Saldo (sdo) + Datum der letzten Abrechnung + Voraussichtliches Abrechnungsdatum + one
    /// "Umsatz Kreditkartenkonto" per data element. The card number may be masked differently than in the UPD.
    /// A transaction without any value (some institutes send one after the last) is skipped.
    /// </summary>
    /// <param name="message">The bank's answer</param>
    /// <returns>The transactions of all HIKKU segments and the first balance</returns>
    internal static CreditCardStatement Parse_CreditCardTransactions_HIKKU(string message)
    {
        // HIKKU:4:1:3+4999990000001234+KD-1+D:10,:EUR:20260922+20260104+20260204+4999990000001234:20260827:20260831::::::::27,99:EUR:D:REWE MARKT::::::::276:REWE Markt GmbH::N:REF1::::'
        return Parse_CreditCardStatement(message, "HIKKU", Parse_HikkuTransaction);
    }

    /// <summary>
    /// Parse the credit card balance (HIKKS v1, FinTS 3.0 change G112, C.12.2) of the first HIKKS segment of a
    /// message: Kreditkartennummer + Kreditkartenkontonummer/Kundennummer (O) + Aktueller Saldo (sdo)
    /// + Verfügbarer Betrag (btgv, O) + Summe offener Autorisierungen (btg, O) + Verfügungsrahmen (btg, O)
    /// + Voraussichtliches Abrechnungsdatum (O). A malformed amount throws a <see cref="FormatException"/>
    /// naming the field, never its content.
    /// </summary>
    /// <param name="message">The bank's answer</param>
    /// <returns>The balance, or null when the answer carries no HIKKS (e.g. 3010 "Zur Kreditkarte liegen keine Salden vor")</returns>
    internal static CreditCardAccountBalance Parse_CreditCardBalance(string message)
    {
        // HIKKS:4:1:3+4999990000001234+KD-1+D:1234,56:EUR:20261006+5000,:EUR:C+120,5:EUR+6000,:EUR+20261020'
        var segment = Helper.SplitEncryptedSegments(message).FirstOrDefault(s => s.StartsWith("HIKKS:"));
        var payloadStart = segment?.IndexOf('+') ?? -1;
        if (payloadStart < 0)
            return null;

        var dataElements = Helper.SplitDataElements(segment.Substring(payloadStart + 1));
        string Element(int index) => index < dataElements.Count && dataElements[index].Length > 0 ? dataElements[index] : null;
        List<string> Group(int index) => Element(index) == null ? null : SplitDataElementGroup(Element(index));

        var result = new CreditCardAccountBalance
        {
            CardNumber = Group(0)?[0],
            CardAccountNumber = Group(1)?[0],
            ExpectedSettlementDate = ParseDate(Element(6)),
        };

        // sdo: C|D : Wert : Währung : Datum [: Uhrzeit]
        if (Group(2) is { } balance)
        {
            result.Balance = ParseSignedAmount(balance[0], balance.Count > 1 ? balance[1] : null, "HIKKS balance");
            result.BalanceCurrency = balance.Count > 2 ? balance[2] : null;
            result.BalanceDate = balance.Count > 3 ? ParseDate(balance[3]) : null;
        }

        // btgv: Wert : Währung : C|D
        if (Group(3) is { } available)
            result.AvailableAmount = ParseSignedAmount(available.Count > 2 ? available[2] : null, available[0], "HIKKS available amount");

        // btg: Wert : Währung
        if (Group(4) is { } authorizations)
            result.OpenAuthorizations = ParseCreditCardAmount(authorizations[0], "HIKKS open authorizations");
        if (Group(5) is { } limit)
            result.CreditLimit = ParseCreditCardAmount(limit[0], "HIKKS credit limit");

        return result;
    }

    /// <summary>
    /// The statement of all <paramref name="segmentName"/> segments of a message, read in the HIKKU
    /// layout up to the transactions. <paramref name="parseTransaction"/> gets the components of one
    /// transaction and its name for error messages, and may return null to skip it.
    /// </summary>
    private static CreditCardStatement Parse_CreditCardStatement(string message, string segmentName,
        Func<List<string>, string, CreditCardTransaction> parseTransaction)
    {
        var statement = new CreditCardStatement();
        foreach (var segment in Helper.SplitEncryptedSegments(message))
        {
            if (!segment.StartsWith(segmentName + ":"))
                continue;

            var payloadStart = segment.IndexOf('+');
            if (payloadStart < 0)
                continue;

            var dataElements = Helper.SplitDataElements(segment.Substring(payloadStart + 1));
            statement.CardNumber ??= SplitDataElementGroup(dataElements[0])[0];

            if (statement.Balance == null && dataElements.Count > 2 && dataElements[2].Length > 0)
            {
                var balance = SplitDataElementGroup(dataElements[2]);
                statement.Balance = ParseSignedAmount(balance[0], balance.Count > 1 ? balance[1] : null, $"{segmentName} balance");
                statement.BalanceCurrency = balance.Count > 2 ? balance[2] : null;
                statement.BalanceDate = balance.Count > 3 ? ParseDate(balance[3]) : null;
            }

            // Informational only: a missing or malformed date stays null.
            if (statement.LastSettlementDate == null && dataElements.Count > 3)
                statement.LastSettlementDate = ParseDate(dataElements[3]);
            if (statement.NextSettlementDate == null && dataElements.Count > 4)
                statement.NextSettlementDate = ParseDate(dataElements[4]);

            for (int i = 5; i < dataElements.Count; i++)
            {
                if (dataElements[i].Length == 0)
                    continue;

                var transaction = parseTransaction(SplitDataElementGroup(dataElements[i]), $"{segmentName} transaction {i - 4}");
                if (transaction != null)
                    statement.Transactions.Add(transaction);
            }
        }

        return statement;
    }

    /// <summary>
    /// One HIKKU "Umsatz Kreditkartenkonto", positional with its nested groups flattened:
    /// 0 Umsatz getätigt von : 1 Belegdatum : 2 Buchungsdatum : 3 Abrechnungsdatum : 4 Wertstellungsdatum
    /// : 5-7 Originalbetrag (Wert:Währung:C|D) : 8 Umrechnungskurs : 9-11 Buchungsbetrag (Wert:Währung:C|D)
    /// : 12-19 Transaktionsbeschreibung 4 x (Grundtext:Zusatz) : 20 Länderkennzeichen : 21 Händlername
    /// : 22 Kartenzahlungsterminal-ID : 23 Umsatz abgerechnet J/N : 24 Buchungsreferenz : 25 Gebührenschlüssel
    /// : 26 Abrechnungskennzeichen : 27 GAA-/BAR-Entgelt + Buchungsreferenz : 28 AEE + Buchungsreferenz.
    /// Further components are ignored. Returns null for a transaction without any value.
    /// </summary>
    private static CreditCardTransaction Parse_HikkuTransaction(List<string> fields, string name)
    {
        if (fields.All(f => f.Length == 0))
            return null;

        string Field(int index) => index < fields.Count && fields[index].Length > 0 ? fields[index] : null;
        string Name(int index) => $"{name}, field {index + 1}";

        return new CreditCardTransaction
        {
            CardNumber = Field(0),
            ReceiptDate = ParseDate(Field(1)),
            BookingDate = ParseDate(Field(2)),
            SettlementDate = ParseDate(Field(3)),
            ValueDate = ParseDate(Field(4)),
            OriginalAmount = Field(5) == null ? null : ParseSignedAmount(Field(7), Field(5), Name(5)),
            OriginalCurrency = Field(6),
            ExchangeRate = Field(8) == null ? null : ParseCreditCardAmount(Field(8), Name(8)),
            Amount = ParseSignedAmount(Field(11), Field(9), Name(9)),
            Currency = Field(10),
            Texts = CreditCardTexts(fields, 12, 8),
            CountryCode = Field(20),
            MerchantName = Field(21),
            TerminalId = Field(22),
            Settled = Field(23) == null ? null : Field(23) == "J",
            BookingReference = Field(24),
            FeeKey = Field(25),
            SettlementPeriod = Field(26),
            CashFee = Field(27),
            // G112 contradicts itself here: the transaction table has DE an..40, the glossary DEG sdo; the table is followed.
            ForeignFee = Field(28),
        };
    }

    /// <summary>
    /// The text fields in their original positions, without the trailing empty ones.
    /// </summary>
    private static List<string> CreditCardTexts(List<string> fields, int start, int count)
    {
        var texts = fields.Skip(start).Take(count).ToList();
        while (texts.Count > 0 && texts[texts.Count - 1].Length == 0)
            texts.RemoveAt(texts.Count - 1);
        return texts;
    }

    /// <summary>
    /// An unsigned FinTS amount (decimal comma, e.g. "75,") with its separate credit/debit mark; D is negative.
    /// </summary>
    private static decimal ParseSignedAmount(string creditDebit, string amount, string field)
    {
        var value = ParseCreditCardAmount(amount, field);
        return creditDebit == "D" ? -Math.Abs(value) : value;
    }

    private static decimal ParseCreditCardAmount(string value, string field)
    {
        if (string.IsNullOrEmpty(value) || !decimal.TryParse(value, AmountStyles, AmountFormat, out var amount))
            throw new FormatException($"{field}: missing or not a FinTS amount.");

        return amount;
    }

    private static DateTime? ParseDate(string value)
    {
        return DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    internal AccountBalance Parse_Balance(string message)
    {
        var hirms = message.Substring(message.IndexOf("HIRMS") + 5);
        hirms = hirms.Substring(0, (hirms.Contains("'") ? hirms.IndexOf('\'') : hirms.Length));
        var hirmsParts = hirms.Split(':');

        AccountBalance balance = new AccountBalance();
        balance.Message = hirmsParts[hirmsParts.Length - 1];

        if (message.Contains("+0020::"))
        {
            var hisal = message.Substring(message.IndexOf("HISAL") + 5);
            hisal = hisal.Substring(0, (hisal.Contains("'") ? hisal.IndexOf('\'') : hisal.Length));
            var hisalParts = hisal.Split('+');

            balance.Successful = true;

            var hisalAccountParts = hisalParts[1].Split(':');
            if (hisalAccountParts.Length == 4)
            {
                balance.AccountType = new AccountInformation()
                {
                    AccountNumber = hisalAccountParts[0],
                    AccountBankCode = hisalAccountParts.Length > 3 ? hisalAccountParts[3] : null,
                    AccountType = hisalParts[2],
                    AccountCurrency = hisalParts[3],
                    AccountBic = !string.IsNullOrEmpty(hisalAccountParts[1]) ? hisalAccountParts[1] : null
                };
            }
            else if (hisalAccountParts.Length == 2)
            {
                balance.AccountType = new AccountInformation()
                {
                    AccountIban = hisalAccountParts[0],
                    AccountBic = hisalAccountParts[1]
                };
            }

            var hisalBalanceParts = hisalParts[4].Split(':');
            if (hisalBalanceParts[1].IndexOf("e-9", StringComparison.OrdinalIgnoreCase) >= 0)
                balance.Balance = 0; // Deutsche Bank liefert manchmal "E-9", wenn der Kontostand 0 ist. Siehe Test_Parse_Balance und https://homebanking-hilfe.de/forum/topic.php?t=24155
            else
                balance.Balance = Convert.ToDecimal($"{(hisalBalanceParts[0] == "D" ? "-" : "")}{hisalBalanceParts[1]}");


            //from here on optional fields / see page 46 in "FinTS_3.0_Messages_Geschaeftsvorfaelle_2015-08-07_final_version.pdf"
            if (hisalParts.Length > 5 && hisalParts[5].Contains(":"))
            {
                var hisalMarkedBalanceParts = hisalParts[5].Split(':');
                balance.MarkedTransactions = Convert.ToDecimal($"{(hisalMarkedBalanceParts[0] == "D" ? "-" : "")}{hisalMarkedBalanceParts[1]}");
            }

            if (hisalParts.Length > 6 && hisalParts[6].Contains(":"))
            {
                balance.CreditLine = Convert.ToDecimal(hisalParts[6].Split(':')[0].TrimEnd(','));
            }

            if (hisalParts.Length > 7 && hisalParts[7].Contains(":"))
            {
                balance.AvailableBalance = Convert.ToDecimal(hisalParts[7].Split(':')[0].TrimEnd(','));
            }

            /* ---------------------------------------------------------------------------------------------------------
             * In addition to the above fields, the following fields from HISAL could also be implemented:
             * 
             * - 9/Bereits verfügter Betrag
             * - 10/Überziehung
             * - 11/Buchungszeitpunkt
             * - 12/Fälligkeit 
             * 
             * Unfortunately I'm missing test samples. So I drop support unless we get test messages for this fields.
             ------------------------------------------------------------------------------------------------------------ */
        }
        else
        {
            balance.Successful = false;

            string msg = string.Empty;
            for (int i = 1; i < hirmsParts.Length; i++)
            {
                msg = msg + "??" + hirmsParts[i].Replace("::", ": ");
            }
            Logger.LogInformation(msg);
        }

        return balance;
    }

    internal static string Parse_Transactions_Startpoint(string bankCode)
    {
        return Regex.Match(bankCode, @"\+3040::[^:]+:(?<startpoint>[^'\+:]+)['\+:]").Groups["startpoint"].Value;
    }

    /// <summary>
    /// The continuation point of a 3040 message, kept FinTS-escaped as it is sent back. Unlike
    /// <see cref="Parse_Transactions_Startpoint"/> the message text cannot run into the next segment,
    /// so a 3040 without continuation point yields an empty string.
    /// </summary>
    internal static string Parse_CreditCardTransactions_Startpoint(string bankCode)
    {
        return Regex.Match(bankCode, @"\+3040::(?:[^:'+?]|\?.)*:(?<startpoint>(?:[^:'+?]|\?.)+)").Groups["startpoint"].Value;
    }

    /// <summary>
    /// Parse tan processes
    /// </summary>
    /// <returns></returns>
    private bool Parse_TANProcesses(string bpd)
    {
        try
        {
            List<TanProcess> list = new List<TanProcess>();

            string[] processes = this.HIRMSf.Split(';');

            // Examples from bpd

            // 944:2:SECUREGO:
            // 920:2:smsTAN:
            // 920:2:BestSign:

            foreach (var process in processes)
            {
                string pattern = process + ":.*?:.*?:(?'name'.*?):.*?:(?'name2'.*?):";

                Regex rgx = new Regex(pattern);

                foreach (Match match in rgx.Matches(bpd))
                {
                    int i = 0;

                    if (!process.Equals("999")) // -> PIN/TAN step 1
                    {
                        if (int.TryParse(match.Groups["name2"].Value, out i))
                            list.Add(new TanProcess { ProcessNumber = process, ProcessName = match.Groups["name"].Value });
                        else
                            list.Add(new TanProcess { ProcessNumber = process, ProcessName = match.Groups["name2"].Value });
                    }
                }
            }

            TanProcesses.Items = list;

            return true;
        }
        catch { return false; }
    }

    internal static IEnumerable<string> Parse_TANMedium(string bankCode)
    {
        // HITAB:5:4:3+0+A:1:::::::::::Handy::::::::+A:2:::::::::::iPhone Abid::::::::
        // HITAB:4:4:3+0+M:1:::::::::::mT?:MFN1:********0340'
        // HITAB:5:4:3+0+M:2:::::::::::Unregistriert 1::01514/654321::::::+M:1:::::::::::Handy:*********4321:::::::
        // HITAB:4:4:3+0+M:1:::::::::::mT?:MFN1:********0340+G:1:SO?:iPhone:00:::::::::SO?:iPhone''

        // For easier matching, replace '?:' by some special character
        bankCode = bankCode.Replace("?:", @"\");

        foreach (Match match in Regex.Matches(bankCode, @"\+[AGMS]:[012]:(?<Kartennummer>[^:]*):(?<Kartenfolgenummer>[^:]*):+(?<Bezeichnung>[^+:]+)"))
        {
            // Return the plain name (FinTS escaping removed); HKTAN escapes it again when sending
            yield return Regex.Replace(match.Groups["Bezeichnung"].Value.Replace(@"\", "?:"), @"\?(.)", "$1");
        }
    }

    /// <summary>
    /// Parse a single bank result message.
    /// </summary>
    /// <param name="bankCodeMessage"></param>
    /// <returns></returns>
    internal static HBCIBankMessage? Parse_BankCode_Message(string bankCodeMessage)
    {
        var match = Regex.Match(bankCodeMessage, PatternResultMessage);
        if (match.Success)
        {
            var code = match.Groups[1].Value;
            var message = match.Groups[2].Value;

            message = message.Replace("?:", ":");
            message = message.Replace("?'", "'");
            message = message.Replace("?+", "+");

            return new HBCIBankMessage(code, message);
        }
        return null;
    }

    /// <summary>
    /// Parse bank error codes
    /// </summary>
    /// <param name="bankCode"></param>
    /// <returns>Banks messages with "??" as seperator.</returns>
    internal IEnumerable<HBCIBankMessage> Parse_BankCode(string bankCode)
    {
        var rawSegments = Helper.SplitEncryptedSegments(bankCode);
        var segments = new List<Segment>();
        foreach (var item in rawSegments)
        {
            Segment segment = Parse_Segment(item);
            if (segment != null)
                segments.Add(segment);
        }

        foreach (var segment in segments)
        {
            if (segment.Name == "HIRMG" || segment.Name == "HIRMS")
            {
                // HIRMS:4:2:3+9210::*?'Ausführung bis?' muss nach ?'Ausführung ab?' liegen.+9210::*Die BIC wurde angepasst.+0900::Freigabe erfolgreich
                var messages = segment.DataElements;
                foreach (var HIRMG_message in messages)
                {
                    var message = Parse_BankCode_Message(HIRMG_message);
                    if (message != null)
                        yield return message;
                }
            }
        }
    }
}
