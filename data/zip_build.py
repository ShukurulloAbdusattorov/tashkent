import os, zipfile, sys
src=r"D:\Tashkent city\Build"; dst=r"D:\Tashkent city\AmirTemurSquare_Win64.zip"
if os.path.exists(dst): os.remove(dst)
n=0
with zipfile.ZipFile(dst,'w',zipfile.ZIP_DEFLATED,allowZip64=True,compresslevel=6) as z:
    for root,dirs,files in os.walk(src):
        dirs[:]=[d for d in dirs if 'DoNotShip' not in d]
        for f in files:
            p=os.path.join(root,f); z.write(p, os.path.relpath(p,src)); n+=1
print('files',n,'size',os.path.getsize(dst))
print('ZIP DONE')
