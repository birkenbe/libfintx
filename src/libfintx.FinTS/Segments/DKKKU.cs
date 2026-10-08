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
using System.Threading.Tasks;
using libfintx.FinTS.Data;
using Microsoft.Extensions.Logging;

namespace libfintx.FinTS
{
    public static class DKKKU
    {
        /// <summary>
        /// Credit card transactions (Kreditkartenumsätze anfordern). An institute specific segment
        /// without a published specification; the layout of version 2 is:
        /// Kontoverbindung (national) + Kreditkartennummer + Unterkontomerkmal + Von Datum + Bis Datum
        /// + Maximale Anzahl Einträge + Aufsetzpunkt. The card number is the account number from the UPD.
        /// </summary>
        public static async Task<String> Init_DKKKU(FinTsClient client, string FromDate, string ToDate, string Startpoint)
        {
            client.Logger.LogInformation("Starting job DKKKU: Request credit card transactions");

            var connectionDetails = client.ConnectionDetails;

            string account = connectionDetails.Account + DEG.Separator + connectionDetails.SubAccount + DEG.Separator
                + SEG_COUNTRY.Germany + DEG.Separator + connectionDetails.Blz;

            // Maximale Anzahl Einträge stays empty: institutes that do not allow it reject the order (9110).
            return await HKKKU.SendCreditCardOrder(client, "DKKKU", client.DIKKUS, account, FromDate, ToDate, string.Empty, Startpoint);
        }
    }
}
