// Reads the pictures baked into FlappyCrix.dll by tools/make_sprites.py (the site's bird, coin,
// hats, witch and sleigh, and the emoji the menus use), so the in-game version needs no
// files next to the DLL to look right.
// No UnityEngine references (tested outside Unity).
// Part of the Flappy Crix Arcade SDK (from Flappy Crix for Gorilla Tag) - made with AI (Claude by Anthropic).

using System;
using System.IO;
using System.Text;

namespace CrixArcade.Core
{
    public static class SpriteSheet
    {
        public static Assets LoadEmbedded()
        {
            var data = CoreResources.Load("sprites");
            if (data == null) throw new InvalidOperationException("The pictures (Resources/CrixArcade/sprites.bytes) are missing");
            return Read(data);
        }

        public static Assets Read(byte[] file)
        {
            // not compressed: some Unity games strip .NET's DeflateStream
            if (file.Length < 8 || file[0] != 'F' || file[1] != 'C' || file[2] != 'S' || file[3] != '2')
                throw new InvalidOperationException("Bad picture data");
            byte[] d = file;
            var a = new Assets();
            int p = 4;
            int count = BitConverter.ToInt32(d, p); p += 4;
            for (int i = 0; i < count; i++)
            {
                int n = d[p++];
                string name = Encoding.UTF8.GetString(d, p, n); p += n;
                int w = BitConverter.ToUInt16(d, p), h = BitConverter.ToUInt16(d, p + 2); p += 4;
                var px = new uint[w * h];
                Buffer.BlockCopy(d, p, px, 0, w * h * 4); p += w * h * 4;
                a.Add(name, new Sprite(px, w, h));
            }
            return a;
        }
    }
}
