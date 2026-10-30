using System.Buffers.Binary;

namespace EdgeTtsSharp.Helper
{
    public class PackHelper
    {
        public static byte[] PackBigEndianUInt64(ulong value)
        {
            byte[] bytes = BitConverter.GetBytes(value);

            if (BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }

            return bytes;
        }

        public static uint[] Unpack16IBigEndian(byte[] data)
        {
            if (data == null || data.Length < 64)
            {
                throw new ArgumentException();
            }

            uint[] result = new uint[16];

            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                for (int i = 0; i < 16; i++)
                {
                    byte b0 = reader.ReadByte();
                    byte b1 = reader.ReadByte();
                    byte b2 = reader.ReadByte();
                    byte b3 = reader.ReadByte();

                    result[i] = ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | b3;
                }
            }

            return result;
        }

        public static uint[] Unpack8IBigEndian(byte[] data)
        {
            if (data.Length < 32)
            {
                throw new ArgumentException();
            }

            uint[] result = new uint[8];

            for (int i = 0; i < 8; i++)
            {
                result[i] = BinaryPrimitives.ReadUInt32BigEndian(new ReadOnlySpan<byte>(data.Skip(i * 4).Take(4).ToArray()));
            }

            return result;
        }

        public static (uint val1, uint val2) UnpackTwoUInt32LittleEndian(byte[] buffer, int offset = 0)
        {

            uint val1 = BitConverter.ToUInt32(buffer, offset);
            uint val2 = BitConverter.ToUInt32(buffer, offset + 4);

            return (val1, val2);
        }

        public static uint[] Unpack(byte[] data, int length)
        {
            if (data == null)
            {
                throw new ArgumentException();
            }

            uint[] result = new uint[length];

            for (int i = 0; i < length; i++)
            {
                result[i] = BitConverter.ToUInt32(data, i * 4);
            }

            return result;
        }

        public static byte[] PackTwoUInt32LittleEndian(uint a, uint b)
        {
            byte[] bytes = new byte[8];

            byte[] ba = BitConverter.GetBytes(a);
            byte[] bb = BitConverter.GetBytes(b);

            Buffer.BlockCopy(ba, 0, bytes, 0, 4);
            Buffer.BlockCopy(bb, 0, bytes, 4, 4);

            return bytes;
        }

        public static byte[] Pack8IBigEndian(uint[] state)
        {
            if (state == null || state.Length != 8)
            {
                throw new ArgumentException();
            }

            byte[] buffer = new byte[32];

            for (int i = 0; i < 8; i++)
            {
                uint value = state[i];
                int offset = i * 4;

                buffer[offset] = (byte)(value >> 24);
                buffer[offset + 1] = (byte)(value >> 16);
                buffer[offset + 2] = (byte)(value >> 8);
                buffer[offset + 3] = (byte)value;
            }

            return buffer;
        }
    }
}
