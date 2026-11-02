import json
import numpy as np
from scipy.spatial.transform import Rotation as R
from pathlib import Path
d=json.loads(Path('Assets/SongChoreography/Editor/MocapData/93_03.json').read_text())
f=d['frames'][7:96]; b=d['bones']; ids={v['name']:i for i,v in enumerate(b)}
def unit(v): return v/max(np.linalg.norm(v),1e-12)
poses=[]
for i,v in enumerate(f):
    t=i/(len(f)-1);ease=t*t*(3-2*t);q=R.from_quat(np.array(v['rotations']).reshape(-1,4));a=R.from_quat(np.array(f[0]['rotations']).reshape(-1,4));z=R.from_quat(np.array(f[-1]['rotations']).reshape(-1,4));delta=(z*a.inv()).as_rotvec();q=R.from_rotvec(-delta*ease)*q
    p=[]
    for j,bone in enumerate(b):
        parent=bone['parent'];p.append(np.zeros(3) if parent<0 else p[parent]+q[parent].apply(b[parent]['offset']))
    poses.append(p)
poses=np.array(poses)
for side in ('l','r'):
    u=np.array([unit(p[ids[side+'tibia']]-p[ids[side+'femur']]) for p in poses]);v=np.array([unit(p[ids[side+'foot']]-p[ids[side+'tibia']]) for p in poses]);cross=np.cross(u,v);weights=(cross**2).sum(axis=1)[:-1];n=np.array([unit(x) for x in cross[:-1]]);count=len(n);anchor=weights.argmax();previous=n[anchor]
    for step in range(count):
        j=(anchor+step)%count
        if np.dot(n[j],previous)<0:n[j]*=-1
        previous=n[j].copy()
    filtered=[]
    for i in range(count):
        s=sum(n[(i+o)%count]*(np.exp(-o*o/8)*max(.001,weights[(i+o)%count])) for o in range(-4,5));s-=np.dot(s,u[i])*u[i];filtered.append(unit(s))
    dots=np.array([np.dot(filtered[i],filtered[(i+1)%count]) for i in range(count)])
    print(side,'anchor',anchor,'seam dot',np.dot(n[0],n[-1]),'worst',np.argsort(dots)[:8],dots[np.argsort(dots)[:8]])
    for j in range(79,87):print(j,'bend',np.degrees(np.arccos(np.clip(np.dot(u[j],v[j]),-1,1))),'normal',filtered[j],'sourceW',weights[j])
