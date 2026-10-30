using EdgeTtsSharp.Exception;
using EdgeTtsSharp.Helper;
using PyException.common;
using SharpUtility.Extension;
using SharpUtility.Helper;
using System.Globalization;
using System.Net.Http.Headers;
using System.Numerics;
using System.Text;

namespace EdgeTtsSharp;

/// <summary>
/// Handles DRM operations with clock skew correction.
/// </summary>
internal static class Drm
{
    private const long WIN_EPOCH = 11644473600;
    private const double S_TO_NS = 1e9;
    private static double clock_skew_seconds = 0.0;

    // FIPS 180-4:
    // https://nvlpubs.nist.gov/nistpubs/FIPS/NIST.FIPS.180-4.pdf
    private static readonly uint[] _SHA256_INITIAL = [
                0x6A09E667,
                0xBB67AE85,
                0x3C6EF372,
                0xA54FF53A,
                0x510E527F,
                0x9B05688C,
                0x1F83D9AB,
                0x5BE0CD19,
            ];

    private static readonly uint[] _SHA256_STATE = [
            0xFD9FBEA2,
            0x91243CE7,
            0xBE499094,
            0xCD2E1E0A,
            0x6C006F49,
            0x04BD35AB,
            0xFEB0A7E4,
            0x03EFFBD2
        ];

    private static readonly uint[] _SHA256_SCHEDULE = [
            0xEB24BF9E,
            0x1558691F,
            0xA0C09EE2,
            0x0CFA32BA,
            0xA622EE8C,
            0xD1DD9491,
            0xC0DD25A0,
            0xF9E70AB6,
            0xF0C31573,
            0x7058CE4E,
            0x7188E606,
            0x00E5AB33,
            0xD258B060,
            0x72766564
        ];

    private static readonly uint[] _SHA256_ROUND_CONSTANTS = [
            0x428A2F98,
            0x71374491,
            0xB5C0FBCF,
            0xE9B5DBA5,
            0x3956C25B,
            0x59F111F1,
            0x923F82A4,
            0xAB1C5ED5,
            0xD807AA98,
            0x12835B01,
            0x243185BE,
            0x550C7DC3,
            0x72BE5D74,
            0x80DEB1FE,
            0x9BDC06A7,
            0xC19BF174,
            0xE49B69C1,
            0xEFBE4786,
            0x0FC19DC6,
            0x240CA1CC,
            0x2DE92C6F,
            0x4A7484AA,
            0x5CB0A9DC,
            0x76F988DA,
            0x983E5152,
            0xA831C66D,
            0xB00327C8,
            0xBF597FC7,
            0xC6E00BF3,
            0xD5A79147,
            0x06CA6351,
            0x14292967,
            0x27B70A85,
            0x2E1B2138,
            0x4D2C6DFC,
            0x53380D13,
            0x650A7354,
            0x766A0ABB,
            0x81C2C92E,
            0x92722C85,
            0xA2BFE8A1,
            0xA81A664B,
            0xC24B8B70,
            0xC76C51A3,
            0xD192E819,
            0xD6990624,
            0xF40E3585,
            0x106AA070,
            0x19A4C116,
            0x1E376C08,
            0x2748774C,
            0x34B0BCB5,
            0x391C0CB3,
            0x4ED8AA4A,
            0x5B9CCA4F,
            0x682E6FF3,
            0x748F82EE,
            0x78A5636F,
            0x84C87814,
            0x8CC70208,
            0x90BEFFFA,
            0xA4506CEB,
            0xBEF9A3F7,
            0xC67178F2
        ];

    private const uint _SHA256_ADJUSTMENT = 0xC9DC06A7;
    private static readonly uint[] _SHA256_BLOCK_CONSTANTS = EnumerableHelper.Concat(
        _SHA256_ROUND_CONSTANTS.Take(14), [_SHA256_ADJUSTMENT], _SHA256_ROUND_CONSTANTS.Skip(15)
    ).ToArray();
    private const uint _SHA256_SIGMA0_ADJUSTMENT = 0x059C0B80;
    private const uint _SHA256_SIGMA1_ADJUSTMENT = 0x000B92C0;

    private static readonly List<(uint, uint)> _CS64_COEFFICIENTS = [
            (0xE82E91B1, 0xB32700D0),
            (0x44DE24C6, 0x88DFF51D),
            (0x3BEB65BC, 0x29514038),
            (0xC89C5933, 0xB328CBDF),
            (0x8D1EC211, 0xD405B4D5),
            (0x314EF5D5, 0x26E55F3C),
            (0xE92491B1, 0x607801C6),
            (0xF0A5B9D5, 0x82B2C1FB),
            (0x06064CF3, 0xFA51A3DA),
            (0xDA79FA60, 0x207CC464),
            (0xBED20560, 0x3C3C74F3),
            (0x89F9C9F8, 0xD2D14B61),
            (0x35FA38C6, 0xBB04A2C1),
            (0xCB49E3C6, 0xC5626167)
        ];
    private const uint _CS64_FACTOR = 0x82893933;
    private const uint _CS64_ADJUSTMENT0 = 0xF2F90B1A;
    private const uint _CS64_ADJUSTMENT1 = 0x0B1A0000;

    private const uint _U32_MASK = 0xFFFFFFFF;



    /// <summary>
    /// Adjusts the clock skew based on the server's Date header.
    /// Throws SkewAdjustmentException if the server date header is missing or unparseable.
    /// </summary>
    public static void HandleClientResponseError(HttpResponseHeaders headers)
    {
        if (headers.Date.HasValue)
        {
            var serverDate = headers.Date.Value.ToUnixTimeSeconds();
            var clientDate = GetUnixTimestamp();

            clock_skew_seconds += serverDate - clientDate;

            return;
        }

        if (headers.TryGetValues("Date", out var dateValues))
        {
            var dateString = dateValues.FirstOrDefault();

            if (!string.IsNullOrEmpty(dateString))
            {
                var serverDate = ParseRfc2616Date(dateString);

                if (serverDate.HasValue)
                {
                    var clientDate = GetUnixTimestamp();

                    clock_skew_seconds += serverDate.Value - clientDate;

                    return;
                }

                throw new SkewAdjustmentException($"Failed to parse server date: {dateString}");
            }
        }

        throw new SkewAdjustmentException("No server date in headers.");
    }

    /// <summary>
    /// Adjusts the clock skew based on a dictionary of headers (e.g. from WebSocket response).
    /// </summary>
    public static void HandleClientResponseError(IDictionary<string, string> headers)
    {
        if (headers.TryGetValue("Date", out var dateString) && !string.IsNullOrEmpty(dateString))
        {
            var serverDate = ParseRfc2616Date(dateString);

            if (serverDate.HasValue)
            {
                var clientDate = GetUnixTimestamp();

                clock_skew_seconds += serverDate.Value - clientDate;

                return;
            }

            throw new SkewAdjustmentException($"Failed to parse server date: {dateString}");
        }

        throw new SkewAdjustmentException("No server date in headers.");
    }

    private static double GetUnixTimestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + clock_skew_seconds;
    }

    /// <summary>
    /// Parses an RFC 2616 date string into a Unix timestamp.
    /// </summary>
    private static long? ParseRfc2616Date(string date)
    {
        string[] formats = new[]
        {
            "ddd, dd MMM yyyy HH:mm:ss 'GMT'",
            "ddd, dd MMM yyyy HH:mm:ss Z",
            "ddd, dd MMM yyyy HH:mm:ss K"
        };

        if (DateTimeOffset.TryParseExact(date, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsedDate))
        {
            return parsedDate.ToUnixTimeSeconds();
        }

        return null;
    }

    /// <summary>
    /// Generates a random MUID.
    /// </summary>
    public static string GenerateMuid()
    {
        return Guid.NewGuid().ToString("N").ToUpper();
    }

    /// <summary>
    /// Gets the current timestamp in Unix format with clock skew correction.
    /// </summary>
    /// <returns>The current timestamp in Unix format with clock skew correction.</returns>
    public static double get_unix_timestamp()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds() + clock_skew_seconds;
    }

    /// <summary>
    /// Generates the Sec-MS-GEC token value.
    /// 
    /// The token combines CS64 and SHA256 digests in the format
    ///"<16 hex>-<64 hex>".
    /// </summary>
    /// <param name="unix_timestamp">unix_timestamp (float): Value to use instead of the current time, for testing</param>
    /// <returns>The generated Sec-MS-GEC token value.</returns>
    public static string generate_sec_ms_gec(double? unix_timestamp = null)
    {
        double ticks = 0;

        if (unix_timestamp is null)
        {
            ticks = get_unix_timestamp();
        }
        else
        {
            ticks = unix_timestamp.Value;
        }

        ticks += WIN_EPOCH;
        ticks -= ticks % 300;
        ticks *= S_TO_NS / 100;

        var str_to_hash = Encoding.ASCII.GetBytes($"{ticks.ToFixed(0)}{Constants.TRUSTED_CLIENT_TOKEN}");

        var (cs64, sha256) = _token_hash(str_to_hash);

        return $"{cs64}-{sha256}";
    }

    /// <summary>
    /// Compute the token hash components.
    /// </summary>
    /// <param name="bytes"></param>
    /// <returns></returns>
    private static (string, string) _token_hash(byte[] data)
    {
        var digest = _sha256_digest(data);

        var items = _cs64_digest(data, digest);
        var bytes = PackHelper.PackTwoUInt32LittleEndian((uint)items.Item1, (uint)items.Item2);
        var cs64 = Convert.ToHexString(bytes).ToUpper();

        return (cs64, Convert.ToHexString(digest.Select(item => (byte)item).ToArray()).ToUpper());
    }

    /// <summary>
    /// Compute the CS64 digest words for token data.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="digest"></param>
    /// <returns></returns>
    private static (long, long) _cs64_digest(byte[] data, byte[] digest)
    {
        var mask = _U32_MASK;
        var (key_a, key_b) = PackHelper.UnpackTwoUInt32LittleEndian(digest);
        key_a |= 1;
        key_b |= 1;

        var factors = ((key_a * _CS64_FACTOR) & mask, (key_b * _CS64_FACTOR) & mask);

        var terms = new List<dynamic>();

        int index = 0;

        foreach (var item in _CS64_COEFFICIENTS)
        {
            var coefficient = item.Item1;

            terms.Add((coefficient * (index % 2 == 0 ? factors.Item1 : factors.Item2)) & mask);

            index++;
        }

        var offsets = _CS64_COEFFICIENTS.Select(item => (long)item.Item2).ToList();

        index = 0;

        var bytes = EnumerableHelper.Concat([(byte)0x00], data);

        foreach (var word in _words_le4_even(bytes.ToArray()))
        {
            var term = word * (index % 2 == 0 ? key_a : key_b);
            var offset = _rol16(word) * (index % 2 == 0 ? 0xEF0569FB : 0xC31713DB);

            if (index == 0)
            {
                term += _CS64_ADJUSTMENT0 * factors.Item1;
                offset += _CS64_ADJUSTMENT1;
            }

            terms.Add(term & mask);
            offsets.Add(offset & mask);

            index++;
        }

        long swap_acc = 0, swap_last = 0, state = 0, acc = 0;

        foreach (var i in RangeHelper.GetRange(0, terms.Count, 2))
        {
            index = i;

            var (x, y) = (terms[index], terms[index + 1]);
            var t = _rol16((offsets[index] + x) & mask);
            var q = (t * 0x76F50F34) & mask;
            swap_acc += (_rol16(q) * 0xC3EFEA97 + q) & mask;
            t = _rol16((offsets[index + 1] + y) & mask);
            q = (t * 0x1881CE3C) & mask;
            swap_last = (_rol16(q) * 0x35BD1EC9 + q) & mask;
            swap_acc = (swap_acc + swap_last) & mask;

            state = (state * key_a + x) & mask;
            state = (_rol16(state) * 0xCF98B111) & mask;
            state = (_rol16(state) * 0x87085B9F) & mask;
            state = (_rol16(state) * 0x12CEB96D) & mask;
            state = (_rol16(state) * 0x257E1D83) & mask;
            acc += state;
            t = (state * key_b + y) & mask;
            t = (_rol16(t) * 0xA27416F5) & mask;
            t = (_rol16(t) * 0xD38396FF) & mask;
            state = (_rol16((_rol16(t) * 0x7C932B89) & mask) * 0xBFA49F69) & mask;
            acc = (acc + state) & mask;
        }

        return (swap_last ^ state, swap_acc ^ acc);
    }

    /// <summary>
    /// Rotate a 32-bit value left by 16 bits.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static long _rol16(long value)
    {
        value &= _U32_MASK;

        return ((value << 16) | (value >> 16)) & _U32_MASK;
    }

    /// <summary>
    /// Split data into little-endian 32-bit words, zero-padded to an even count.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static List<uint> _words_le4_even(byte[] data)
    {
        var padded = EnumerableHelper.Concat(data, Enumerable.Repeat((byte)0x00, Utility.GetMod(-data.Length % 4, 4))).ToList();

        if (padded.Count == 0)
        {
            return new List<uint>();
        }

        var number = (int)Math.Floor(padded.Count / 4.0);

        var words = PackHelper.Unpack(padded.ToArray(), number).ToList();

        if (words.Count % 2 != 0)
        {
            words.Add(0);
        }

        return words;
    }

    /// <summary>
    /// Compute the SHA-256 digest for token data.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static byte[] _sha256_digest(byte[] data)
    {
        if (data.Length < 7)
        {
            throw new ValueError("Token salt must contain at least seven bytes");
        }

        var mask = _U32_MASK;
        var length = 57 + data.Length;
        var padded = EnumerableHelper.Concat(data.Skip(7), [(byte)0x80], Enumerable.Repeat((byte)0x00, Utility.GetMod((55 - length) % 64, 64))).ToList();

        padded.AddRange( PackHelper.PackBigEndianUInt64((ulong)(length * 8)));

        var state = _sha256_init(data.Take(7).ToArray()).Select(item => (BigInteger)item).ToList();

        foreach (var offset in RangeHelper.GetRange(0, padded.Count, 64))
        {
            var words = PackHelper.Unpack16IBigEndian(padded.Skip(offset).Take(64).ToArray()).Select(item => (long)item).ToList();

            _expand_sha256_schedule(words);

            var working = _sha256_rounds(state.ToArray(), words);

            state = state.Zip(working).Select(item => (item.First + item.Second) & mask).ToList();
        }

        return PackHelper.Pack8IBigEndian(state.Select(item => (uint)item).ToArray());
    }

    /// <summary>
    /// Compute the initial SHA-256 state for token data.
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static List<uint> _sha256_init(byte[] data)
    {
        var mask = _U32_MASK;
        List<long> words = EnumerableHelper.Concat(Enumerable.Repeat(0l, 14), [Utility.FromBytesBigEndian(data.Take(3).ToArray())]).ToList();

        words.Add(Utility.FromBytesBigEndian(data.Skip(3).Take(4).ToArray()));

        foreach (var index in RangeHelper.GetRange(16, 30))
        {
            long value = _SHA256_SCHEDULE[index - 16];

            if (index - 2 >= 14)
            {
                value += index == 16 ? (_sigma1(words[index - 2]) ^ _SHA256_SIGMA1_ADJUSTMENT) : _sigma1(words[index - 2]);
            }

            if (index - 7 >= 14)
            {
                value += words[index - 7];
            }

            if (index - 15 >= 14)
            {
                value += index == 29 ? (_sigma0(words[index - 15]) ^ _SHA256_SIGMA0_ADJUSTMENT) : _sigma0(words[index - 15]);
            }

            words.Add(value & mask);
        }

        words.Add(
        (
            _sigma1(words[28])
                + words[23]
                + _sigma0(words[15])
                + words[14]
                + _SHA256_ADJUSTMENT
                - _SHA256_ROUND_CONSTANTS[14]
            )
            & mask
        );

        _expand_sha256_schedule(words, 31);

        var working = _sha256_rounds(_SHA256_STATE.Select(item => (BigInteger)item).ToArray(), words, 14);

        return _SHA256_INITIAL.Zip(working.Select(item => (uint)item)).Select(item => (item.First + item.Second) & mask).ToList();
    }

    /// <summary>
    /// Run SHA-256 rounds, optionally resuming inside the first block.
    /// </summary>
    /// <param name="state"></param>
    /// <param name="words"></param>
    /// <param name="first_round"></param>
    /// <returns></returns>
    private static List<BigInteger> _sha256_rounds(BigInteger[] state, List<long> words, int first_round = 0)
    {
        BigInteger a = state[0], b = state[1], c = state[2], d = state[3], e = state[4], f = state[5], g = state[6], h = state[7];

        var mask = _U32_MASK;
        var constants = first_round == 14 ? _SHA256_BLOCK_CONSTANTS : _SHA256_ROUND_CONSTANTS;

        // Multiplying a 32-bit word by 2**32 + 1 repeats it in 64 bits.
        // A right shift then has ROTR's low 32 bits, with no separate left shift.
        // The final additions mask off the unused high bits.
        var aligned = (int)Math.Min(64, (Math.Floor((first_round + 7) / 8.0)) * 8);

        foreach (var index in RangeHelper.GetRange(first_round, aligned))
        {
            var rot_e = e * 0x100000001;
            var rot_a = a * 0x100000001;

            var t1 = (
                h
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (g ^ (e & (f ^ g)))
                + constants[index]
                + words[index]
            );

            var t2 = ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22)) + ((a & b) | (c & (a | b)));
            (a, b, c, d, e, f, g, h) = ((t1 + t2) & mask, a, b, c, (d + t1) & mask, e, f, g);
        }

        // Eight rounds return the working-register names to their original order.
        // Rotate their roles in the expressions instead of moving eight values
        // after every round; only the two updated registers need assignments.
        foreach (var index in RangeHelper.GetRange(aligned, 64, 8))
        {
            var rot_e = e * 0x100000001;
            var rot_a = a * 0x100000001;

            var t1 = (
                h
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (g ^ (e & (f ^ g)))
                + constants[index]
                + words[index]
            );

            d = (d + t1) & mask;
            h = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((a & b) | (c & (a | b)))
            ) & mask;

            rot_e = d * 0x100000001;
            rot_a = h * 0x100000001;
            t1 = (
                g
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (f ^ (d & (e ^ f)))
                + constants[index + 1]
                + words[index + 1]
            );

            c = (c + t1) & mask;
            g = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((h & a) | (b & (h | a)))
            ) & mask;
            rot_e = c * 0x100000001;
            rot_a = g * 0x100000001;
            t1 = (
                f
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (e ^ (c & (d ^ e)))
                + constants[index + 2]
                + words[index + 2]
            );
            b = (b + t1) & mask;
            f = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((g & h) | (a & (g | h)))
            ) & mask;
            rot_e = b * 0x100000001;
            rot_a = f * 0x100000001;
            t1 = (
                e
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (d ^ (b & (c ^ d)))
                + constants[index + 3]
                + words[index + 3]
            );
            a = (a + t1) & mask;
            e = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((f & g) | (h & (f | g)))
            ) & mask;
            rot_e = a * 0x100000001;
            rot_a = e * 0x100000001;
            t1 = (
                d
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (c ^ (a & (b ^ c)))
                + constants[index + 4]
                + words[index + 4]
            );
            h = (h + t1) & mask;
            d = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((e & f) | (g & (e | f)))
            ) & mask;
            rot_e = h * 0x100000001;
            rot_a = d * 0x100000001;
            t1 = (
                c
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (b ^ (h & (a ^ b)))
                + constants[index + 5]
                + words[index + 5]
            );
            g = (g + t1) & mask;
            c = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((d & e) | (f & (d | e)))
            ) & mask;
            rot_e = g * 0x100000001;
            rot_a = c * 0x100000001;
            t1 = (
                b
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (a ^ (g & (h ^ a)))
                + constants[index + 6]
                + words[index + 6]
            );
            f = (f + t1) & mask;
            b = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((c & d) | (e & (c | d)))
            ) & mask;
            rot_e = f * 0x100000001;
            rot_a = b * 0x100000001;
            t1 = (
                a
                + ((rot_e >> 6) ^ (rot_e >> 11) ^ (rot_e >> 25))
                + (h ^ (f & (g ^ h)))
                + constants[index + 7]
                + words[index + 7]
            );
            e = (e + t1) & mask;
            a = (
                t1
                + ((rot_a >> 2) ^ (rot_a >> 13) ^ (rot_a >> 22))
                + ((b & c) | (d & (b | c)))
            ) & mask;
        }

        return new List<BigInteger>() { a, b, c, d, e, f, g, h };
    }

    /// <summary>
    /// Expand SHA-256 words, combining rotations before modular addition.
    /// </summary>
    /// <param name="words"></param>
    /// <param name="first_word"></param>
    private static void _expand_sha256_schedule(List<long> words, int first_word = 16)
    {
        var mask = _U32_MASK;

        foreach (var index in RangeHelper.GetRange(first_word, 64))
        {
            var x = words[index - 2];
            var y = words[index - 15];

            // Rotations can retain their high bits until the final sum is masked.
            var sigma1 = (x >> 17) ^ (x << 15) ^ (x >> 19) ^ (x << 13) ^ (x >> 10);
            var sigma0 = (y >> 7) ^ (y << 25) ^ (y >> 18) ^ (y << 14) ^ (y >> 3);

            words.Add((sigma1 + words[index - 7] + sigma0 + words[index - 16]) & mask);
        }
    }

    /// <summary>
    /// SHA-256 small sigma one, truncating the combined rotations once.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static long _sigma1(long value)
    {
        return ((value >> 17) ^ (value << 15) ^ (value >> 19) ^ (value << 13) ^ (value >> 10)) & _U32_MASK;
    }

    /// <summary>
    /// SHA-256 small sigma zero, truncating the combined rotations once.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    private static long _sigma0(long value)
    {
        return ((value >> 7) ^ (value << 25) ^ (value >> 18) ^ (value << 14) ^ (value >> 3)) & _U32_MASK;
    }
}