"""Fetch only the freely downloadable Standard character pack from its author.

No payment, account, or email is submitted. itch's public 'No thanks, take me to
downloads' workflow creates a download URL; only the Standard archive is used.
"""
from html.parser import HTMLParser
from io import RawIOBase
from pathlib import Path
import json
from hashlib import sha256
import requests
import zipfile
import binascii

ROOT=Path(__file__).resolve().parents[1]
BASE='https://quaternius.itch.io/universal-base-characters'


class Page(HTMLParser):
    def __init__(self,text):
        super().__init__();self.csrf=None;self.files=[];self.feed(text)
    def handle_starttag(self,tag,attrs):
        a=dict(attrs)
        if tag=='meta' and a.get('name')=='csrf_token':self.csrf=a.get('value')
        if tag=='a' and 'download_btn' in a.get('class',''):self.files.append(a)


class RemoteZip(RawIOBase):
    def __init__(self,session,url_provider):
        self.session=session;self.url_provider=url_provider;self.url=url_provider();self.position=0
        r=self.get_range('bytes=0-0')
        if r.status_code!=206:raise RuntimeError('Server does not support byte ranges.')
        self.length=int(r.headers['Content-Range'].split('/')[-1])
    def get_range(self,range_header):
        for attempt in range(2):
            r=self.session.get(self.url,headers={'Range':range_header},timeout=45)
            if r.status_code==403 and attempt==0:
                self.url=self.url_provider()  # CDN links issued by itch expire after 60 seconds.
                continue
            if r.status_code not in (200,206):raise RuntimeError('Character download failed: HTTP '+str(r.status_code))
            return r
    def seekable(self):return True
    def tell(self):return self.position
    def seek(self,offset,whence=0):
        self.position=offset if whence==0 else self.position+offset if whence==1 else self.length+offset
        return self.position
    def read(self,size=-1):
        if size<0:size=self.length-self.position
        size=min(size,self.length-self.position)
        if size<=0:return b''
        if size>25*1024*1024:raise RuntimeError('Unexpectedly large selected archive member.')
        r=self.get_range(f'bytes={self.position}-{self.position+size-1}')
        if r.status_code!=206 or len(r.content)!=size:raise RuntimeError('Invalid range response.')
        self.position+=size
        return r.content


def main():
    session=requests.Session()
    page=session.get(BASE+'/purchase',timeout=25);page.raise_for_status()
    csrf=Page(page.text).csrf
    if not csrf:raise RuntimeError('Free download form is missing.')
    result=session.post(BASE+'/download_url',data={'csrf_token':csrf},headers={'Referer':BASE+'/purchase'},timeout=25)
    result.raise_for_status();download_page=result.json()['url']
    content=session.get(download_page,timeout=25);content.raise_for_status()
    out=ROOT/'Samples/CharacterSources';out.mkdir(parents=True,exist_ok=True)
    # Keep the public metadata only; signed download tokens are deliberately not saved.
    parsed=Page(content.text)
    print('Free download buttons:',[(x.get('data-upload_id'),x.get('data-upload_name')) for x in parsed.files],flush=True)
    if not parsed.files:
        print(content.text[-9000:])
        raise RuntimeError('No free download buttons found.')
    standard=parsed.files[0]
    url=standard.get('href','')
    def fresh_url():
        if url.startswith('https://'):return url
        upload=standard.get('data-upload_id')
        response=session.post(BASE+'/file/'+upload,data={'csrf_token':csrf},headers={'Referer':download_page},timeout=25)
        response.raise_for_status();return response.json()['url']
    remote=RemoteZip(session,fresh_url)
    with zipfile.ZipFile(remote) as archive:
        names=archive.namelist()
        (out/'archive-files.json').write_text(json.dumps(names,indent=2),encoding='utf-8')
        print('Archive members:',len(names),flush=True)
        license_name=next(n for n in names if n.endswith('License_Standard.txt'))
        license_text=archive.read(license_name).decode('utf-8-sig')
        (out/'License_Standard.txt').write_text(license_text,encoding='utf-8')
        print('ARCHIVE LICENSE:',license_text,flush=True)
        if 'CC0' not in license_text and 'Creative Commons Zero' not in license_text:
            raise RuntimeError('Archive license requires review before importing.')
        destination=ROOT/'Assets/CharacterArt/Quaternius'
        destination.mkdir(parents=True,exist_ok=True)
        wanted=[]
        for name in names:
            if '/Base Characters/Unity/' in name and name.endswith('.fbx'):wanted.append(name)
            if '/Hairstyles/Origin at 0/FBX (Unity)/' in name and name.endswith(('Hair_Buns.fbx','Hair_SimpleParted.fbx','Eyebrows_Female.fbx','Eyebrows_Regular.fbx')):wanted.append(name)
            if '/Base Characters/Textures/' in name and '/Normals ' not in name and Path(name).name in {'T_Eye_Brown.png','T_Hair_1_BaseColor.png','T_Hair_2_BaseColor.png','T_Superhero_Female_Light_BaseColor.png','T_Superhero_Male_Ligh.png'}:wanted.append(name)
        wanted.sort(key=lambda name:0 if name.endswith('.fbx') else 1)
        evidence=[]
        for name in wanted:
            filename=Path(name).name
            existing=destination/filename
            payload=existing.read_bytes() if existing.exists() else b''
            if not payload or binascii.crc32(payload)!=archive.getinfo(name).CRC:payload=archive.read(name)
            (destination/filename).write_bytes(payload)
            evidence.append(dict(member=name,file=filename,sha256=sha256(payload).hexdigest()))
            print('Imported',filename,len(payload),flush=True)
        (destination/'License_Standard.txt').write_text(license_text,encoding='utf-8')
        (destination/'SOURCE.json').write_text(json.dumps(dict(author='Quaternius',source='https://quaternius.com/packs/universalbasecharacters.html',download=BASE,license='CC0-1.0',retrieved='2026-11-02',files=evidence),indent=2),encoding='utf-8')


if __name__=='__main__':main()
