import json
from pathlib import Path

d = json.loads(Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\ink-blobs.json").read_text())
for name in ("golden_blob_count",):
    pass
all_g = json.loads(Path(r"d:\Cursor\FLIT\2.0\services\core-api\artifacts\fur-analysis\ink-blobs.json").read_text())
# reload full golden from script output - use calibrate-ink again with all blobs saved
import fitz, numpy as np
from PIL import Image
from pathlib import Path
ROOT = Path(r"d:\Cursor\FLIT\2.0\services\core-api")
BLANK = ROOT / "src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
GOLDEN = Path(r"d:\Cursor\FLIT\2.0\FUR.pdf")
W,H=1008,612

def render(path):
    doc=fitz.open(path); page=doc[0]
    mat=fitz.Matrix(W/page.rect.width,H/page.rect.height)
    pix=page.get_pixmap(matrix=mat,alpha=False); doc.close()
    return np.frombuffer(pix.samples,dtype=np.uint8).reshape(pix.height,pix.width,3)

def ink_mask(a,b,thresh=35):
    return np.abs(a.astype(np.int16)-b.astype(np.int16)).max(axis=2)>thresh

def blobs(mask,min_pixels=30):
    visited=np.zeros(mask.shape,dtype=bool); h,w=mask.shape; found=[]
    for y in range(h):
        for x in range(w):
            if not mask[y,x] or visited[y,x]: continue
            stack=[(x,y)]; pts=[]
            visited[y,x]=True
            while stack:
                cx,cy=stack.pop(); pts.append((cx,cy))
                for nx,ny in ((cx-1,cy),(cx+1,cy),(cx,cy-1),(cx,cy+1)):
                    if 0<=nx<w and 0<=ny<h and mask[ny,nx] and not visited[ny,nx]:
                        visited[ny,nx]=True; stack.append((nx,ny))
            if len(pts)<min_pixels: continue
            xs=[p[0] for p in pts]; ys=[p[1] for p in pts]
            found.append((min(ys),min(xs),max(xs)-min(xs),max(ys)-min(ys),len(pts)))
    return sorted(found)

g=blobs(ink_mask(render(GOLDEN),render(BLANK)))
print('GOLDEN small blobs y=90-520:')
for y,x,w,h,px in g:
    if 90<=y<=520 and w<100 and h<15:
        print(f'  y={y:5.1f} x={x:5.1f} w={w:5.1f} h={h:4.1f} px={px}')
