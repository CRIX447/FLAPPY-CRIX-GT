// The picture on the screen while the website is still loading, or if nothing could
// start - so the screen is never invisible or plain black. Drawn with PixelFont into an
// RGBA buffer (no Unity text shaders). No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System.Collections.Generic;

namespace FlappyCrix
{
    public static class StatusPicture
    {
        public const int W = 384, H = 480;

        /// <summary>Draws the status screen into px (W x H, rows top-down).</summary>
        public static void Draw(uint[] px, string title, string body, string footer)
        {
            uint bg = PixelFont.Rgba("#0A0A0F"), orange = PixelFont.Rgba("#FF6B35"),
                 white = PixelFont.Rgba("#FFFFFF"), grey = PixelFont.Rgba("#A8A8C0"), shadow = PixelFont.Rgba("#000000");
            for (int i = 0; i < px.Length; i++) px[i] = bg;

            // orange bars top and bottom, like the site's header
            Fill(px, 0, 0, W, 10, orange);
            Fill(px, 0, H - 10, W, 10, orange);

            PixelFont.DrawCentred(px, W, H, "FLAPPY", W / 2, 70, 7, orange, shadow);
            PixelFont.DrawCentred(px, W, H, "CRIX", W / 2, 130, 7, orange, shadow);

            int y = 230;
            foreach (var line in Wrap(title, 3))
            {
                PixelFont.DrawCentred(px, W, H, line, W / 2, y, 3, white, shadow);
                y += 30;
            }
            y += 12;
            foreach (var line in Wrap(body, 2))
            {
                PixelFont.DrawCentred(px, W, H, line, W / 2, y, 2, grey);
                y += 22;
            }
            int fy = H - 40;
            foreach (var line in Wrap(footer, 2))
            {
                PixelFont.DrawCentred(px, W, H, line, W / 2, fy, 2, grey);
                fy += 20;
            }
        }

        private static void Fill(uint[] px, int x, int y, int w, int h, uint c)
        {
            for (int yy = y; yy < y + h && yy < H; yy++)
                for (int xx = x; xx < x + w && xx < W; xx++)
                    px[yy * W + xx] = c;
        }

        /// <summary>Splits text into lines that fit the picture at the given font scale.</summary>
        public static List<string> Wrap(string text, int scale)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text)) return lines;
            int max = (W - 24) / (PixelFont.Advance * scale);
            foreach (var para in text.ToUpperInvariant().Split(new[] { '\n' }))
            {
                string line = "";
                foreach (var word in para.Split(new[] { ' ' }))
                {
                    string w = word;
                    while (w.Length > max) { if (line.Length > 0) { lines.Add(line); line = ""; } lines.Add(w.Substring(0, max)); w = w.Substring(max); }
                    if (line.Length == 0) line = w;
                    else if (line.Length + 1 + w.Length <= max) line += " " + w;
                    else { lines.Add(line); line = w; }
                }
                if (line.Length > 0) lines.Add(line);
            }
            return lines;
        }
    }
}
