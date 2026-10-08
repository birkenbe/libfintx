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
using Microsoft.Extensions.Logging;

namespace libfintx.FinTS
{
    public static class HKKKS
    {
        /// <summary>
        /// Credit card balance (Kreditkartensaldo anfordern), version 1 as specified in FinTS 3.0 change G112,
        /// chapter C.12.2: Kontoverbindung international + Kreditkartennummer + Kreditkartenkontonummer/Kundennummer.
        /// Filled like HKKKU: the card number is the account number from the UPD, the card account number its
        /// Unterkontomerkmal (left out when the UPD has none), the Kontoverbindung only when HIKKSS requires it.
        /// </summary>
        public static async Task<String> Init_HKKKS(FinTsClient client)
        {
            client.Logger.LogInformation("Starting job HKKKS: Request credit card balance");

            return await HKKKU.SendCreditCardOrder(client, "HKKKS", client.HIKKSS,
                HKKKU.CardAccountConnection(client, client.HIKKSS_AccountRequired));
        }
    }
}
