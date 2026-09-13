import os, urllib.request, time
UA={'User-Agent':'Mozilla/5.0'}
RAW=r"D:\Tashkent city\data\raw\ambientcg"
def get(url,dest):
    if os.path.exists(dest): return
    for i in range(3):
        try:
            with urllib.request.urlopen(urllib.request.Request(url,headers=UA),timeout=180) as r, open(dest+'.part','wb') as f: f.write(r.read())
            os.replace(dest+'.part',dest); print('OK',dest); return
        except Exception as e: print('RETRY',url,e); time.sleep(2)
for t in ['Granite001A','Granite005A','Road007','Asphalt031','Asphalt033','Facade018A','Facade020A','Facade019A','Facade005','Facade013','Facade009','PavingStones151','PavingStones142','Tiles098','PaintedPlaster006','Concrete042A','Marble023']:
    get(f'https://ambientcg.com/get?file={t}_2K-JPG.zip', os.path.join(RAW,f'{t}_2K-JPG.zip'))
print('EXTRA DONE')
