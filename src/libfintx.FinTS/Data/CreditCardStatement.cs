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
    /// Credit card transactions (HIKKU, "Kreditkartenumsätze rückmelden") and the card account balance
    /// the bank reports with them
    /// </summary>
    public class CreditCardStatement
    {
        /// <summary>
        /// Card or card account number the bank reports the transactions for
        /// </summary>
        public string CardNumber { get; set; }

        /// <summary>
        /// Balance of the card account, negative for a debit balance. Null if not delivered by the bank.
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
        /// Date of the last card statement (HIKKU "Datum der letzten Abrechnung"). Null if not
        /// delivered or malformed.
        /// </summary>
        public DateTime? LastSettlementDate { get; set; }

        /// <summary>
        /// Expected date of the next card statement (HIKKU "Voraussichtliches Abrechnungsdatum"). Null
        /// if not delivered or malformed.
        /// </summary>
        public DateTime? NextSettlementDate { get; set; }

        /// <summary>
        /// The transactions of all HIKKU segments and pages, in the order the bank sent them
        /// </summary>
        public List<CreditCardTransaction> Transactions { get; set; } = new List<CreditCardTransaction>();
    }
}
