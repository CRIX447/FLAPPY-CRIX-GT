// Small HTTPS helper for the account features (crixgamingvr.com's device link, Firebase sign-in,
// Firestore, PlayFab). Blocking calls: always run on a background thread.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    public struct HttpResult
    {
        public int Status;          // 0 = no answer (network error)
        public string Body;
        public string Error;        // network error text when Status == 0
        public bool Ok => Status >= 200 && Status < 300;
        public Dictionary<string, object> Json
        {
            get { try { return MiniJson.Obj(MiniJson.Parse(Body ?? "")); } catch { return null; } }
        }
        public override string ToString() => Status == 0 ? "no answer (" + Error + ")" : "HTTP " + Status;
    }

    public static class Http
    {
        public const string UserAgent = "FlappyCrixGTMod/1.0 (Windows; Gorilla Tag mod, made with AI)";
        public static int TimeoutMs = 15000;

        static Http()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
        }

        public static HttpResult Send(string method, string url, string body = null, string contentType = "application/json",
                                      Dictionary<string, string> headers = null)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.UserAgent = UserAgent;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.Accept = "application/json";
                if (headers != null) foreach (var kv in headers) req.Headers[kv.Key] = kv.Value;
                if (body != null)
                {
                    byte[] b = Encoding.UTF8.GetBytes(body);
                    req.ContentType = contentType;
                    req.ContentLength = b.Length;
                    using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length);
                }
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return new HttpResult { Status = (int)resp.StatusCode, Body = Read(resp) };
            }
            catch (WebException e)
            {
                var r = e.Response as HttpWebResponse;
                if (r != null) using (r) return new HttpResult { Status = (int)r.StatusCode, Body = Read(r) };
                return new HttpResult { Status = 0, Error = e.Status + ": " + e.Message };
            }
            catch (Exception e)
            {
                return new HttpResult { Status = 0, Error = e.GetType().Name + ": " + e.Message };
            }
        }

        public static HttpResult PostJson(string url, object body, Dictionary<string, string> headers = null) =>
            Send("POST", url, MiniJson.Serialize(body), "application/json", headers);

        private static string Read(HttpWebResponse r)
        {
            using (var s = r.GetResponseStream())
            using (var sr = new StreamReader(s, Encoding.UTF8))
                return sr.ReadToEnd();
        }

        public static string Escape(string s) => Uri.EscapeDataString(s ?? "");
    }
}
