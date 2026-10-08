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

namespace libfintx.FinTS
{
    /// <summary>
    /// Balance of a credit card account (HIKKS, "Kreditkartensaldo rückmelden", FinTS 3.0 change G112, C.12.2).
    /// Amounts are in the currency of the balance; negative for a debit.
    /// </summary>
    public class CreditCardAccountBalance
    {
        /// <summary>
        /// Card number the bank reports the balance for; may be masked differently than in the UPD
        /// </summary>
        public string CardNumber { get; set; }

        /// <summary>
        /// Card account or customer number (Kreditkartenkontonummer/Kundennummer). Null if not delivered.
        /// </summary>
        public string CardAccountNumber { get; set; }

        /// <summary>
        /// Current balance (Aktueller Saldo), including bookings not yet settled but no authorizations.
        /// Null if the bank sent an empty one.
        /// </summary>
        public decimal? Balance { get; set; }

        /// <summary>
        /// Currency of the balance
        /// </summary>
        public string BalanceCurrency { get; set; }

        /// <summary>
        /// Date of the balance
        /// </summary>
        public DateTime? BalanceDate { get; set; }

        /// <summary>
        /// Available amount (Verfügbarer Betrag Kreditkarte), negative for a debit. Null if not delivered.
        /// </summary>
        public decimal? AvailableAmount { get; set; }

        /// <summary>
        /// Sum of the authorizations not yet booked (Summe offener Autorisierungen). Null if not delivered.
        /// </summary>
        public decimal? OpenAuthorizations { get; set; }

        /// <summary>
        /// Credit limit (Verfügungsrahmen). Null if not delivered.
        /// </summary>
        public decimal? CreditLimit { get; set; }

        /// <summary>
        /// Expected date of the next card statement (Voraussichtliches Abrechnungsdatum). Null if not delivered.
        /// </summary>
        public DateTime? ExpectedSettlementDate { get; set; }
    }
}
