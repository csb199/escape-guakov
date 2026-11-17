"""Run with qrenderdoc --python. Captures a live target; rejects queued historical notifications.
Environment: BARRACK_CAPTURE_IDENT, BARRACK_CAPTURE_COUNT, BARRACK_CAPTURE_OUTPUT.
The operator must perform the desired motion while status.json says ARMED/CAPTURING.
"""
import json,os,time,traceback
from pathlib import Path
import renderdoc as rd
root=Path(os.environ.get('BARRACK_CAPTURE_OUTPUT','F:/escape-guakov/LocalOnly/Barrack/Captures/operator-sequence'))
root.mkdir(parents=True,exist_ok=True)
count=int(os.environ.get('BARRACK_CAPTURE_COUNT','24'));ident=int(os.environ.get('BARRACK_CAPTURE_IDENT','38920'))
report={'status':'CONNECTING','requested':count,'captures':[]};control=None

def save():
    (root/'status.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
try:
    control=rd.CreateTargetControl('',ident,'Barrack continuous validation',False)
    if control is None:raise RuntimeError('RenderDoc target unavailable')
    report.update(pid=control.GetPID(),target=control.GetTarget());save()
    end=time.time()+3
    while time.time()<end:control.ReceiveMessage(None)
    report.update(status='ARMED',triggerTime=time.time());save();control.TriggerCapture(count)
    report['status']='CAPTURING';save();deadline=time.time()+240
    seen=set()
    while time.time()<deadline and control.Connected():
        message=control.ReceiveMessage(None)
        if message.type!=rd.TargetControlMessageType.NewCapture:continue
        path=Path(message.newCapture.path)
        if str(path)in seen or not path.exists()or path.stat().st_mtime<report['triggerTime']:continue
        seen.add(str(path));report['captures'].append({'path':str(path),'captureId':message.newCapture.captureId,'bytes':path.stat().st_size});save()
        if len(report['captures'])>=count:break
    report['status']='COLLECTED'if len(report['captures'])==count else 'INCOMPLETE'
except Exception:report.update(status='FAILED',error=traceback.format_exc())
finally:
    if control:control.Shutdown()
    save()
raise SystemExit()
