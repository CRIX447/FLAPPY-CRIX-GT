// Dev-only: runs LocalWebServer outside Unity so it can be tested with headless Chromium.
using System;
using System.Threading;
using FlappyCrix.Web;
class Harness {
    static void Main(string[] a) {
        string cfg = a.Length > 2 ? a[2] : "{\"skipIntro\":true,\"disableServiceWorker\":true,\"flapStartsGame\":true}";
        var s = new LocalWebServer(a[0], "flappycrix.html", cfg, Console.WriteLine);
        s.Start(int.Parse(a[1]));
        Console.WriteLine("READY " + s.EntryUrl);
        Thread.Sleep(Timeout.Infinite);
    }
}
