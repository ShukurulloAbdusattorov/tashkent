import json, os, sys, urllib.request, time, zipfile
UA={'User-Agent':'Mozilla/5.0 (asset fetch for Unity project)'}
RAW=r"D:\Tashkent city\data\raw"
def get(url, dest, retries=3):
    if os.path.exists(dest) and os.path.getsize(dest)>0: return True
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    for i in range(retries):
        try:
            with urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=180) as r, open(dest+'.part','wb') as f:
                while True:
                    b=r.read(1<<20)
                    if not b: break
                    f.write(b)
            os.replace(dest+'.part',dest); print('OK',dest,os.path.getsize(dest)); return True
        except Exception as e:
            print('RETRY',url,e); time.sleep(3)
    print('FAIL',url); return False
def api(url):
    return json.load(urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=60))
# --- Poly Haven models (fbx + textures) ---
MODELS=[('jacaranda_tree','2k'),('tree_small_02','2k'),('island_tree_02','2k'),('street_lamp_01','2k'),('street_lamp_02','2k'),
        ('painted_wooden_bench','2k'),('modular_street_seating','2k'),('metal_trash_can','2k'),('fire_hydrant','2k'),
        ('water_manhole_cover','2k'),('horse_statue_01','2k'),('shrub_02','2k'),('shrub_03','2k'),('shrub_04','2k'),('planter_box_01','2k'),('fir_sapling_medium','2k'),('covered_car','2k')]
for name,res in MODELS:
    try:
        j=api(f'https://api.polyhaven.com/files/{name}')
        fx=j['fbx'][res]['fbx']
        base=os.path.join(RAW,'polyhaven_models',name)
        get(fx['url'], os.path.join(base,f'{name}.fbx'))
        for rel,info in fx.get('include',{}).items():
            get(info['url'], os.path.join(base,rel.replace('/',os.sep)))
    except Exception as e: print('MODEL FAIL',name,e)
# --- HDRIs ---
for h in ['kloofendal_48d_partly_cloudy_puresky','kloppenheim_06_puresky','moonless_golf']:
    try:
        j=api(f'https://api.polyhaven.com/files/{h}')
        get(j['hdri']['4k']['hdr']['url'], os.path.join(RAW,'hdri',f'{h}_4k.hdr'))
    except Exception as e: print('HDRI FAIL',h,e)
# --- ambientCG PBR textures 2K JPG ---
TEX=['Asphalt012','Asphalt025','Concrete034','Concrete016','Concrete036','PavingStones070','PavingStones131','PavingStones138','PavingStones092',
     'Marble016','Marble012','Granite001','Granite007','Grass004','Grass001','Ground037','Bricks075A','Facade001','Facade018','Facade020','Facade006',
     'Metal032','Metal034','Tiles074','Tiles093','Rock030','Plaster001','Concrete023','Travertine008','Metal009','MetalPlates006','Fabric030','Wood051','Leather011']
for t in TEX:
    get(f'https://ambientcg.com/get?file={t}_2K-JPG.zip', os.path.join(RAW,'ambientcg',f'{t}_2K-JPG.zip'))
# --- Unity Standard Assets Characters (mocap + DefaultMale) ---
get('https://github.com/Unity-Technologies/Standard-Assets-Characters/archive/refs/heads/master.zip', os.path.join(RAW,'Standard-Assets-Characters-master.zip'))
print('ALL DONE')
