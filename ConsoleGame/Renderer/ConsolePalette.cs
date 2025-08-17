using System;
using ConsoleGame.RayTracing;

namespace ConsoleRayTracing
{
    public static class MeshSwatches
    {
        // Exact 16-color console sRGB anchors (match ConsolePalette order):
        // Black, DarkBlue, DarkGreen, DarkCyan, DarkRed, DarkMagenta, DarkYellow, Gray,
        // DarkGray, Blue, Green, Cyan, Red, Magenta, Yellow, White
        // (Kept here locally so scenes can pick deterministic swatches without peeking into ConsolePalette internals.)
        private static readonly Vec3[] Palette16 = new Vec3[]
        {
            new Vec3(0.00f,0.00f,0.00f),  // Black
            new Vec3(0.00f,0.00f,0.50f),  // DarkBlue
            new Vec3(0.00f,0.50f,0.00f),  // DarkGreen
            new Vec3(0.00f,0.50f,0.50f),  // DarkCyan
            new Vec3(0.50f,0.00f,0.00f),  // DarkRed
            new Vec3(0.50f,0.00f,0.50f),  // DarkMagenta
            new Vec3(0.50f,0.50f,0.00f),  // DarkYellow
            new Vec3(0.75f,0.75f,0.75f),  // Gray
            new Vec3(0.50f,0.50f,0.50f),  // DarkGray
            new Vec3(0.00f,0.00f,1.00f),  // Blue
            new Vec3(0.00f,1.00f,0.00f),  // Green
            new Vec3(0.00f,1.00f,1.00f),  // Cyan
            new Vec3(1.00f,0.00f,0.00f),  // Red
            new Vec3(1.00f,0.00f,1.00f),  // Magenta
            new Vec3(1.00f,1.00f,0.00f),  // Yellow
            new Vec3(1.00f,1.00f,1.00f)   // White
        };

        private static Vec3 FromConsole(ConsoleColor c)
        {
            int idx = (int)c;
            if (idx < 0 || idx >= Palette16.Length) return Palette16[0];
            return Palette16[idx];
        }

        private static Vec3 Scale(ConsoleColor c, float k)
        {
            if (k < 0.0f) k = 0.0f;
            if (k > 1.0f) k = 1.0f;
            Vec3 v = FromConsole(c);
            return new Vec3(v.X * k, v.Y * k, v.Z * k);
        }

        // ---------- Neutrals (great for bones, clay, porcelain, etc.) ----------
        public static readonly Vec3 Black = FromConsole(ConsoleColor.Black);
        public static readonly Vec3 Charcoal = FromConsole(ConsoleColor.DarkGray);
        public static readonly Vec3 Stone = FromConsole(ConsoleColor.Gray);
        public static readonly Vec3 WhiteSoft = Scale(ConsoleColor.White, 0.85f);   // avoids harsh clipping
        public static readonly Vec3 White = FromConsole(ConsoleColor.White);

        // ---------- Metals / warm materials ----------
        public static readonly Vec3 Gold = Scale(ConsoleColor.Yellow, 0.90f);
        public static readonly Vec3 Brass = Scale(ConsoleColor.DarkYellow, 1.00f);
        public static readonly Vec3 Copper = new Vec3(0.80f, 0.45f, 0.25f);      // will quantize near DarkYellow/Red

        // ---------- Gem-ish primaries (bright but softened a bit) ----------
        public static readonly Vec3 Ruby = Scale(ConsoleColor.Red, 0.92f);
        public static readonly Vec3 Emerald = Scale(ConsoleColor.Green, 0.85f);
        public static readonly Vec3 Sapphire = Scale(ConsoleColor.Blue, 0.85f);
        public static readonly Vec3 Amethyst = Scale(ConsoleColor.Magenta, 0.88f);
        public static readonly Vec3 CyanSoft = Scale(ConsoleColor.Cyan, 0.85f);
        public static readonly Vec3 Jade = Scale(ConsoleColor.DarkCyan, 1.00f);

        // ---------- Helpful dark accents ----------
        public static readonly Vec3 OxideRed = FromConsole(ConsoleColor.DarkRed);
        public static readonly Vec3 PineGreen = FromConsole(ConsoleColor.DarkGreen);
        public static readonly Vec3 Navy = FromConsole(ConsoleColor.DarkBlue);
        public static readonly Vec3 Plum = FromConsole(ConsoleColor.DarkMagenta);

        // ---------- Console-aligned primaries (exact anchors) ----------
        public static readonly Vec3 Red = FromConsole(ConsoleColor.Red);
        public static readonly Vec3 Green = FromConsole(ConsoleColor.Green);
        public static readonly Vec3 Blue = FromConsole(ConsoleColor.Blue);
        public static readonly Vec3 Magenta = FromConsole(ConsoleColor.Magenta);
        public static readonly Vec3 Yellow = FromConsole(ConsoleColor.Yellow);
        public static readonly Vec3 Cyan = FromConsole(ConsoleColor.Cyan);

        // ---------- Material helpers ----------
        public static Material Matte(Vec3 albedo, double specular = 0.10, double reflectivity = 0.00)
        {
            return new Material(albedo, specular, reflectivity, Vec3.Zero);
        }

        public static Material Mirror(Vec3 tint, double reflectivity = 0.85)
        {
            return new Material(tint, 0.0, reflectivity, Vec3.Zero);
        }

        public static Material Emissive(Vec3 emission)
        {
            return new Material(new Vec3(0.0f, 0.0f, 0.0f), 0.0, 0.0, emission);
        }
    }


    public static class ConsolePalette
    {
        private static readonly ConsoleColor[] Colors = new ConsoleColor[]
        {
            ConsoleColor.Black, ConsoleColor.DarkBlue, ConsoleColor.DarkGreen, ConsoleColor.DarkCyan, ConsoleColor.DarkRed, ConsoleColor.DarkMagenta, ConsoleColor.DarkYellow, ConsoleColor.Gray, ConsoleColor.DarkGray, ConsoleColor.Blue, ConsoleColor.Green, ConsoleColor.Cyan, ConsoleColor.Red, ConsoleColor.Magenta, ConsoleColor.Yellow, ConsoleColor.White
        };

        private static readonly Vec3[] SRGB = new Vec3[]
        {
            new Vec3(0.0f,0.0f,0.0f),   // Black
            new Vec3(0.0f,0.0f,0.5f),   // DarkBlue
            new Vec3(0.0f,0.5f,0.0f),   // DarkGreen
            new Vec3(0.0f,0.5f,0.5f),   // DarkCyan
            new Vec3(0.5f,0.0f,0.0f),   // DarkRed
            new Vec3(0.5f,0.0f,0.5f),   // DarkMagenta
            new Vec3(0.5f,0.5f,0.0f),   // DarkYellow
            new Vec3(0.75f,0.75f,0.75f),// Gray
            new Vec3(0.5f,0.5f,0.5f),   // DarkGray
            new Vec3(0.0f,0.0f,1.0f),   // Blue
            new Vec3(0.0f,1.0f,0.0f),   // Green
            new Vec3(0.0f,1.0f,1.0f),   // Cyan
            new Vec3(1.0f,0.0f,0.0f),   // Red
            new Vec3(1.0f,0.0f,1.0f),   // Magenta
            new Vec3(1.0f,1.0f,0.0f),   // Yellow
            new Vec3(1.0f,1.0f,1.0f)    // White
        };

        // Precomputed CIE L*a*b* for the 16-color palette (D65), in double precision.
        private static readonly double[,] PaletteLab;
        private static readonly int[] GrayIndices = new int[] { 0, 8, 7, 15 }; // Black, DarkGray, Gray, White

        // Tunables: small gating to keep near-neutrals from snapping to tinted anchors; ΔE00 is used for ranking.
        private const double ChromaNeutralGate = 2.5;   // if input C*ab < gate, only compare against gray ramp
        private const double GrayPenaltyForChromatic = 0.0; // extra ΔE added to gray candidates for chromatic inputs; keep 0 to rely purely on ΔE00

        static ConsolePalette()
        {
            PaletteLab = new double[SRGB.Length, 3];
            for (int i = 0; i < SRGB.Length; i++)
            {
                ToLabD65(SRGB[i], out double L, out double a, out double b);
                PaletteLab[i, 0] = L;
                PaletteLab[i, 1] = a;
                PaletteLab[i, 2] = b;
            }
        }

        // Input c is expected in sRGB [0,1] (already tone-mapped + gamma).
        public static ConsoleColor NearestColor(Vec3 c)
        {
            double r = Clamp01(c.X);
            double g = Clamp01(c.Y);
            double b = Clamp01(c.Z);

            ToLabD65(new Vec3((float)r, (float)g, (float)b), out double L, out double A, out double B);
            double Cab = Math.Sqrt(A * A + B * B);

            int bestIdx = 0;
            double bestDE = double.MaxValue;

            if (Cab < ChromaNeutralGate)
            {
                for (int gi = 0; gi < GrayIndices.Length; gi++)
                {
                    int idx = GrayIndices[gi];
                    double dE = DeltaE00(L, A, B, PaletteLab[idx, 0], PaletteLab[idx, 1], PaletteLab[idx, 2]);
                    if (dE < bestDE)
                    {
                        bestDE = dE;
                        bestIdx = idx;
                    }
                }
                return Colors[bestIdx];
            }
            else
            {
                for (int i = 0; i < SRGB.Length; i++)
                {
                    bool grayCandidate = IsGrayIndex(i);
                    double dE = DeltaE00(L, A, B, PaletteLab[i, 0], PaletteLab[i, 1], PaletteLab[i, 2]);
                    if (grayCandidate)
                    {
                        dE += GrayPenaltyForChromatic;
                    }
                    if (dE < bestDE)
                    {
                        bestDE = dE;
                        bestIdx = i;
                    }
                }
                return Colors[bestIdx];
            }
        }

        // -------- CIEDE2000 ΔE implementation (Sharma et al.) --------
        private static double DeltaE00(double L1, double a1, double b1, double L2, double a2, double b2)
        {
            const double kL = 1.0;
            const double kC = 1.0;
            const double kH = 1.0;

            double C1 = Math.Sqrt(a1 * a1 + b1 * b1);
            double C2 = Math.Sqrt(a2 * a2 + b2 * b2);
            double Cbar = 0.5 * (C1 + C2);
            double Cbar7 = Math.Pow(Cbar, 7.0);
            double G = 0.5 * (1.0 - Math.Sqrt(Cbar7 / (Cbar7 + Math.Pow(25.0, 7.0))));

            double a1p = (1.0 + G) * a1;
            double a2p = (1.0 + G) * a2;
            double C1p = Math.Sqrt(a1p * a1p + b1 * b1);
            double C2p = Math.Sqrt(a2p * a2p + b2 * b2);

            double h1p = Math.Atan2(b1, a1p);
            if (h1p < 0.0) h1p += 2.0 * Math.PI;
            double h2p = Math.Atan2(b2, a2p);
            if (h2p < 0.0) h2p += 2.0 * Math.PI;

            double dLp = L2 - L1;
            double dCp = C2p - C1p;

            double dhp;
            double dh = h2p - h1p;
            if (C1p * C2p == 0.0)
            {
                dhp = 0.0;
            }
            else
            {
                if (Math.Abs(dh) <= Math.PI)
                {
                    dhp = dh;
                }
                else
                {
                    if (dh > 0.0) dhp = dh - 2.0 * Math.PI;
                    else dhp = dh + 2.0 * Math.PI;
                }
            }

            double dHp = 2.0 * Math.Sqrt(C1p * C2p) * Math.Sin(dhp * 0.5);

            double Lbarp = 0.5 * (L1 + L2);
            double Cbarp = 0.5 * (C1p + C2p);

            double hbarp;
            if (C1p * C2p == 0.0)
            {
                hbarp = h1p + h2p;
            }
            else
            {
                if (Math.Abs(h1p - h2p) <= Math.PI)
                {
                    hbarp = 0.5 * (h1p + h2p);
                }
                else
                {
                    if (h1p + h2p < 2.0 * Math.PI) hbarp = 0.5 * (h1p + h2p + 2.0 * Math.PI);
                    else hbarp = 0.5 * (h1p + h2p - 2.0 * Math.PI);
                }
            }

            double T = 1.0
                       - 0.17 * Math.Cos(hbarp - DegToRad(30.0))
                       + 0.24 * Math.Cos(2.0 * hbarp)
                       + 0.32 * Math.Cos(3.0 * hbarp + DegToRad(6.0))
                       - 0.20 * Math.Cos(4.0 * hbarp - DegToRad(63.0));

            double SL = 1.0 + (0.015 * Math.Pow(Lbarp - 50.0, 2.0)) / Math.Sqrt(20.0 + Math.Pow(Lbarp - 50.0, 2.0));
            double SC = 1.0 + 0.045 * Cbarp;
            double SH = 1.0 + 0.015 * Cbarp * T;

            double deltaTheta = DegToRad(30.0) * Math.Exp(-Math.Pow((RadToDeg(hbarp) - 275.0) / 25.0, 2.0));
            double RC = 2.0 * Math.Sqrt(Math.Pow(Cbarp, 7.0) / (Math.Pow(Cbarp, 7.0) + Math.Pow(25.0, 7.0)));
            double RT = -Math.Sin(2.0 * deltaTheta) * RC;

            double dE = Math.Sqrt(
                Math.Pow(dLp / (kL * SL), 2.0) +
                Math.Pow(dCp / (kC * SC), 2.0) +
                Math.Pow(dHp / (kH * SH), 2.0) +
                RT * (dCp / (kC * SC)) * (dHp / (kH * SH))
            );

            return dE;
        }

        // -------- sRGB (gamma) -> XYZ (D65) -> Lab (D65) --------
        private static void ToLabD65(Vec3 srgb, out double L, out double a, out double b)
        {
            double R = SRGBToLinear(srgb.X);
            double G = SRGBToLinear(srgb.Y);
            double B = SRGBToLinear(srgb.Z);

            double X = 0.4124564 * R + 0.3575761 * G + 0.1804375 * B;
            double Y = 0.2126729 * R + 0.7151522 * G + 0.0721750 * B;
            double Z = 0.0193339 * R + 0.1191920 * G + 0.9503041 * B;

            const double Xn = 0.95047;  // D65
            const double Yn = 1.00000;
            const double Zn = 1.08883;

            double fx = LabPivot(X / Xn);
            double fy = LabPivot(Y / Yn);
            double fz = LabPivot(Z / Zn);

            L = 116.0 * fy - 16.0;
            a = 500.0 * (fx - fy);
            b = 200.0 * (fy - fz);
        }

        private static double LabPivot(double t)
        {
            const double e = 216.0 / 24389.0; // (6/29)^3
            const double k = 24389.0 / 27.0;  // (29/3)^3
            if (t > e) return Math.Pow(t, 1.0 / 3.0);
            return (k * t + 16.0) / 116.0;
        }

        private static double SRGBToLinear(double c)
        {
            if (c <= 0.04045) return c / 12.92;
            return Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static bool IsGrayIndex(int idx)
        {
            for (int i = 0; i < GrayIndices.Length; i++)
            {
                if (GrayIndices[i] == idx) return true;
            }
            return false;
        }

        private static double Clamp01(double v)
        {
            if (v < 0.0) return 0.0;
            if (v > 1.0) return 1.0;
            return v;
        }

        private static double DegToRad(double d)
        {
            return d * (Math.PI / 180.0);
        }

        private static double RadToDeg(double r)
        {
            return r * (180.0 / Math.PI);
        }
    }
}
