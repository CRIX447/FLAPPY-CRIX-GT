# Writes the .meta file Unity needs next to every file and folder of the package (a package
# installed from git is read-only, so Unity can't make them itself). GUIDs come from the path,
# so running it again gives the same ones. Folders ending in ~ are ignored by Unity and skipped.
#   python3 Tests~/make_meta.py
# Part of the Flappy Crix Arcade SDK - made with AI (Claude by Anthropic).
import hashlib, os

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PKG = "com.crixgamingvr.flappycrix-arcade"
TAIL = "  userData: \n  assetBundleName: \n  assetBundleVariant: \n"


def guid(rel):
    return hashlib.md5((PKG + "/" + rel.replace(os.sep, "/")).encode()).hexdigest()


def body(rel, folder):
    name = os.path.basename(rel)
    ext = os.path.splitext(name)[1].lower()
    if folder:
        return "folderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n" + TAIL
    if ext == ".cs":
        return ("MonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n  defaultReferences: []\n"
                "  executionOrder: 0\n  icon: {instanceID: 0}\n" + TAIL)
    if ext == ".asmdef":
        return "AssemblyDefinitionImporter:\n  externalObjects: {}\n" + TAIL
    if name == "package.json":
        return "PackageManifestImporter:\n  externalObjects: {}\n" + TAIL
    if ext == ".mp3":
        stream = 2 if "music" in name else 0   # long music streams, short sounds load into memory
        return ("AudioImporter:\n  externalObjects: {}\n  serializedVersion: 7\n  defaultSettings:\n"
                f"    serializedVersion: 2\n    loadType: {stream}\n    sampleRateSetting: 0\n    sampleRateOverride: 44100\n"
                "    compressionFormat: 1\n    quality: 0.7\n    conversionMode: 0\n    preloadAudioData: 1\n"
                "  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 1\n  loadInBackground: 0\n"
                "  ambisonic: 0\n  3D: 1\n" + TAIL)
    if ext in (".bytes", ".md", ".txt", ".json"):
        return "TextScriptImporter:\n  externalObjects: {}\n" + TAIL
    return "DefaultImporter:\n  externalObjects: {}\n" + TAIL


made = 0
for d, dirs, files in os.walk(ROOT):
    dirs[:] = sorted(x for x in dirs if not x.endswith("~") and not x.startswith("."))
    for n in sorted(dirs) + sorted(files):
        if n.endswith(".meta") or n.startswith("."):
            continue
        full = os.path.join(d, n)
        rel = os.path.relpath(full, ROOT)
        meta = full + ".meta"
        if os.path.exists(meta):
            continue
        with open(meta, "w", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: " + guid(rel) + "\n" + body(rel, os.path.isdir(full)))
        made += 1
# remove metas whose file is gone
for d, dirs, files in os.walk(ROOT):
    dirs[:] = [x for x in dirs if not x.endswith("~")]
    for n in files:
        if n.endswith(".meta") and not os.path.exists(os.path.join(d, n[:-5])):
            os.remove(os.path.join(d, n)); print("removed", n)
print(made, "meta files written")
