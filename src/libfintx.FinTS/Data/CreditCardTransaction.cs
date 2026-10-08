/*
 *
 *  This file is part of libfintx.
 *
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU Lesser General Public
 *  License as published by the Free Software Foundation; either
 *  version 3 of the License, or (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
 *  Lesser General Public License for more details.
 *
 *  You should have received a copy of the GNU Lesser General Public License
 *  along with this program; if not, write to the Free Software Foundation,
 *  Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 *
 */

using System;
using System.Collections.Generic;

namespace libfintx.FinTS
{
    /// <summary>
    /// One credit card transaction of a HIKKU segment ("Umsatz Kreditkartenkonto"), specified in FinTS 3.0
    /// change G112, C.12.1.
    /// </summary>
    public class CreditCardTransaction
    {
        /// <summary>
        /// Number of the card used
        /// </summary>
        public string CardNumber { get; set; }

        /// <summary>
        /// Receipt date (Belegdatum), the day of the purchase
        /// </summary>
        public DateTime? ReceiptDate { get; set; }

        /// <summary>
        /// Booking date (Buchungsdatum), the day the transaction was posted to the card account
        /// </summary>
        public DateTime? BookingDate { get; set; }

        /// <summary>
        /// Amount in the original currency, negative for a debit. Null if not delivered by the bank.
        /// </summary>
        public decimal? OriginalAmount { get; set; }

        /// <summary>
        /// Original currency
        /// </summary>
        public string OriginalCurrency { get; set; }

        /// <summary>
        /// Exchange rate as sent by the bank (1 without conversion). Its direction differs between banks,
        /// so <see cref="Amount"/> must not be recomputed from it.
        /// </summary>
        public decimal? ExchangeRate { get; set; }

        /// <summary>
        /// Value date (Wertstellungsdatum).
        /// </summary>
        public DateTime? ValueDate { get; set; }

        /// <summary>
        /// Booked amount in the account currency, negative for a debit
        /// </summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Account currency
        /// </summary>
        public string Currency { get; set; }

        /// <summary>
        /// Text lines in their original positions, usually merchant and location first: the four
        /// Grundtext/Zusatz pairs of the Transaktionsbeschreibung as eight lines
        /// </summary>
        public List<string> Texts { get; set; } = new List<string>();

        /// <summary>
        /// Country code (Länderkennzeichen) as sent: numeric per FinTS, alphabetic at some institutes.
        /// </summary>
        public string CountryCode { get; set; }

        /// <summary>
        /// Merchant name (Händlername).
        /// </summary>
        public string MerchantName { get; set; }

        /// <summary>
        /// ID of the card payment terminal (Kartenzahlungsterminal-ID).
        /// </summary>
        public string TerminalId { get; set; }

        /// <summary>
        /// Whether the bank flags the transaction as booked (J); null if the flag is not given
        /// </summary>
        public bool? Settled { get; set; }

        /// <summary>
        /// Booking reference of the bank
        /// </summary>
        public string BookingReference { get; set; }

        /// <summary>
        /// Date of the card statement the transaction was settled with, if already settled
        /// </summary>
        public DateTime? SettlementDate { get; set; }

        /// <summary>
        /// Key of the fee charged for the transaction (Gebührenschlüssel).
        /// </summary>
        public string FeeKey { get; set; }

        /// <summary>
        /// Settlement period the transaction belongs to (Abrechnungskennzeichen), free text such as "09-2024".
        /// </summary>
        public string SettlementPeriod { get; set; }

        /// <summary>
        /// Cash or ATM fee with its booking reference, formatted for display (GAA-/BAR-Entgelt).
        /// </summary>
        public string CashFee { get; set; }

        /// <summary>
        /// Foreign usage fee with its booking reference, formatted for display (AEE, Auslandseinsatzentgelt).
        /// </summary>
        public string ForeignFee { get; set; }
    }
}
