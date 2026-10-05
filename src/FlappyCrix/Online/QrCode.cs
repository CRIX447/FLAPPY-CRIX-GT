// A small QR code encoder (byte mode, error correction level M, versions 1-10) for the account
// link: the game shows https://crixgamingvr.com/link?code=XXX-XXX as a QR code, so a phone can
// open the page with the code already filled in. Written from the QR code standard (ISO/IEC 18004).
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.Text;

namespace FlappyCrix.Online
{
    public sealed class QrCode
    {
        public readonly int Size;
        public readonly int Version;
        private readonly bool[,] dark;
        private readonly bool[,] reserved;

        public bool this[int x, int y] => dark[y, x];

        // level M: ecc codewords per block, then (blocks, data codewords) for group 1 and group 2
        static readonly int[][] Blocks =
        {
            null,
            new[] { 10, 1, 16, 0, 0 }, new[] { 16, 1, 28, 0, 0 }, new[] { 26, 1, 44, 0, 0 }, new[] { 18, 2, 32, 0, 0 },
            new[] { 24, 2, 43, 0, 0 }, new[] { 16, 4, 27, 0, 0 }, new[] { 18, 4, 31, 0, 0 }, new[] { 22, 2, 38, 2, 39 },
            new[] { 22, 3, 36, 2, 37 }, new[] { 26, 4, 43, 1, 44 },
        };
        static readonly int[][] Align =
        {
            null, new int[0], new[] { 6, 18 }, new[] { 6, 22 }, new[] { 6, 26 }, new[] { 6, 30 }, new[] { 6, 34 },
            new[] { 6, 22, 38 }, new[] { 6, 24, 42 }, new[] { 6, 26, 46 }, new[] { 6, 28, 50 },
        };

        public static QrCode Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            for (int v = 1; v <= 10; v++)
            {
                var b = Blocks[v];
                int dataCw = b[1] * b[2] + b[3] * b[4];
                int countBits = v < 10 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= dataCw * 8) return new QrCode(v, data, countBits, dataCw);
            }
            throw new ArgumentException("Too long for a QR code here");
        }

        private QrCode(int version, byte[] data, int countBits, int dataCw)
        {
            Version = version;
            Size = 17 + 4 * version;
            dark = new bool[Size, Size];
            reserved = new bool[Size, Size];

            // ---- data bits
            var bits = new List<bool>();
            Action<int, int> put = (val, n) => { for (int i = n - 1; i >= 0; i--) bits.Add(((val >> i) & 1) != 0); };
            put(4, 4);
            put(data.Length, countBits);
            foreach (byte x in data) put(x, 8);
            int cap = dataCw * 8;
            put(0, Math.Min(4, cap - bits.Count));
            while (bits.Count % 8 != 0) bits.Add(false);
            for (int pad = 0; bits.Count < cap; pad ^= 1) put(pad == 0 ? 0xEC : 0x11, 8);
            var cw = new byte[dataCw];
            for (int i = 0; i < cap; i++) if (bits[i]) cw[i >> 3] |= (byte)(0x80 >> (i & 7));

            // ---- blocks + Reed-Solomon, interleaved
            var b = Blocks[version];
            int ecc = b[0];
            var dataBlocks = new List<byte[]>();
            var eccBlocks = new List<byte[]>();
            int pos = 0;
            for (int g = 0; g < 2; g++)
                for (int k = 0; k < b[1 + g * 2]; k++)
                {
                    int n = b[2 + g * 2];
                    var blk = new byte[n];
                    Array.Copy(cw, pos, blk, 0, n); pos += n;
                    dataBlocks.Add(blk);
                    eccBlocks.Add(ReedSolomon(blk, ecc));
                }
            var all = new List<byte>();
            int maxData = 0; foreach (var d in dataBlocks) maxData = Math.Max(maxData, d.Length);
            for (int i = 0; i < maxData; i++) foreach (var d in dataBlocks) if (i < d.Length) all.Add(d[i]);
            for (int i = 0; i < ecc; i++) foreach (var e in eccBlocks) all.Add(e[i]);

            // ---- function patterns
            Finder(0, 0); Finder(Size - 7, 0); Finder(0, Size - 7);
            for (int i = 8; i < Size - 8; i++) { Set(i, 6, i % 2 == 0, true); Set(6, i, i % 2 == 0, true); }
            var al = Align[version];
            foreach (int ay in al)
                foreach (int ax in al)
                {
                    if ((ax == 6 && ay == 6) || (ax == 6 && ay == al[al.Length - 1]) || (ax == al[al.Length - 1] && ay == 6)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(ax + dx, ay + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1, true);
                }
            // format and version areas (filled in after masking), dark module
            for (int i = 0; i < 9; i++) { Reserve(8, i); Reserve(i, 8); }
            for (int i = 0; i < 8; i++) { Reserve(Size - 1 - i, 8); Reserve(8, Size - 1 - i); }
            Set(8, Size - 8, true, true);
            if (version >= 7)
                for (int i = 0; i < 6; i++) for (int j = 0; j < 3; j++) { Reserve(Size - 11 + j, i); Reserve(i, Size - 11 + j); }

            // ---- data, zig-zag from the bottom right
            int bit = 0, total = all.Count * 8;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vert = 0; vert < Size; vert++)
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vert : vert;
                        if (reserved[y, x]) continue;
                        dark[y, x] = bit < total && ((all[bit >> 3] >> (7 - (bit & 7))) & 1) != 0;
                        bit++;
                    }
            }

            // ---- pick the mask with the lowest penalty
            int best = 0, bestScore = int.MaxValue;
            for (int m = 0; m < 8; m++)
            {
                ApplyMask(m); DrawFormat(m);
                int score = Penalty();
                if (score < bestScore) { bestScore = score; best = m; }
                ApplyMask(m);   // undo (xor)
            }
            ApplyMask(best); DrawFormat(best);
            if (version >= 7) DrawVersion();
        }

        private void Set(int x, int y, bool on, bool reserve) { dark[y, x] = on; if (reserve) reserved[y, x] = true; }
        private void Reserve(int x, int y) => reserved[y, x] = true;

        private void Finder(int x0, int y0)
        {
            for (int dy = -1; dy <= 7; dy++)
                for (int dx = -1; dx <= 7; dx++)
                {
                    int x = x0 + dx, y = y0 + dy;
                    if (x < 0 || y < 0 || x >= Size || y >= Size) continue;
                    int d = Math.Max(Math.Abs(dx - 3), Math.Abs(dy - 3));
                    Set(x, y, d != 2 && d != 4, true);
                }
        }

        private static bool MaskBit(int m, int x, int y)
        {
            switch (m)
            {
                case 0: return (x + y) % 2 == 0;
                case 1: return y % 2 == 0;
                case 2: return x % 3 == 0;
                case 3: return (x + y) % 3 == 0;
                case 4: return (x / 3 + y / 2) % 2 == 0;
                case 5: return x * y % 2 + x * y % 3 == 0;
                case 6: return (x * y % 2 + x * y % 3) % 2 == 0;
                default: return ((x + y) % 2 + x * y % 3) % 2 == 0;
            }
        }

        private void ApplyMask(int m)
        {
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (!reserved[y, x] && MaskBit(m, x, y)) dark[y, x] = !dark[y, x];
        }

        private void DrawFormat(int mask)
        {
            int data = (0 << 3) | mask;          // level M = 00
            int rem = data;
            for (int i = 0; i < 10; i++) rem = (rem << 1) ^ ((rem >> 9) * 0x537);
            int bits = ((data << 10) | rem) ^ 0x5412;
            Func<int, bool> B = i => ((bits >> i) & 1) != 0;
            for (int i = 0; i <= 5; i++) dark[i, 8] = B(i);
            dark[7, 8] = B(6); dark[8, 8] = B(7); dark[8, 7] = B(8);
            for (int i = 9; i < 15; i++) dark[8, 14 - i] = B(i);
            for (int i = 0; i < 8; i++) dark[8, Size - 1 - i] = B(i);
            for (int i = 8; i < 15; i++) dark[Size - 15 + i, 8] = B(i);
            dark[Size - 8, 8] = true;
        }

        private void DrawVersion()
        {
            int rem = Version;
            for (int i = 0; i < 12; i++) rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            int bits = (Version << 12) | rem;
            for (int i = 0; i < 18; i++)
            {
                bool on = ((bits >> i) & 1) != 0;
                int a = Size - 11 + i % 3, b = i / 3;
                dark[b, a] = on; dark[a, b] = on;
            }
        }

        private int Penalty()
        {
            int score = 0;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < Size; i++)
                {
                    int run = 1;
                    for (int j = 1; j < Size; j++)
                    {
                        bool a = pass == 0 ? dark[i, j] : dark[j, i], p = pass == 0 ? dark[i, j - 1] : dark[j - 1, i];
                        if (a == p) { run++; if (run == 5) score += 3; else if (run > 5) score++; }
                        else run = 1;
                    }
                }
            for (int y = 0; y < Size - 1; y++)
                for (int x = 0; x < Size - 1; x++)
                {
                    bool c = dark[y, x];
                    if (c == dark[y, x + 1] && c == dark[y + 1, x] && c == dark[y + 1, x + 1]) score += 3;
                }
            int darkCount = 0;
            foreach (bool d in dark) if (d) darkCount++;
            int k = Math.Abs(darkCount * 20 - Size * Size * 10) / (Size * Size);
            return score + k * 10;
        }

        // GF(256) with x^8 + x^4 + x^3 + x^2 + 1
        static readonly byte[] Exp = new byte[512], Log = new byte[256];
        static QrCode()
        {
            int v = 1;
            for (int i = 0; i < 255; i++) { Exp[i] = (byte)v; Log[v] = (byte)i; v <<= 1; if (v >= 256) v ^= 0x11D; }
            for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
        }
        static byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];

        private static byte[] ReedSolomon(byte[] data, int n)
        {
            // generator polynomial (x - a^0)(x - a^1)...(x - a^(n-1)), coefficients highest first (leading 1 dropped)
            var gen = new byte[n];
            gen[n - 1] = 1;
            byte root = 1;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    gen[j] = Mul(gen[j], root);
                    if (j + 1 < n) gen[j] ^= gen[j + 1];
                }
                root = Mul(root, 2);
            }
            var res = new byte[n];
            foreach (byte d in data)
            {
                byte factor = (byte)(d ^ res[0]);
                Array.Copy(res, 1, res, 0, n - 1);
                res[n - 1] = 0;
                for (int i = 0; i < n; i++) res[i] ^= Mul(gen[i], factor);
            }
            return res;
        }
    }
}
