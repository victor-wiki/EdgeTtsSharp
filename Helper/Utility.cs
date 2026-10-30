namespace EdgeTtsSharp.Helper
{
    public class Utility
    {
        public static int GetMod(int mod, int fixNumber)
        {
            if(mod<0)
            {
                return fixNumber + mod;
            }

            return mod;
        }

        public static long FromBytesBigEndian(byte[] data)
        {
            long value = 0;

            foreach (byte b in data)
            {
                value = (value << 8) | b;
            }

            return value;
        }
    }
}
