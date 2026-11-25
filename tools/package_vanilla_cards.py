"""Package the readable data and provenance, excluding local engine/device logs."""
from pathlib import Path
import hashlib
import json
import zipfile

root=Path(__file__).resolve().parents[1]
folder=root/'outputs/claude-vanilla-cards-0.113.1'
report=json.loads((folder/'导出核对.json').read_text('utf-8'))
assert report['status']=='pass' and report['cards']==717 and report['nativeExportErrors']==0
files=sorted(p for p in [*folder.glob('*.md'),*folder.glob('*.json'),*folder.glob('*.jsonl'),*folder.glob('按卡池阅读/*.md')] if p.name!='文件校验.json')
manifest={p.relative_to(folder).as_posix():{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest().upper()} for p in files}
(folder/'文件校验.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
files.append(folder/'文件校验.json')
archive=folder.parent/'给Claude-原版全卡数据-v0.113.1.zip'
with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=9) as z:
    for p in files:z.write(p,p.relative_to(folder).as_posix())
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None and len(z.namelist())==len(files)
    for name,entry in manifest.items():assert hashlib.sha256(z.read(name)).hexdigest().upper()==entry['sha256']
    assert len(json.loads(z.read('原版全卡数据.json'))['cards'])==717
print(f'PASS: {archive} / {len(files)} files / {archive.stat().st_size:,} bytes')
print('SHA256: '+hashlib.sha256(archive.read_bytes()).hexdigest().upper())
