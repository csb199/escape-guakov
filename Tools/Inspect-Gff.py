"""Read-only GFF v0 directory inspector; no files extracted or executed.
Format reference: EllanJiang/GameFramework FileSystem HeaderData/BlockData/StringData.
"""
import argparse, collections, json, struct
from pathlib import Path

def inspect(path):
    path=Path(path)
    size=path.stat().st_size
    with path.open('rb') as f:
        header=f.read(20)
        if len(header)!=20 or header[:4]!=b'GFF\0': raise ValueError('Not a GFF v0 file')
        maximum, blocks, used=struct.unpack_from('<iii',header,8)
        if not 0<maximum<=blocks or not 0<=used<=blocks: raise ValueError('Invalid GFF counts')
        strings=20+blocks*12
        if strings+maximum*256>size: raise ValueError('Truncated directory')
        names=set(); records=[]
        for i in range(used):
            f.seek(20+i*12)
            string_index,cluster,length=struct.unpack('<iii',f.read(12))
            if string_index<0: continue
            if string_index>=maximum or cluster<0 or length<0: raise ValueError('Invalid block')
            offset=cluster*4096
            if offset<strings+maximum*256 or offset+length>size: raise ValueError('Block outside file data')
            f.seek(strings+string_index*256)
            text=f.read(256); n=text[0]
            name=bytes(v^header[4+j%4] for j,v in enumerate(text[1:1+n])).decode('utf-8')
            if not name or name in names: raise ValueError('Empty or duplicate name')
            names.add(name)
            records.append(dict(name=name,offset=offset,length=length))
        return dict(source=str(path),size=size,files=len(records),
                    lengthHistogram=dict(collections.Counter(r['length'] for r in records)),records=records)

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source',type=Path);parser.add_argument('--output',required=True,type=Path)
    args=parser.parse_args();result=inspect(args.source)
    args.output.parent.mkdir(parents=True,exist_ok=True)
    args.output.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='records'},ensure_ascii=False))
