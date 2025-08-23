using ConsoleGame.RayTracing;
using System.Runtime.InteropServices;

namespace ConsoleGame.Renderer
{
    [StructLayout(LayoutKind.Sequential, Size = 4, Pack = 4)]
    public struct RGBA32
    {
        public byte a;
        public byte g;
        public byte b;
        public byte r;

        public unsafe RGBA32(int value)
        {
            byte* bytePtr = (byte*)&value;
            r = bytePtr[0];
            g = bytePtr[1];
            b = bytePtr[2];
            a = bytePtr[3];
        }

        public unsafe int ToInt()
        {
            int result;
            byte* bytePtr = (byte*)&result;
            bytePtr[0] = r;
            bytePtr[1] = g;
            bytePtr[2] = b;
            bytePtr[3] = a;
            return result;
        }

        public RGBA32()
        {
            r = 0;
            g = 0;
            b = 0;
            a = 255;
        }

        public RGBA32(Vec3 col)
        {
            r = (byte)(col.X * 255);
            g = (byte)(col.Y * 255);
            b = (byte)(col.Z * 255);
            a = 255;
        }

        public RGBA32(float x, float y, float z)
        {
            r = (byte)(x * 255);
            g = (byte)(y * 255);
            b = (byte)(z * 255);
            a = 255;
        }

        public RGBA32(float x, float y, float z, float a)
        {
            r = (byte)(x * 255);
            g = (byte)(y * 255);
            b = (byte)(z * 255);
            a = (byte)(a * 255);
        }

        public RGBA32(float x)
        {
            r = (byte)(x * 255);
            g = (byte)(x * 255);
            b = (byte)(x * 255);
            a = 255;
        }

        public RGBA32(byte r, byte g, byte b, byte a)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public Vec3 toVec3()
        {
            return new Vec3(r / 255f, g / 255f, b / 255f);
        }

        public static RGBA32 Lerp(RGBA32 a, RGBA32 b, float t)
        {
            return new RGBA32((byte)(a.r + t * (b.r - a.r)), (byte)(a.g + t * (b.g - a.g)), (byte)(a.b + t * (b.b - a.b)), (byte)(a.a + t * (b.a - a.a)));
        }

        public byte this[int index]
        {
            get
            {
                switch (index)
                {
                    case 0: return r;
                    case 1: return g;
                    case 2: return b;
                    case 3: return a;
                    default: return 0;
                }
            }
            set
            {
                switch (index)
                {
                    case 0: r = value; break;
                    case 1: g = value; break;
                    case 2: b = value; break;
                    case 3: a = value; break;
                    default: break;
                }
            }
        }

        public static bool operator ==(RGBA32 left, RGBA32 right)
        {
            return left.r == right.r && left.g == right.g && left.b == right.b && left.a == right.a;
        }

        public static bool operator !=(RGBA32 left, RGBA32 right)
        {
            return !(left == right);
        }

        public static unsafe implicit operator int(RGBA32 v)
        {
            int result;
            byte* bytePtr = (byte*)&result;
            bytePtr[0] = v.r;
            bytePtr[1] = v.g;
            bytePtr[2] = v.b;
            bytePtr[3] = v.a;
            return result;
        }

        public override bool Equals(object obj)
        {
            if (obj is RGBA32)
            {
                RGBA32 other = (RGBA32)obj;
                return this == other;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return r.GetHashCode() ^ g.GetHashCode() ^ b.GetHashCode() ^ a.GetHashCode();
        }
    }
}
