"""Build contact sheets and trajectory plots from Unity-captured review evidence."""
import json
from pathlib import Path
import sys
import numpy as np
from PIL import Image, ImageDraw, ImageFont

folder=Path(sys.argv[1])
report=json.loads((folder/'trajectories.json').read_text(encoding='utf-8'))
font=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',18)
small=ImageFont.truetype('C:/Windows/Fonts/arial.ttf',15)
for clip in report['clips']:
    directory=folder/clip['id']
    indices=[0,2,4,6,8,10,12,14]
    board=Image.new('RGB',(1280,900),'#101923');draw=ImageDraw.Draw(board)
    draw.text((12,8),clip['id']+' | '+str(round(clip['length'],3))+'s',font=font,fill='white')
    for cell,i in enumerate(indices):
        x=(cell%4)*320;y=40+(cell//4)*430
        board.paste(Image.open(directory/f'front-{i:02d}.png').convert('RGB'),(x,y))
        draw.text((x+8,y+402),f't={clip["length"]*i/16:.3f}s',font=small,fill='white')
    board.save(directory/'front-board.jpg',quality=93)
    side=Image.new('RGB',(1280,900),'#101923');draw=ImageDraw.Draw(side)
    draw.text((12,8),clip['id']+' | SIDE',font=font,fill='white')
    for cell,i in enumerate(indices):
        x=(cell%4)*320;y=40+(cell//4)*430
        side.paste(Image.open(directory/f'side-{i:02d}.png').convert('RGB'),(x,y))
        draw.text((x+8,y+402),f't={clip["length"]*i/16:.3f}s',font=small,fill='white')
    side.save(directory/'side-board.jpg',quality=93)
    frames=clip['frames'];plot=Image.new('RGB',(1280,560),'white');draw=ImageDraw.Draw(plot)
    draw.text((15,10),clip['id']+' | raw bone trajectories (metres)',font=font,fill='black')
    signals=[]
    for bone,axis,color in [('hips','y','#222222'),('leftFoot','y','#16834a'),('rightFoot','y','#b22c35'),('leftHand','y','#317ac2'),('rightHand','y','#b763cb')]:
        values=np.array([f[bone][axis] for f in frames]);signals.append((bone+' '+axis,values,color))
    low=min(v.min() for _,v,_ in signals)-.05;high=max(v.max() for _,v,_ in signals)+.05
    for k in range(11):
        y=80+k*40;draw.line((70,y,1240,y),fill='#dddddd');draw.text((5,y-8),f'{high-(high-low)*k/10:.2f}',font=small,fill='black')
    for j,(name,values,color) in enumerate(signals):
        xy=[(70+1170*f['time']/clip['length'],80+400*(high-float(value))/(high-low)) for f,value in zip(frames,values)]
        draw.line(xy,fill=color,width=2);draw.text((80+j*230,510),name,font=small,fill=color)
    for k in range(int(clip['length'])+1):
        x=70+1170*k/clip['length'];draw.line((x,80,x,480),fill='#dddddd');draw.text((x,482),str(k),font=small,fill='black')
    plot.save(directory/'trajectories.png')
print('Created contact sheets and trajectory plots for',len(report['clips']),'clips.')
