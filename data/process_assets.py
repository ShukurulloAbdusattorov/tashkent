"""Unpack downloaded raw assets into the Unity project with HDRP-ready textures.
- ambientCG zips  -> Assets/AmirTemur/Art/Textures/<Set>/<Set>_BaseColor.jpg, _Normal.jpg (GL), _Mask.png (R metal, G AO, B 0, A smooth), _Height.jpg
- Poly Haven fbx  -> Assets/AmirTemur/Art/Models/<name>/<name>.fbx + textures/ (+ generated *_Mask.png and *_BaseColorA.png for alpha)
- HDRIs           -> Assets/AmirTemur/Art/HDRI/
- Standard Assets Characters (DefaultMale + Male animations) -> Assets/AmirTemur/Art/Character/
- city.json       -> Assets/AmirTemur/Data/city.json
"""
import os, sys, zipfile, shutil, re, glob
from PIL import Image
import numpy as np
Image.MAX_IMAGE_PIXELS = None
RAW = r"D:\Tashkent city\data\raw"
PROJ = r"D:\Tashkent city\AmirTemurSquare\Assets\AmirTemur"
ART = os.path.join(PROJ, "Art")
def ensure(p): os.makedirs(p, exist_ok=True); return p
def load_gray(path, size=None):
    im = Image.open(path).convert("L")
    if size and im.size != size: im = im.resize(size, Image.LANCZOS)
    return np.asarray(im, dtype=np.uint8)
def write_mask(out, size, metal=None, ao=None, rough=None):
    w, h = size
    r = metal if metal is not None else np.zeros((h, w), np.uint8)
    g = ao if ao is not None else np.full((h, w), 255, np.uint8)
    b = np.zeros((h, w), np.uint8)
    a = (255 - rough) if rough is not None else np.full((h, w), 128, np.uint8)
    Image.fromarray(np.dstack([r, g, b, a]), "RGBA").save(out, optimize=False, compress_level=3)

# ---------------- ambientCG ----------------
def process_ambientcg():
    for z in sorted(glob.glob(os.path.join(RAW, "ambientcg", "*.zip"))):
        setname = os.path.basename(z).replace("_2K-JPG.zip", "")
        dst = ensure(os.path.join(ART, "Textures", setname))
        if os.path.exists(os.path.join(dst, f"{setname}_Mask.png")): print("skip", setname); continue
        try:
            with zipfile.ZipFile(z) as zf:
                names = zf.namelist()
                def find(suffix):
                    for n in names:
                        if n.lower().endswith(suffix.lower()): return n
                    return None
                tmp = ensure(os.path.join(RAW, "_tmp", setname))
                for suf, outname in [("_Color.jpg", f"{setname}_BaseColor.jpg"), ("_NormalGL.jpg", f"{setname}_Normal.jpg"), ("_Displacement.jpg", f"{setname}_Height.jpg")]:
                    n = find(suf)
                    if n:
                        with zf.open(n) as src, open(os.path.join(dst, outname), "wb") as f: shutil.copyfileobj(src, f)
                rough = find("_Roughness.jpg"); ao = find("_AmbientOcclusion.jpg"); metal = find("_Metalness.jpg"); opac = find("_Opacity.jpg")
                def extract(n):
                    if not n: return None
                    p = os.path.join(tmp, os.path.basename(n))
                    with zf.open(n) as src, open(p, "wb") as f: shutil.copyfileobj(src, f)
                    return p
                rp, ap, mp, op = extract(rough), extract(ao), extract(metal), extract(opac)
                base = Image.open(os.path.join(dst, f"{setname}_BaseColor.jpg")); size = base.size
                write_mask(os.path.join(dst, f"{setname}_Mask.png"), size,
                           metal=load_gray(mp, size) if mp else None, ao=load_gray(ap, size) if ap else None, rough=load_gray(rp, size) if rp else None)
                if op:
                    rgba = base.convert("RGBA"); rgba.putalpha(Image.open(op).convert("L").resize(size))
                    rgba.save(os.path.join(dst, f"{setname}_BaseColor.png")); os.remove(os.path.join(dst, f"{setname}_BaseColor.jpg"))
                shutil.rmtree(tmp, ignore_errors=True)
                print("OK", setname, size)
        except Exception as e:
            print("FAIL", setname, e)

# ---------------- Poly Haven models ----------------
def process_polyhaven():
    for d in sorted(glob.glob(os.path.join(RAW, "polyhaven_models", "*"))):
        name = os.path.basename(d)
        fbx = os.path.join(d, f"{name}.fbx")
        if not os.path.exists(fbx): print("no fbx", name); continue
        dst = ensure(os.path.join(ART, "Models", name)); tdst = ensure(os.path.join(dst, "textures"))
        shutil.copy2(fbx, os.path.join(dst, f"{name}.fbx"))
        texs = glob.glob(os.path.join(d, "textures", "*"))
        for t in texs: shutil.copy2(t, os.path.join(tdst, os.path.basename(t)))
        # group by prefix: <prefix>_(diff|nor_gl|arm|rough|alpha|mask|...)_2k.ext
        groups = {}
        for t in texs:
            b = os.path.basename(t)
            m = re.match(r"(.+?)_(diff|diffuse|nor_gl|nor_dx|arm|rough|ao|metal|alpha|mask|disp|opacity|spec)_(\d+k)\.(\w+)$", b, re.I)
            if not m: continue
            groups.setdefault(m.group(1), {})[m.group(2).lower()] = t
        for prefix, g in groups.items():
            try:
                base = None
                if "diff" in g or "diffuse" in g: base = Image.open(g.get("diff", g.get("diffuse")))
                size = base.size if base else None
                if "arm" in g:
                    arm = np.asarray(Image.open(g["arm"]).convert("RGB"), dtype=np.uint8)
                    if size and arm.shape[1::-1] != size: arm = np.asarray(Image.fromarray(arm).resize(size), dtype=np.uint8)
                    ao, rough, metal = arm[..., 0], arm[..., 1], arm[..., 2]
                    write_mask(os.path.join(tdst, f"{prefix}_Mask.png"), (arm.shape[1], arm.shape[0]), metal=metal, ao=ao, rough=rough)
                elif "rough" in g and size:
                    write_mask(os.path.join(tdst, f"{prefix}_Mask.png"), size, metal=load_gray(g["metal"], size) if "metal" in g else None,
                               ao=load_gray(g["ao"], size) if "ao" in g else None, rough=load_gray(g["rough"], size))
                alpha = g.get("alpha") or g.get("mask") or g.get("opacity")
                if alpha and base is not None:
                    rgba = base.convert("RGBA"); rgba.putalpha(Image.open(alpha).convert("L").resize(size))
                    rgba.save(os.path.join(tdst, f"{prefix}_BaseColorA.png"))
                print("OK model tex", name, prefix, list(g.keys()))
            except Exception as e:
                print("FAIL model tex", name, prefix, e)

# ---------------- HDRI ----------------
def process_hdri():
    dst = ensure(os.path.join(ART, "HDRI"))
    for h in glob.glob(os.path.join(RAW, "hdri", "*.hdr")): shutil.copy2(h, os.path.join(dst, os.path.basename(h))); print("OK hdri", os.path.basename(h))

# ---------------- Character ----------------
def process_character():
    z = os.path.join(RAW, "Standard-Assets-Characters-master.zip")
    if not os.path.exists(z): print("no character zip"); return
    dst = ensure(os.path.join(ART, "Character"))
    with zipfile.ZipFile(z) as zf:
        n = 0
        for name in zf.namelist():
            if name.endswith("/"): continue
            keep = ("/Characters/Models/Male/" in name or "/Characters/Animation/Male/" in name or "/Characters/Models/Female/" in name or "/Characters/Animation/Female/" in name) and not name.endswith(".meta")
            if not keep: continue
            rel = name.split("/Characters/", 1)[1]
            out = os.path.join(dst, rel.replace("/", os.sep)); ensure(os.path.dirname(out))
            with zf.open(name) as src, open(out, "wb") as f: shutil.copyfileobj(src, f)
            n += 1
        # license
        for name in zf.namelist():
            if name.endswith("LICENSE.md") and name.count("/") == 1:
                with zf.open(name) as src, open(os.path.join(dst, "LICENSE_UnityCompanion.md"), "wb") as f: shutil.copyfileobj(src, f)
        print("OK character files", n)

def process_data():
    dst = ensure(os.path.join(PROJ, "Data")); shutil.copy2(r"D:\Tashkent city\data\city.json", os.path.join(dst, "city.json")); print("OK city.json")

if __name__ == "__main__":
    steps = sys.argv[1:] or ["data", "hdri", "character", "ambientcg", "polyhaven"]
    for s in steps: globals()["process_" + s]()
    print("PROCESS DONE")
