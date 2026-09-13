import os, json, urllib.request, numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
UA={'User-Agent':'Mozilla/5.0'}
ART=r"D:\Tashkent city\AmirTemurSquare\Assets\AmirTemur\Art\Models"
RAW=r"D:\Tashkent city\data\raw\polyhaven_png"
def api(u): return json.load(urllib.request.urlopen(urllib.request.Request(u,headers=UA),timeout=60))
def get(url,dest):
    if os.path.exists(dest): return dest
    os.makedirs(os.path.dirname(dest),exist_ok=True)
    with urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=300) as r, open(dest,'wb') as f: f.write(r.read())
    return dest
def pick(j,keys):
    for k in keys:
        if k in j:
            v=j[k]; res='2k' if '2k' in v else list(v.keys())[0]
            fm=v[res]; ext='png' if 'png' in fm else ('jpg' if 'jpg' in fm else None)
            if ext: return fm[ext]['url'], ext
    return None,None
def gray(p,size):
    im=Image.open(p).convert('L')
    if im.size!=size: im=im.resize(size)
    return np.asarray(im,dtype=np.uint8)
for name in ['covered_car','fire_hydrant','island_tree_02','metal_trash_can','painted_wooden_bench','planter_box_01','shrub_02','shrub_03','shrub_04','street_lamp_01','street_lamp_02','tree_small_02','water_manhole_cover']:
    try:
        j=api(f'https://api.polyhaven.com/files/{name}')
        tdir=os.path.join(ART,name,'textures')
        out=os.path.join(tdir,f'{name}_Mask.png')
        base=None
        for f in os.listdir(tdir):
            if (f.startswith(name+'_diff') or f.startswith(name+'_Diffuse')) and not f.endswith('.meta') and not f.endswith('_BaseColorA.png'): base=os.path.join(tdir,f)
        if base is None: print('no base',name); continue
        size=Image.open(base).size
        maps={}
        for key,cands in [('rough',['Rough','rough']),('metal',['Metal','metal']),('ao',['AO','ao'])]:
            url,ext=pick(j,cands)
            if url: maps[key]=gray(get(url,os.path.join(RAW,name,f'{key}.{ext}')),size)
        h,w=size[1],size[0]
        r=maps.get('metal',np.zeros((h,w),np.uint8)); g=maps.get('ao',np.full((h,w),255,np.uint8)); a=255-maps['rough'] if 'rough' in maps else np.full((h,w),128,np.uint8)
        Image.fromarray(np.dstack([r,g,np.zeros((h,w),np.uint8),a]),'RGBA').save(out,compress_level=3)
        print('OK',name,list(maps.keys()))
    except Exception as e: print('FAIL',name,e)
print('PNGMASK DONE')
