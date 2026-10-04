#!/usr/bin/env python3
"""Checks that every UnityEngine member FlappyCrix.dll calls really exists in Unity.

The DLL can be built without Gorilla Tag installed (tools/refs/build_dll.sh), against
hand-written stand-ins for UnityEngine. If a stand-in ever declared a member differently
from the real Unity, Mono would refuse to run the method that uses it inside the game.
This script disassembles the built DLL (ikdasm), lists every UnityEngine member it
references (type, name, parameter types, field vs property vs method) and looks each one
up in Unity's own C# reference source (github.com/Unity-Technologies/UnityCsReference).

    python3 tools/api-audit/check.py <FlappyCrix.dll> <UnityCsReference checkout>

Run through tools/api-audit/run.sh, which fetches the reference source first.
Part of Flappy Crix for Gorilla Tag - made with AI (Claude by Anthropic).
"""
import glob
import re
import subprocess
import sys

dll, ucs = sys.argv[1], sys.argv[2]

# ---- 1. every UnityEngine member the DLL references -------------------------------------
il = subprocess.run(["ikdasm", dll], capture_output=True, text=True).stdout
lines = []
for l in il.split("\n"):            # ikdasm wraps long signatures onto continuation lines
    if lines and re.match(r"^\s{20,}\S", l) and not re.match(r"^\s*IL_", l) and lines[-1].strip().startswith("IL_"):
        lines[-1] += " " + l.strip()
    else:
        lines.append(l)
members = set()
for l in lines:
    m = re.match(r"\s*IL_\w+:\s+(call|callvirt|newobj|ldsfld|stsfld|ldfld|stfld|ldflda|ldsflda|ldftn)\s+(.*)$", l)
    if m and re.search(r"\[UnityEngine\]UnityEngine[\w.]*::", m.group(2)):
        members.add(re.sub(r"\s+", " ", m.group(2)))
if not members:
    sys.exit("No UnityEngine references found - is ikdasm installed and the DLL path right?")

# ---- 2. Unity's runtime source --------------------------------------------------------
files = [f for f in glob.glob(ucs + "/Runtime/**/*.cs", recursive=True) + glob.glob(ucs + "/Modules/**/*.cs", recursive=True)
         if "/Editor/" not in f and "Tests" not in f]
src = {f: open(f, encoding="utf-8", errors="ignore").read() for f in files}
PRIM = {"float32": "float", "float64": "double", "int32": "int", "uint32": "uint", "int64": "long",
        "uint8": "byte", "bool": "bool", "string": "string", "object": "object"}


def cs(t):
    """IL type name -> the C# spelling used in Unity's source."""
    t = t.strip().replace("valuetype ", "").replace("class ", "")
    t = re.sub(r"\[[^\]]+\]", "", t)
    arr = t.endswith("[]")
    t = t[:-2] if arr else t
    t = t.rstrip("&")
    g = re.match(r"(.*)`1<(.*)>$", t)
    t = g.group(1).split(".")[-1] + "<" + cs(g.group(2)) + ">" if g else PRIM.get(t, t.split(".")[-1].split("/")[-1])
    return t + ("[]" if arr else "")


type_files = {}


def declaring_source(type_name):
    if type_name not in type_files:
        pat = re.compile(r"\b(class|struct|interface)\s+" + re.escape(type_name) + r"\b")
        type_files[type_name] = "\n".join(s for s in src.values() if pat.search(s))
    return type_files[type_name]


OPS = {"op_Equality": "==", "op_Inequality": "!=", "op_Addition": "+", "op_Subtraction": "-",
       "op_Multiply": "*", "op_Division": "/", "op_UnaryNegation": "-"}
ARG = r"(?:\[[^\]]*\]\s*)*(?:this |ref |out |in |params )?{t}\s+\w+(?:\s*=\s*[^,)]+)?"

ok, bad = 0, []
for m in sorted(members):
    mm = re.match(r"(instance\s+)?(.*?)\s*(\S*\[UnityEngine\]UnityEngine[\w.]*?)::([\w.]+)(<[^>]*>)?(\((.*)\))?$", m)
    if not mm:
        bad.append(("UNPARSED", m)); continue
    ret, decl, name, params = mm.group(2), mm.group(3), mm.group(4), mm.group(7)
    type_name = decl.replace("[UnityEngine]", "").split(".")[-1].split("/")[-1]
    body = declaring_source(type_name)
    if not body:
        bad.append(("NO SUCH TYPE", m)); continue
    ps = [cs(x) for x in params.split(",")] if params and params.strip() else []
    if params is None:                                           # field
        found = re.search(r"\b" + re.escape(name) + r"\s*[;=]", body)
    elif name.startswith(("get_", "set_")):                      # property
        found = re.search(r"\b" + re.escape(name[4:]) + r"\s*(\{|=>)", body)
    elif name == ".ctor":
        found = re.search(r"\b" + re.escape(type_name) + r"\s*\(\s*" + r"\s*,\s*".join(ARG.format(t=re.escape(p)) for p in ps) + r"\s*\)", body)
    elif name == "op_Implicit":
        found = re.search(r"implicit\s+operator\s+" + re.escape(cs(ret)) + r"\s*\(\s*" + re.escape(ps[0]) + r"\s", body)
    elif name in OPS:
        found = re.search(r"operator\s*" + re.escape(OPS[name]) + r"\s*\(\s*" + r"\s*,\s*".join(re.escape(p) + r"\s+\w+" for p in ps) + r"\s*\)", body)
    else:                                                        # method (exact parameter list)
        found = re.search(r"\b" + re.escape(name) + r"(?:<[^>]*>)?\s*\(\s*" + r"\s*,\s*".join(ARG.format(t=re.escape(p)) for p in ps) + r"\s*\)", body)
    if found:
        ok += 1
    else:
        bad.append(("NOT IN UNITY", m))

print("%d UnityEngine members referenced, %d found in Unity's source" % (len(members), ok))
for kind, m in bad:
    print("FAIL", kind, "|", m.replace("[UnityEngine]", ""))
sys.exit(1 if bad else 0)
