"""Convert the explicitly selected CMU ASF/AMC sources to editor-only pose data.

No downloaded code is executed. scipy implements the documented Acclaim XYZ
Euler rotations. Unit length 0.45 converts source units to metres by .0254/.45.
Original source files and this intermediate data are not runtime asset imports.
"""
from pathlib import Path
import json
import numpy as np
from scipy.spatial.transform import Rotation

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'Samples/MotionSources/CMU93'
OUTPUT=ROOT/'Assets/SongChoreography/Editor/MocapData'


def parse_asf(text):
    bones={'root':dict(name='root',parent='',direction=[0,0,0],length=0,axis=[0,0,0],dof=['rx','ry','rz'])}
    section='';current=None
    for raw in text.splitlines():
        line=raw.strip();words=line.split()
        if not words or line.startswith('#'):continue
        if line.startswith(':'):section=line;continue
        if section==':bonedata':
            if line=='begin':current={}
            elif line=='end':bones[current['name']]=current;current=None
            elif current is not None:
                key=words[0]
                if key=='name':current.update(name=words[1],parent='',dof=[])
                elif key in ('direction','axis'):current[key]=list(map(float,words[1:4]))
                elif key=='length':current[key]=float(words[1])
                elif key=='dof':current[key]=words[1:]
        elif section==':hierarchy' and line not in ('begin','end'):
            for child in words[1:]:bones[child]['parent']=words[0]
    ordered=[]
    def visit(name):
        ordered.append(bones[name])
        for child,bone in bones.items():
            if bone['parent']==name:visit(child)
    visit('root')
    assert len(ordered)==len(bones)==31,'Incomplete source skeleton'
    return ordered


def parse_amc(text):
    frames=[];frame=None
    for raw in text.splitlines():
        words=raw.split()
        if not words or words[0].startswith(('#',':')):continue
        if len(words)==1 and words[0].isdigit():
            frame={};frames.append(frame)
        elif frame is not None:frame[words[0]]=list(map(float,words[1:]))
    if not frames or any('root' not in f for f in frames):raise ValueError('Incomplete motion')
    return frames


def convert(bones,frames):
    unit=.0254/.45
    axes={b['name']:Rotation.from_euler('xyz',b['axis'],degrees=True) for b in bones}
    names=[b['name'] for b in bones]
    parents=[names.index(b['parent']) if b['parent'] else -1 for b in bones]
    offsets=[np.asarray(b['direction'])*b['length']*unit for b in bones]
    result=[]
    for frame in frames[::4]:
        rotations=[];positions=[]
        for index,bone in enumerate(bones):
            name=bone['name'];parent=parents[index]
            if parent<0:
                rotations.append(Rotation.from_euler('xyz',frame['root'][3:],degrees=True))
                positions.append(np.array(frame['root'][:3])*unit)
            else:
                angles=[0.,0.,0.]
                for dof,value in zip(bone['dof'],frame.get(name,[])):angles['xyz'.index(dof[1])]=value
                axis=axes[name]
                local=axis*Rotation.from_euler('xyz',angles,degrees=True)*axis.inv()
                rotations.append(rotations[parent]*local)
                positions.append(positions[parent]+rotations[parent].apply(offsets[parent]))
        values=np.concatenate([r.as_quat() for r in rotations]);points=np.concatenate(positions)
        if not np.all(np.isfinite(values)) or not np.all(np.isfinite(points)):raise ValueError('Non-finite pose')
        result.append(dict(root=positions[0].tolist(),rotations=values.round(7).tolist(),positions=points.round(6).tolist()))
    return dict(schemaVersion=1,fps=30,bones=[dict(name=b['name'],parent=parents[i],offset=offsets[i].round(7).tolist()) for i,b in enumerate(bones)],frames=result)


def main():
    OUTPUT.mkdir(parents=True,exist_ok=True)
    bones=parse_asf((SOURCE/'93.asf').read_text())
    for trial in (3,4,5,8):
        motion=f'93_{trial:02}'
        data=convert(bones,parse_amc((SOURCE/(motion+'.amc')).read_text()))
        data.update(source=motion,description='Charleston Dance / CMU subject 93')
        path=OUTPUT/(motion+'.json');path.write_text(json.dumps(data,separators=(',',':')),encoding='utf-8')
        print(f'{motion}: {len(data["frames"])} frames, {(len(data["frames"])-1)/30:.2f} sec at 30 fps')


if __name__=='__main__':main()
