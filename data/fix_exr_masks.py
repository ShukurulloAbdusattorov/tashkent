import os, glob, re
os.environ["OPENCV_IO_ENABLE_OPENEXR"]="1"
import cv2, numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
ART=r"D:\Tashkent city\AmirTemurSquare\Assets\AmirTemur\Art\Models"
def gray(p,size):
    if p.lower().endswith('.exr'):
        im=cv2.imread(p, cv2.IMREAD_UNCHANGED)
        if im is None: raise Exception('cv2 fail '+p)
        if im.ndim==3: im=im[...,0]
        im=np.clip(im,0,1)*255; im=im.astype(np.uint8)
        if (im.shape[1],im.shape[0])!=size: im=cv2.resize(im,size)
        return im
    im=Image.open(p).convert('L')
    if im.size!=size: im=im.resize(size)
    return np.asarray(im,dtype=np.uint8)
for d in glob.glob(os.path.join(ART,'*')):
    name=os.path.basename(d); tdir=os.path.join(d,'textures')
    groups={}
    for t in glob.glob(os.path.join(tdir,'*')):
        m=re.match(r"(.+?)_(diff|diffuse|nor_gl|nor_dx|arm|rough|ao|metal|alpha|mask|disp|opacity|spec)_(\d+k)\.(\w+)$", os.path.basename(t), re.I)
        if m: groups.setdefault(m.group(1),{})[m.group(2).lower()]=t
    for prefix,g in groups.items():
        out=os.path.join(tdir,f"{prefix}_Mask.png")
        if os.path.exists(out) or 'arm' in g: continue
        base=g.get('diff') or g.get('diffuse')
        if not base: print('no base',name,prefix); continue
        size=Image.open(base).size
        try:
            rough=gray(g['rough'],size) if 'rough' in g else None
            metal=gray(g['metal'],size) if 'metal' in g else None
            ao=gray(g['ao'],size) if 'ao' in g else None
            h,w=size[1],size[0]
            r=metal if metal is not None else np.zeros((h,w),np.uint8)
            gg=ao if ao is not None else np.full((h,w),255,np.uint8)
            a=(255-rough) if rough is not None else np.full((h,w),128,np.uint8)
            Image.fromarray(np.dstack([r,gg,np.zeros((h,w),np.uint8),a]),'RGBA').save(out,compress_level=3)
            print('OK',name,prefix,list(g.keys()))
        except Exception as e: print('FAIL',name,prefix,e)
print('FIX DONE')
