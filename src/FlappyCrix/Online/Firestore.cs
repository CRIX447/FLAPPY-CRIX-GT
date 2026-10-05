// Firestore's REST value format ({"integerValue":"5"}, {"mapValue":{"fields":{...}}} ...) to and
// from plain values: long, double, bool, string, null, List<object>, Dictionary<string, object>.
// No UnityEngine references (tested outside Unity).
// Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using FlappyCrix.Web.Cdp;

namespace FlappyCrix.Online
{
    public static class Firestore
    {
        public static Dictionary<string, object> Value(object v)
        {
            if (v == null) return MiniJson.Args("nullValue", null);
            if (v is bool) return MiniJson.Args("booleanValue", v);
            if (v is int || v is long || v is short || v is byte || v is uint)
                return MiniJson.Args("integerValue", Convert.ToInt64(v, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture));
            if (v is float || v is double) return MiniJson.Args("doubleValue", Convert.ToDouble(v, CultureInfo.InvariantCulture));
            var s = v as string;
            if (s != null) return MiniJson.Args("stringValue", s);
            var d = v as IDictionary<string, object>;
            if (d != null) return MiniJson.Args("mapValue", MiniJson.Args("fields", Fields(d)));
            var e = v as IEnumerable;
            if (e != null)
            {
                var list = new List<object>();
                foreach (var x in e) list.Add(Value(x));
                return MiniJson.Args("arrayValue", MiniJson.Args("values", list));
            }
            return MiniJson.Args("stringValue", v.ToString());
        }

        public static Dictionary<string, object> Fields(IDictionary<string, object> plain)
        {
            var f = new Dictionary<string, object>();
            foreach (var kv in plain) f[kv.Key] = Value(kv.Value);
            return f;
        }

        /// <summary>A Firestore value back to a plain value (integers come back as long).</summary>
        public static object Plain(object fsValue)
        {
            var v = MiniJson.Obj(fsValue);
            if (v == null) return null;
            object x;
            if (v.TryGetValue("integerValue", out x))
            {
                long l;
                if (x is string && long.TryParse((string)x, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) return l;
                if (x is double) return (long)(double)x;
                return 0L;
            }
            if (v.TryGetValue("doubleValue", out x)) return x is double ? (double)x : 0.0;
            if (v.TryGetValue("booleanValue", out x)) return x is bool && (bool)x;
            if (v.TryGetValue("stringValue", out x)) return x as string;
            if (v.TryGetValue("timestampValue", out x)) return x as string;
            if (v.ContainsKey("nullValue")) return null;
            if (v.TryGetValue("mapValue", out x))
                return PlainFields(MiniJson.Child(MiniJson.Obj(x), "fields"));
            if (v.TryGetValue("arrayValue", out x))
            {
                var list = new List<object>();
                object vals;
                var a = MiniJson.Obj(x);
                if (a != null && a.TryGetValue("values", out vals) && vals is List<object>)
                    foreach (var item in (List<object>)vals) list.Add(Plain(item));
                return list;
            }
            return null;
        }

        public static Dictionary<string, object> PlainFields(Dictionary<string, object> fields)
        {
            var d = new Dictionary<string, object>();
            if (fields != null) foreach (var kv in fields) d[kv.Key] = Plain(kv.Value);
            return d;
        }

        // helpers for reading plain documents
        public static long Long(Dictionary<string, object> d, string k, long fallback = 0)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return fallback;
            if (v is long) return (long)v;
            if (v is double) return (long)(double)v;
            return fallback;
        }

        public static string Str(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) ? v as string : null;
        }

        public static Dictionary<string, object> Map(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) ? v as Dictionary<string, object> : null;
        }

        public static List<object> List(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) ? v as List<object> : null;
        }
    }
}
