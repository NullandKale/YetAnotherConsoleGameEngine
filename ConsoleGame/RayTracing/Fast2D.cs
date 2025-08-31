using System.Runtime.CompilerServices;

namespace ConsoleGame.RayTracing
{
    public sealed class Fast2D<T>
    {
        public readonly int Width;
        public readonly int Height;
        public readonly T[] Buffer;

        public Fast2D(int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            Width = width;
            Height = height;
            Buffer = new T[width * height];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Index(int x, int y)
        {
            return x + y * Width;
        }

        public ref T this[int x, int y]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return ref Buffer[Index(x, y)];
            }
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return Buffer.Length;
            }
        }
    }
}
