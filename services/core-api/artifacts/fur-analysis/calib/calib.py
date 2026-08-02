import fitz, json, math, collections, os

OLD_TPL="src/Flit.Infrastructure/Documents/Fur/Templates/fur-formulario-p1-blank.pdf"
NEW_PDF="../../FUR/Formulario-automotor.pdf"
MANIFEST="src/Flit.Infrastructure/Documents/Fur/fur-field-manifest.json"

old=fitz.open(OLD_TPL)[0]
newdoc=fitz.open(NEW_PDF)
new=newdoc[0]

def words(pg):
    return [(w[4], (w[0]+w[2])/2, (w[1]+w[3])/2, w[0], w[1]) for w in pg.get_text("words")]

wo=words(old); wn=words(new)
def counts(ws):
    c=collections.Counter(w[0].upper() for w in ws); return c
co=counts(wo); cn=counts(wn)

# positional pairing for tokens with equal small counts
corr=[]  # (old_cx,old_cy,new_cx,new_cy, token)
for tok in set(co)&set(cn):
    if co[tok]!=cn[tok] or co[tok]>4: continue
    if len(tok)<2: continue
    oc=sorted([w for w in wo if w[0].upper()==tok], key=lambda w:(round(w[2]/3),w[1]))
    nc=sorted([w for w in wn if w[0].upper()==tok], key=lambda w:(round(w[2]/3),w[1]))
    for a,b in zip(oc,nc):
        corr.append([a[1],a[2],b[1],b[2],tok])

print("raw correspondences:", len(corr))

# robust global affine (independent x,y) via least squares, iterative outlier rejection
def fit_axis(src, dst):
    # dst = s*src + t  (1D)
    n=len(src); sx=sum(src); sy=sum(dst); sxx=sum(a*a for a in src); sxy=sum(a*b for a,b in zip(src,dst))
    den=n*sxx-sx*sx
    s=(n*sxy-sx*sy)/den; t=(sy-s*sx)/n
    return s,t

pts=corr[:]
for it in range(6):
    ox=[p[0] for p in pts]; nx=[p[2] for p in pts]
    oy=[p[1] for p in pts]; ny=[p[3] for p in pts]
    sx,tx=fit_axis(ox,nx); sy,ty=fit_axis(oy,ny)
    res=[]
    for p in pts:
        ex=sx*p[0]+tx; ey=sy*p[1]+ty
        r=math.hypot(ex-p[2],ey-p[3]); res.append(r)
    mean=sum(res)/len(res)
    keep=[p for p,r in zip(pts,res) if r< max(6, mean*1.8)]
    print(f"iter {it}: n={len(pts)} meanres={mean:.2f} sx={sx:.4f} tx={tx:.2f} sy={sy:.4f} ty={ty:.2f} -> keep {len(keep)}")
    if len(keep)==len(pts): break
    pts=keep

inliers=pts
print("inliers:", len(inliers))

# Moving Least Squares affine transform using inliers
P=[(p[0],p[1]) for p in inliers]
Q=[(p[2],p[3]) for p in inliers]

def mls(vx,vy,alpha=1.5):
    ws=[]
    for (px,py) in P:
        d2=(px-vx)**2+(py-vy)**2
        ws.append(1.0/ (d2**alpha + 1e-6))
    W=sum(ws)
    pcx=sum(w*px for w,(px,py) in zip(ws,P))/W
    pcy=sum(w*py for w,(px,py) in zip(ws,P))/W
    qcx=sum(w*qx for w,(qx,qy) in zip(ws,Q))/W
    qcy=sum(w*qy for w,(qx,qy) in zip(ws,Q))/W
    # M = (sum w phat^T phat)^-1 (sum w phat^T qhat)
    a11=a12=a21=a22=0.0
    b11=b12=b21=b22=0.0
    for w,(px,py),(qx,qy) in zip(ws,P,Q):
        phx=px-pcx; phy=py-pcy; qhx=qx-qcx; qhy=qy-qcy
        a11+=w*phx*phx; a12+=w*phx*phy; a21+=w*phy*phx; a22+=w*phy*phy
        b11+=w*phx*qhx; b12+=w*phx*qhy; b21+=w*phy*qhx; b22+=w*phy*qhy
    det=a11*a22-a12*a21
    if abs(det)<1e-9: 
        return qcx+(vx-pcx), qcy+(vy-pcy), 1.0,1.0
    i11=a22/det; i12=-a12/det; i21=-a21/det; i22=a11/det
    # M = inv(A) * B
    m11=i11*b11+i12*b21; m12=i11*b12+i12*b22
    m21=i21*b11+i22*b21; m22=i21*b12+i22*b22
    dvx=vx-pcx; dvy=vy-pcy
    fx=dvx*m11+dvy*m21+qcx
    fy=dvx*m12+dvy*m22+qcy
    # local scales
    sxl=math.hypot(m11,m12); syl=math.hypot(m21,m22)
    return fx,fy,sxl,syl

man=json.load(open(MANIFEST,encoding="utf-8"))
newman=dict(man); newman["pageWidth"]=792; newman["pageHeight"]=612
newman["version"]=man["version"]+"-newtpl792"
outf=[]
for f in man["fields"]:
    g=dict(f)
    fx,fy,sxl,syl=mls(f["x"],f["y"])
    g["x"]=round(fx,1); g["y"]=round(fy,1)
    if "w" in f: g["w"]=round(f["w"]*sxl,1)
    if "h" in f: g["h"]=round(f["h"]*syl,1)
    if "size" in f: g["size"]=round(f["size"]*(sxl+syl)/2,1)
    # fontSize: scale gently by syl clamped
    if "fontSize" in f:
        fs=f["fontSize"]*max(0.9,min(1.12,(sxl+syl)/2))
        g["fontSize"]=round(fs,1)
    outf.append(g)
newman["fields"]=outf
json.dump(newman, open("artifacts/fur-analysis/calib/manifest-792.json","w",encoding="utf-8"), ensure_ascii=False, indent=2)
print("wrote candidate manifest")
