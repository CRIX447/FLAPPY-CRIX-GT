// A small 5x7 pixel font, drawn straight into RGBA pixel buffers. Used for the
// native game's text and the arcade deck's button labels, so neither depends on
// Unity's font/text shaders (those draw on top of everything in Gorilla Tag).
// No UnityEngine references.
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System.Collections.Generic;

namespace FlappyCrix
{
    public static class PixelFont
    {
        public const int GlyphW = 5, GlyphH = 7, Advance = 6;

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { " ### ", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
            ['B'] = new[] { "#### ", "#   #", "#   #", "#### ", "#   #", "#   #", "#### " },
            ['C'] = new[] { " ### ", "#   #", "#    ", "#    ", "#    ", "#   #", " ### " },
            ['D'] = new[] { "#### ", "#   #", "#   #", "#   #", "#   #", "#   #", "#### " },
            ['E'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#####" },
            ['F'] = new[] { "#####", "#    ", "#    ", "#### ", "#    ", "#    ", "#    " },
            ['G'] = new[] { " ### ", "#   #", "#    ", "# ###", "#   #", "#   #", " ####" },
            ['H'] = new[] { "#   #", "#   #", "#   #", "#####", "#   #", "#   #", "#   #" },
            ['I'] = new[] { " ### ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
            ['J'] = new[] { "  ###", "   # ", "   # ", "   # ", "   # ", "#  # ", " ##  " },
            ['K'] = new[] { "#   #", "#  # ", "# #  ", "##   ", "# #  ", "#  # ", "#   #" },
            ['L'] = new[] { "#    ", "#    ", "#    ", "#    ", "#    ", "#    ", "#####" },
            ['M'] = new[] { "#   #", "## ##", "# # #", "# # #", "#   #", "#   #", "#   #" },
            ['N'] = new[] { "#   #", "#   #", "##  #", "# # #", "#  ##", "#   #", "#   #" },
            ['O'] = new[] { " ### ", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
            ['P'] = new[] { "#### ", "#   #", "#   #", "#### ", "#    ", "#    ", "#    " },
            ['Q'] = new[] { " ### ", "#   #", "#   #", "#   #", "# # #", "#  # ", " ## #" },
            ['R'] = new[] { "#### ", "#   #", "#   #", "#### ", "# #  ", "#  # ", "#   #" },
            ['S'] = new[] { " ####", "#    ", "#    ", " ### ", "    #", "    #", "#### " },
            ['T'] = new[] { "#####", "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "  #  " },
            ['U'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", "#   #", " ### " },
            ['V'] = new[] { "#   #", "#   #", "#   #", "#   #", "#   #", " # # ", "  #  " },
            ['W'] = new[] { "#   #", "#   #", "#   #", "# # #", "# # #", "# # #", " # # " },
            ['X'] = new[] { "#   #", "#   #", " # # ", "  #  ", " # # ", "#   #", "#   #" },
            ['Y'] = new[] { "#   #", "#   #", " # # ", "  #  ", "  #  ", "  #  ", "  #  " },
            ['Z'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", "#    ", "#####" },
            ['0'] = new[] { " ### ", "#   #", "#  ##", "# # #", "##  #", "#   #", " ### " },
            ['1'] = new[] { "  #  ", " ##  ", "  #  ", "  #  ", "  #  ", "  #  ", " ### " },
            ['2'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", " #   ", "#####" },
            ['3'] = new[] { "#####", "   # ", "  #  ", "   # ", "    #", "#   #", " ### " },
            ['4'] = new[] { "   # ", "  ## ", " # # ", "#  # ", "#####", "   # ", "   # " },
            ['5'] = new[] { "#####", "#    ", "#### ", "    #", "    #", "#   #", " ### " },
            ['6'] = new[] { "  ## ", " #   ", "#    ", "#### ", "#   #", "#   #", " ### " },
            ['7'] = new[] { "#####", "    #", "   # ", "  #  ", " #   ", " #   ", " #   " },
            ['8'] = new[] { " ### ", "#   #", "#   #", " ### ", "#   #", "#   #", " ### " },
            ['9'] = new[] { " ### ", "#   #", "#   #", " ####", "    #", "   # ", " ##  " },
            ['!'] = new[] { "  #  ", "  #  ", "  #  ", "  #  ", "  #  ", "     ", "  #  " },
            ['?'] = new[] { " ### ", "#   #", "    #", "   # ", "  #  ", "     ", "  #  " },
            ['.'] = new[] { "     ", "     ", "     ", "     ", "     ", " ##  ", " ##  " },
            [','] = new[] { "     ", "     ", "     ", "     ", " ##  ", "  #  ", " #   " },
            [':'] = new[] { "     ", " ##  ", " ##  ", "     ", " ##  ", " ##  ", "     " },
            ['-'] = new[] { "     ", "     ", "     ", "#####", "     ", "     ", "     " },
            ['+'] = new[] { "     ", "  #  ", "  #  ", "#####", "  #  ", "  #  ", "     " },
            ['/'] = new[] { "    #", "    #", "   # ", "  #  ", " #   ", "#    ", "#    " },
            ['('] = new[] { "   # ", "  #  ", " #   ", " #   ", " #   ", "  #  ", "   # " },
            [')'] = new[] { " #   ", "  #  ", "   # ", "   # ", "   # ", "  #  ", " #   " },
            ['\''] = new[] { "  #  ", "  #  ", " #   ", "     ", "     ", "     ", "     " },
            ['>'] = new[] { " #   ", "  #  ", "   # ", "    #", "   # ", "  #  ", " #   " },
            ['<'] = new[] { "   # ", "  #  ", " #   ", "#    ", " #   ", "  #  ", "   # " },
            ['='] = new[] { "     ", "     ", "#####", "     ", "#####", "     ", "     " },
            [' '] = new[] { "     ", "     ", "     ", "     ", "     ", "     ", "     " },
        };

        /// <summary>Width in pixels of a line of text at the given scale.</summary>
        public static int Measure(string text, int scale) =>
            string.IsNullOrEmpty(text) ? 0 : (text.Length * Advance - 1) * scale;

        /// <summary>
        /// Draws text into an RGBA32 buffer (one uint per pixel, 0xAABBGGRR, rows top-down).
        /// shadow != 0 draws a 1-step dark outline first, for readability over pictures.
        /// </summary>
        public static void Draw(uint[] buf, int bufW, int bufH, string text, int x, int y, int scale, uint colour, uint shadow = 0)
        {
            if (string.IsNullOrEmpty(text)) return;
            text = text.ToUpperInvariant();
            if (shadow != 0)
            {
                for (int oy = -1; oy <= 1; oy++)
                    for (int ox = -1; ox <= 1; ox++)
                        if (ox != 0 || oy != 0) DrawRaw(buf, bufW, bufH, text, x + ox * scale, y + oy * scale, scale, shadow);
            }
            DrawRaw(buf, bufW, bufH, text, x, y, scale, colour);
        }

        public static void DrawCentred(uint[] buf, int bufW, int bufH, string text, int cx, int y, int scale, uint colour, uint shadow = 0)
        {
            Draw(buf, bufW, bufH, text, cx - Measure(text, scale) / 2, y, scale, colour, shadow);
        }

        private static void DrawRaw(uint[] buf, int bufW, int bufH, string text, int x, int y, int scale, uint colour)
        {
            int cx = x;
            foreach (char ch in text)
            {
                string[] g;
                if (!Glyphs.TryGetValue(ch, out g)) g = Glyphs[' '];
                for (int gy = 0; gy < GlyphH; gy++)
                {
                    string row = g[gy];
                    for (int gx = 0; gx < GlyphW; gx++)
                    {
                        if (row[gx] != '#') continue;
                        int px0 = cx + gx * scale, py0 = y + gy * scale;
                        for (int sy = 0; sy < scale; sy++)
                        {
                            int py = py0 + sy;
                            if (py < 0 || py >= bufH) continue;
                            int rowStart = py * bufW;
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int px = px0 + sx;
                                if (px >= 0 && px < bufW) buf[rowStart + px] = colour;
                            }
                        }
                    }
                }
                cx += Advance * scale;
            }
        }

        /// <summary>0xAABBGGRR from a #RRGGBB string (Unity RGBA32 byte order on little-endian).</summary>
        public static uint Rgba(string hex, byte a = 255)
        {
            hex = hex.TrimStart('#');
            byte r = System.Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = System.Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = System.Convert.ToByte(hex.Substring(4, 2), 16);
            return Pack(r, g, b, a);
        }

        public static uint Pack(byte r, byte g, byte b, byte a) => (uint)(r | (g << 8) | (b << 16) | (a << 24));
    }
}
