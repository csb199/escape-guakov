"""Inspect protobuf field evidence; numeric field tags are not inferred semantic names."""
import argparse,json,struct
from pathlib import Path
parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--source',type=Path,required=True)
parser.add_argument('--output',type=Path,required=True)
args=parser.parse_args();root=args.source
def var(b,p):
 v=0;s=0
 while True:
  x=b[p];p+=1;v|=(x&127)<<s
  if x<128:return v,p
  s+=7
  if s>63:raise ValueError()
def parse(b):
 out=[];p=0
 while p<len(b):
  tag,p=var(b,p);f,w=tag>>3,tag&7
  if not f:raise ValueError()
  if w==0:v,p=var(b,p)
  elif w==2:
   n,p=var(b,p);v=b[p:p+n];p+=n
   if p>len(b):raise ValueError()
   try:
    text=v.decode('utf8');v=text if all(ord(c)>=32 or c in '\n\r\t' for c in text) else parse(v)
   except Exception:
    try:v=parse(v)
    except Exception:v={'hex':v.hex()}
  elif w==5:v={'float':struct.unpack_from('<f',b,p)[0]};p+=4
  elif w==1:v={'double':struct.unpack_from('<d',b,p)[0]};p+=8
  else:raise ValueError()
  out.append([f,v])
 return out
out={}
for name in ['GunData.bytes','ModelConfigData.bytes','ModelAnimPathData.bytes','BarrackData.bytes','BarracksBackgroundData.bytes']:
 b=(root/name).read_bytes();header=struct.unpack_from('<I',b)[0];rows=parse(b[4+header:]);sel=[r for r in rows if any(s in json.dumps(r,ensure_ascii=False) for s in ['Groza','groza','闪电','Barracks'])] if 'Barrack' not in name else rows
 out[name]=sel
args.output.parent.mkdir(parents=True,exist_ok=True)
args.output.write_text(json.dumps(out,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps({name:len(rows) for name,rows in out.items()}))
