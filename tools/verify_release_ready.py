"""Release gate: every required native proof must describe this exact production binary."""
import hashlib
import json
from datetime import datetime,timezone
from pathlib import Path
from compile_revision05 import BONDS

root=Path(__file__).resolve().parents[1]
read=lambda p:json.loads(p.read_text('utf-8-sig'))
sha=hashlib.sha256((root/'bin/Release/net9.0/Covenant.dll').read_bytes()).hexdigest().upper()
native=Path('F:/SteamLibrary/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64/sts2.dll')
assert hashlib.sha256(native.read_bytes()).hexdigest().upper()=='62ECBE91093E1FA862DE4329AAC865209DCB297804CAF0F0520D92D8C0B4A9C3','Native game changed since verification'
paths={
    'mechanisms':'audits/0.5/revision-native.json',
    'closing':'audits/0.5/revision-closing-native.json',
    'ui':'audits/0.5/layout-native.json',
    'rewards':'audits/reward-runtime.json',
    'save':'audits/save-runtime.json',
    'merchant':'audits/merchant-runtime.json',
    'extras':'audits/0.5/release-extras-native.json',
    'route':'audits/0.5/climb/climb-runtime.json',
}
records={}
for name,path in paths.items():
    record=read(root/path)
    assert record.get('status')=='pass' and record.get('modSha256')==sha,f'Incomplete or different binary: {name}'
    records[name]=record
assert records['mechanisms']['productionCatalogApplied'] and records['mechanisms']['stagedPrograms']==717
assert records['closing']['productionCatalogApplied']
assert records['route']['outcome']=='victory' and records['route']['bossWins']>=3
assert len({r['Act'] for r in records['route']['rooms']})>=3 and all(r['AllCardPools'] for r in records['route']['rooms'])
catalog=read(root/'audits/0.5/catalog-integrity.json')
assert catalog['status']=='pass' and catalog['productionCatalogApplied'] and catalog['legacyEffects']==0
assert catalog['stagedSha256']==records['mechanisms']['stagedSha256']
foundation=read(root/'audits/0.5/foundation.json')
assert foundation['status']=='pass' and foundation['coreAssertions']==163 and foundation['compilerTests']==22
for path,digest in foundation['sourceHashes'].items():
    assert hashlib.sha256((root/path).read_bytes()).hexdigest()==digest,f'Foundation input changed: {path}'
cards=read(root/'Data/cards.json')
assert len(cards)==717 and all(c['trait'].get('revision') and c['trait']['effects']==[] for c in cards)
report=dict(status='pass',completedUtc=datetime.now(timezone.utc).isoformat(),version=read(root/'Covenant.json')['version'],
            modSha256=sha,cards=717,covenants=len(BONDS),nativeChecks={name:len(record.get('checks',[])) for name,record in records.items() if name!='route'},
            evidence=paths,foundation=foundation,route=dict(outcome=records['route']['outcome'],wins=records['route']['wins'],bossWins=records['route']['bossWins']))
(root/'audits/0.5/release-ready.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
print('COVENANT_RELEASE_READY '+sha)
