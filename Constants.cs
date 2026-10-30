namespace EdgeTtsSharp;

/// <summary>
/// Contains constants for the EdgeTTS library.
/// </summary>
internal static class Constants
{
    public const string BASE_URL = "speech.platform.bing.com/consumer/speech/synthesize/readaloud";
    public const string TRUSTED_CLIENT_TOKEN = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";

    public static readonly string WSS_URL = $"wss://{BASE_URL}/edge/v1?TrustedClientToken={TRUSTED_CLIENT_TOKEN}";
    public static readonly string VOICE_LIST = $"https://{BASE_URL}/voices/list?trustedclienttoken={TRUSTED_CLIENT_TOKEN}";

    public const string DEFAULT_VOICE = "en-US-EmmaMultilingualNeural";

    private const string CHROMIUM_FULL_VERSION = "143.0.3650.75";
    private static readonly string CHROMIUM_MAJOR_VERSION = CHROMIUM_FULL_VERSION.Split('.')[0];
    public static readonly string SEC_MS_GEC_VERSION = $"2-{CHROMIUM_FULL_VERSION}";

    public static readonly Dictionary<string, string> BASE_HEADERS = new()
    {
        { "User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{CHROMIUM_MAJOR_VERSION}.0.0.0 Safari/537.36 Edg/{CHROMIUM_MAJOR_VERSION}.0.0.0" },
        { "Accept-Encoding", "gzip, deflate, br, zstd" },
        { "Accept-Language", "en-US,en;q=0.9" }
    };

    public static readonly Dictionary<string, string> WSS_HEADERS = new()
    {
        { "Pragma", "no-cache" },
        { "Cache-Control", "no-cache" },
        { "Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold" },
        { "User-Agent", BASE_HEADERS["User-Agent"] },
        { "Accept-Language", BASE_HEADERS["Accept-Language"] }
    };

    public static readonly Dictionary<string, string> VOICE_HEADERS = new()
    {
        { "Authority", "speech.platform.bing.com" },
        { "Sec-CH-UA", $"\" Not;A Brand\";v=\"99\", \"Microsoft Edge\";v=\"{CHROMIUM_MAJOR_VERSION}\", \"Chromium\";v=\"{CHROMIUM_MAJOR_VERSION}\"" },
        { "Sec-CH-UA-Mobile", "?0" },
        { "Accept", "*/*" },
        { "Sec-Fetch-Site", "none" },
        { "Sec-Fetch-Mode", "cors" },
        { "Sec-Fetch-Dest", "empty" },
        { "User-Agent", BASE_HEADERS["User-Agent"] },
        { "Accept-Encoding", BASE_HEADERS["Accept-Encoding"] },
        { "Accept-Language", BASE_HEADERS["Accept-Language"] }
    };

    // Audio timing constants for CBR-based offset compensation.
    public const long TICKS_PER_SECOND = 10_000_000;
    public const int MP3_BITRATE_BPS = 48_000;
}