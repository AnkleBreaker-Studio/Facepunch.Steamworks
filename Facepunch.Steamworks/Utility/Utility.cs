using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;

namespace Steamworks
{
	public static partial class Utility
    {
	    public static readonly Encoding Utf8NoBom = new UTF8Encoding( false, false );

        /// <summary>
        /// Reads a native struct out of unmanaged memory.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>This allocates, and it is on the hottest path in the library.</b> It runs once
        /// per registered handler per delivered callback, so the cost is paid continuously
        /// for the lifetime of the process.
        /// </para>
        /// <para>
        /// Both <c>Marshal.PtrToStructure</c> overloads box. The generic
        /// <c>PtrToStructure&lt;T&gt;</c> looks like it should not, but internally it does
        /// <c>Activator.CreateInstance</c> into an <c>object</c>, fills that, and unboxes on
        /// return. Measured on CoreCLR with a blittable 24-byte struct:
        /// </para>
        /// <code>
        ///   PtrToStructure&lt;T&gt;( ptr )            40 B/op    126 ns/op
        ///   PtrToStructure( ptr, typeof( T ) )   40 B/op    122 ns/op
        ///   *(T*)ptr                              0 B/op    8.5 ns/op
        /// </code>
        /// <para>
        /// 40 bytes is exactly <c>sizeof(T) + 16</c>, i.e. one boxed instance. So the two
        /// overloads are equivalent - swapping between them changes nothing, and an earlier
        /// attempt to "fix" this by switching overloads was measured to have no effect.
        /// Do not repeat it.
        /// </para>
        /// <para>
        /// The real fix is a raw pointer read, which is ~14x faster and allocates nothing.
        /// It requires <c>where T : unmanaged</c>, which the C# compiler will only accept
        /// for blittable types - and several callback structs are not blittable today
        /// because they carry <c>[MarshalAs(ByValTStr)] string</c> fields (see
        /// <c>ConnectionInfo</c>). Converting those to <c>fixed byte</c> with on-demand
        /// decoding is what unlocks this. Tracked in <c>docs/audit/06-performance.md</c>;
        /// it is sequenced behind the struct-layout work because it changes marshalled
        /// layout and must move the baseline in <c>Tools/baselines/</c> deliberately.
        /// </para>
        /// <para>
        /// Where the type IS statically known to be blittable, call
        /// <see cref="ToTypeUnmanaged{T}"/> instead - it is the zero-allocation path.
        /// </para>
        /// </remarks>
        static internal T ToType<T>( this IntPtr ptr )
        {
            if ( ptr == IntPtr.Zero )
                return default;

            return Marshal.PtrToStructure<T>( ptr );
        }

        /// <summary>
        /// Reads a blittable native struct out of unmanaged memory with no allocation.
        /// </summary>
        /// <remarks>
        /// The zero-allocation counterpart to <see cref="ToType{T}"/>: measured at 0 bytes
        /// and ~8.5 ns/op against 40 bytes and ~126 ns/op for <c>Marshal.PtrToStructure</c>.
        ///
        /// The <c>unmanaged</c> constraint is what makes this safe - the compiler refuses
        /// any type containing a reference (such as a <c>[MarshalAs(ByValTStr)] string</c>
        /// field), which is exactly the set that needs real marshalling. If a type will not
        /// satisfy the constraint, it genuinely cannot use this path; use
        /// <see cref="ToType{T}"/> for it.
        ///
        /// Deliberately not written using <c>System.Runtime.CompilerServices.Unsafe</c>:
        /// that would add a NuGet dependency to a library shipped into Unity projects as
        /// loose DLLs, and a plain pointer dereference measures identically.
        /// </remarks>
        static internal unsafe T ToTypeUnmanaged<T>( this IntPtr ptr ) where T : unmanaged
        {
            if ( ptr == IntPtr.Zero )
                return default;

            return *(T*)ptr;
        }

        static internal object ToType( this IntPtr ptr, System.Type t )
        {
            if ( ptr == IntPtr.Zero )
                return default;

            return Marshal.PtrToStructure( ptr, t );
        }

        static internal uint Swap( uint x )
        {
            return ((x & 0x000000ff) << 24) +
                   ((x & 0x0000ff00) << 8) +
                   ((x & 0x00ff0000) >> 8) +
                   ((x & 0xff000000) >> 24);
        }

        static public uint IpToInt32( this IPAddress ipAddress )
        {
            return Swap( (uint) ipAddress.Address );
        }

        static public IPAddress Int32ToIp( uint ipAddress )
        {
            return new IPAddress( Swap( ipAddress ) );
        }

		public static string FormatPrice(string currency, double price)
        {
			var decimaled = price.ToString("0.00");

            switch (currency)
            {
                case "AED": return $"{decimaled}د.إ";
                case "ARS": return $"${decimaled} ARS";
                case "AUD": return $"A${decimaled}";
                case "BRL": return $"R${decimaled}";
                case "CAD": return $"C${decimaled}";
                case "CHF": return $"Fr. {decimaled}";
                case "CLP": return $"${decimaled} CLP";
                case "CNY": return $"{decimaled}元";
                case "COP": return $"COL$ {decimaled}";
                case "CRC": return $"₡{decimaled}";
                case "EUR": return $"€{decimaled}";
                case "SEK": return $"{decimaled}kr";
                case "GBP": return $"£{decimaled}";
                case "HKD": return $"HK${decimaled}";
                case "ILS": return $"₪{decimaled}";
                case "IDR": return $"Rp{decimaled}";
                case "INR": return $"₹{decimaled}";
                case "JPY": return $"¥{decimaled}";
                case "KRW": return $"₩{decimaled}";
                case "KWD": return $"KD {decimaled}";
                case "KZT": return $"{decimaled}₸";
                case "MXN": return $"Mex${decimaled}";
                case "MYR": return $"RM {decimaled}";
                case "NOK": return $"{decimaled} kr";
                case "NZD": return $"${decimaled} NZD";
                case "PEN": return $"S/. {decimaled}";
                case "PHP": return $"₱{decimaled}";
                case "PLN": return $"{decimaled}zł";
                case "QAR": return $"QR {decimaled}";
                case "RUB": return $"{decimaled}₽";
                case "SAR": return $"SR {decimaled}";
                case "SGD": return $"S${decimaled}";
                case "THB": return $"฿{decimaled}";
                case "TRY": return $"₺{decimaled}";
                case "TWD": return $"NT$ {decimaled}";
                case "UAH": return $"₴{decimaled}";
                case "USD": return $"${decimaled}";
                case "UYU": return $"$U {decimaled}"; // yes the U goes after $
                case "VND": return $"₫{decimaled}";
                case "ZAR": return $"R {decimaled}";

                // TODO - check all of them https://partner.steamgames.com/doc/store/pricing/currencies

                default: return $"{decimaled} {currency}";
            }
        }

		static readonly byte[] readBuffer = new byte[1024 * 8];

		public static string ReadNullTerminatedUTF8String( this BinaryReader br )
		{
			lock ( readBuffer )
			{
				byte chr;
				int i = 0;
				while ( (chr = br.ReadByte()) != 0 && i < readBuffer.Length )
				{
					readBuffer[i] = chr;
					i++;
				}

				return Utf8NoBom.GetString( readBuffer, 0, i );
			}
		}
	}
}
