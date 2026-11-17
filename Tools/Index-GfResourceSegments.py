"""Index logical UnityFS segments described by the client's GFF 40-byte records.
Read-only source access. Output contains resource locations, never process memory.
"""
import argparse, json, struct, sys, importlib.util
from pathlib import Path

def records(manifest):
    spec=importlib.util.spec_from_file_location('gff_inspector',Path(__file__).with_name('Inspect-Gff.py'))
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    directory=module.inspect(manifest)
    with Path(manifest).open('rb') as stream:
        for entry in directory['records']:
            if entry['length']!=40:raise ValueError('Expected 40-byte segment record')
            stream.seek(entry['offset']);record=stream.read(40)
            offset,length=struct.unpack_from('<II',record,16)
            yield dict(logical=entry['name'],physical=record[:16].hex()+'.bundle',offset=offset,length=length,recordHash=record[24:].hex())

def segment_bytes(record,root,start=0,length=None):
    path=Path(root)/record['physical'];offset=record['offset'];size=record['length']
    if offset<0 or size<48 or offset+size>path.stat().st_size:raise ValueError('Segment outside physical file')
    length=size-start if length is None else length
    if start<0 or length<0 or start+length>size:raise ValueError('Read outside segment')
    with path.open('rb') as stream:
        stream.seek(offset);head=stream.read(16)
        key=None if head[:8]==b'UnityFS\0' else bytes(a^b for a,b in zip(head,bytes.fromhex('556e69747946530000000007352e782e')))
        stream.seek(offset+start);data=bytearray(stream.read(length))
    if len(data)!=length:raise ValueError('Truncated segment')
    if key:
        for i in range(min(length,max(0,32768-start))):data[i]^=key[(start+i)%16]
    return bytes(data)

def index_segment(record,root):
    if record['length']==0 and record['offset']==0 and record['logical']==record['physical']:
        record=dict(record,length=(Path(root)/record['physical']).stat().st_size,standalone=True)
    from lz4.block import decompress
    h=segment_bytes(record,root,0,min(256,record['length']))
    if h[:8]!=b'UnityFS\0':raise ValueError('Not UnityFS')
    version=struct.unpack_from('>I',h,8)[0];pos=h.index(0,12)+1;pos=h.index(0,pos)+1
    size,compressed,expanded,flags=struct.unpack_from('>QIII',h,pos);pos+=20
    if size!=record['length']:raise ValueError('Segment size does not match UnityFS header')
    if version>=7:pos=(pos+15)//16*16
    if flags&128:pos=size-compressed
    if not 0<expanded<16*1024*1024:raise ValueError('Invalid directory size')
    data=segment_bytes(record,root,pos,compressed)
    compression=flags&63
    if compression in (2,3):data=decompress(data,uncompressed_size=expanded)
    elif compression!=0:raise ValueError('Unsupported directory compression '+str(compression))
    if len(data)!=expanded:raise ValueError('Directory length mismatch')
    blocks=struct.unpack_from('>I',data,16)[0];cur=20+blocks*10
    count=struct.unpack_from('>I',data,cur)[0];cur+=4;names=[]
    for _ in range(count):
        cur+=20;end=data.index(0,cur);names.append(data[cur:end].decode());cur=end+1
    if cur!=len(data):raise ValueError('Unexpected directory tail')
    return dict(record,cab=[n for n in names if n.startswith('CAB-') and not n.endswith(('.resS','.resource'))],entries=names)

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--manifest',required=True,type=Path);p.add_argument('--bundle-root',required=True,type=Path);p.add_argument('--python-path',type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
    if a.python_path:sys.path.insert(0,str(a.python_path))
    a.output.parent.mkdir(parents=True,exist_ok=True);count=errors=0
    with a.output.open('w',encoding='utf8') as out:
        for record in records(a.manifest):
            try:row=index_segment(record,a.bundle_root)
            except Exception as ex:row=dict(record,error=str(ex));errors+=1
            out.write(json.dumps(row)+'\n');count+=1
            if count%1000==0:out.flush();print(json.dumps(dict(segments=count,errors=errors)),flush=True)
    print(json.dumps(dict(segments=count,errors=errors,complete=True)))
if __name__=='__main__':main()
