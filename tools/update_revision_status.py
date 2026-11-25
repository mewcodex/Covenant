"""Summarize the current source and exact-binary proofs without implying an unverified release."""
import hashlib,json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
read=lambda p:json.loads(p.read_text('utf-8-sig'))
sha=hashlib.sha256((root/'bin/Release/net9.0/Covenant.dll').read_bytes()).hexdigest().upper()
cards=read(root/'Data/cards.json');coverage=read(root/'audits/0.5/revision-coverage.json')
installed=Path(r'F:/SteamLibrary/steamapps/common/Slay the Spire 2/mods/Covenant/Covenant.json')
paths={'mechanisms':'audits/0.5/revision-native.json','closing':'audits/0.5/revision-closing-native.json','ui':'audits/0.5/layout-native.json','reward':'audits/reward-runtime.json','save':'audits/save-runtime.json','merchant':'audits/merchant-runtime.json','extras':'audits/0.5/release-extras-native.json','route':'audits/0.5/climb/climb-runtime.json'}
proofs={}
for name,path in paths.items():
 if not (root/path).exists():proofs[name]={'status':'pending'};continue
 record=read(root/path);proofs[name]={'status':record['status'],'matchesCurrentBinary':record.get('modSha256')==sha,'checks':len(record.get('checks',[])),'report':path}
remaining=[name for name,p in proofs.items() if p['status']!='pass' or not p.get('matchesCurrentBinary')]
installed_sha=hashlib.sha256(installed.with_name('Covenant.dll').read_bytes()).hexdigest().upper() if installed.with_name('Covenant.dll').exists() else None
status={'status':'verification-in-progress' if remaining else 'released' if installed_sha==sha else 'verified-awaiting-delivery','version':read(root/'Covenant.json')['version'],'installedVersion':read(installed)['version'] if installed.exists() else None,'modSha256':sha,'compiled':coverage['compiled'],'pendingConversions':coverage['pending'],'productionCardsWithRevision':sum(bool(c['trait'].get('revision')) for c in cards),'proofs':proofs,'remaining':remaining}
(root/'audits/0.5/work-status.json').write_text(json.dumps(status,ensure_ascii=False,indent=2)+'\n','utf-8')
print(json.dumps(status,ensure_ascii=False))
