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
using System.Threading.Tasks;
using libfintx.FinTS.Data;
using libfintx.FinTS.Message;
using libfintx.FinTS.Segments;
using Microsoft.Extensions.Logging;

namespace libfintx.FinTS
{
    public static class HKKKU
    {
        /// <summary>
        /// Credit card transactions (Kreditkartenumsätze anfordern), version 1 as specified in FinTS 3.0
        /// change G112, chapter C.12.1: Kontoverbindung international + Kreditkartennummer
        /// + Kreditkartenkontonummer/Kundennummer + Von Datum + Bis Datum + Maximale Anzahl Einträge + Aufsetzpunkt.
        /// The card number is the account number from the UPD, the card account number its Unterkontomerkmal.
        /// The Kontoverbindung is optional unless HIKKUS requires it ("Kontoverbindung benötigt"), so it is
        /// only sent then; an account without IBAN is sent in the national form. What is sent is the card account
        /// itself, while the spec asks for the account at the card-issuing institute (likely the settlement giro
        /// account): if a bank answers 9210 "Keine gültige Kontoverbindung", look here first.
        /// </summary>
        public static async Task<String> Init_HKKKU(FinTsClient client, string FromDate, string ToDate, string Startpoint)
        {
            client.Logger.LogInformation("Starting job HKKKU: Request credit card transactions");

            // Maximale Anzahl Einträge stays empty: institutes that do not allow it reject the order (9110).
            return await SendCreditCardOrder(client, "HKKKU", client.HIKKUS,
                CardAccountConnection(client, client.HIKKUS_AccountRequired), FromDate, ToDate, string.Empty, Startpoint);
        }

        /// <summary>
        /// The Kontoverbindung international of HKKKU and HKKKS: only when the bank requires it
        /// ("Kontoverbindung benötigt"), otherwise empty; an account without IBAN in the national form.
        /// </summary>
        internal static string CardAccountConnection(FinTsClient client, bool required)
        {
            if (!required)
                return string.Empty;

            var connectionDetails = client.ConnectionDetails;

            // Kontoverbindung international: IBAN:BIC:Kontonummer:Unterkontomerkmal:280:BLZ, or the national form
            // ::Kontonummer:Unterkontomerkmal:280:BLZ for an account without IBAN.
            string national = DEG.Separator + DEG.Separator + connectionDetails.Account + DEG.Separator + connectionDetails.SubAccount
                + DEG.Separator + SEG_COUNTRY.Germany + DEG.Separator + connectionDetails.Blz;
            return string.IsNullOrWhiteSpace(connectionDetails.Iban)
                ? national
                : connectionDetails.Iban + DEG.Separator + connectionDetails.Bic + national.Substring(DEG.Separator.Length);
        }

        /// <summary>
        /// Sends a credit card order (DKKKU, HKKKU or HKKKS, which share the layout up to the card account):
        /// Kontoverbindung + Kreditkartennummer (the account number from the UPD) + Unterkontomerkmal, then the
        /// order's further data elements (for DKKKU/HKKKU Von Datum + Bis Datum + Maximale Anzahl Einträge
        /// + Aufsetzpunkt), followed by HKTAN when the BPD requires a TAN for the order.
        /// </summary>
        internal static async Task<String> SendCreditCardOrder(FinTsClient client, string job, int version, string account,
            params string[] furtherElements)
        {
            var connectionDetails = client.ConnectionDetails;

            client.SEGNUM = Convert.ToInt16(SEG_NUM.Seg3);

            SEG sEG = new SEG();

            // Trailing empty data elements are omitted; they are dropped before joining, since trimming the
            // joined text would also strip an escaped "?+" at the end of the continuation point.
            var elements = new List<string> { account, connectionDetails.Account, connectionDetails.SubAccount };
            elements.AddRange(furtherElements);
            while (elements.Count > 0 && string.IsNullOrEmpty(elements[elements.Count - 1]))
                elements.RemoveAt(elements.Count - 1);
            string rawData = string.Join(sEG.Delimiter, elements);

            string segments = sEG.toSEG(new SEG_DATA
            {
                Header = job,
                Num = client.SEGNUM,
                Version = version,
                RefNum = 0,
                RawData = rawData + sEG.Terminator
            });

            if (client.BPD.IsTANRequired(job))
            {
                client.SEGNUM = Convert.ToInt16(SEG_NUM.Seg4);
                segments = HKTAN.Init_HKTAN(client, segments, job);
            }

            string message = FinTSMessage.Create(client, client.HNHBS, client.HNHBK, segments, client.HIRMS);
            string response = await FinTSMessage.Send(client, message);

            client.Parse_Message(response);

            return response;
        }
    }
}
