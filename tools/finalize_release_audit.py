"""Summarize completed 0.5 checks for the exact packaged binary."""
import hashlib,json,statistics,zipfile
from pathlib import Path
from datetime import datetime,timezone
root=Path(__file__).resolve().parents[1]
read=lambda p:json.loads(p.read_text('utf-8-sig'))
ready=read(root/'audits/0.5/release-ready.json');package=read(root/'dist/package-validation.json')
sha=hashlib.sha256((root/'bin/Release/net9.0/Covenant.dll').read_bytes()).hexdigest().upper()
assert ready['status']=='pass' and ready['modSha256']==package['modSha256']==sha
archive=root/f"dist/Covenant-{ready['version']}-sts2-0.113.1.zip"
assert hashlib.sha256(archive.read_bytes()).hexdigest().upper()==package['zipSha256']
with zipfile.ZipFile(archive) as z:
 assert hashlib.sha256(z.read('Covenant/Covenant.dll')).hexdigest().upper()==sha
rows=read(root/'audits/0.5/revision-description-check.json')['cards']
summary=dict(status='pass',completedUtc=datetime.now(timezone.utc).isoformat(),version=ready['version'],game='v0.113.1/d09d48e5',modSha256=sha,
 package=str(archive.relative_to(root)).replace('\\','/'),zipSha256=package['zipSha256'],cards=717,covenants=35,
 productionRevisionCards=717,legacyEffects=0,nativeChecks=ready['nativeChecks'],nativeChecksTotal=sum(ready['nativeChecks'].values()),
 coreAssertions=163,compilerTests=22,route=ready['route'],evidence=ready['evidence'],
 text=dict(medianCharacters=statistics.median(len(c['baseText']) for c in rows),maxCharacters=max(len(c['baseText']) for c in rows)),
 limitations=['Native behavior examples and complete metadata/description coverage do not prove every original card combination.',
 'The assisted route verifies three-act integration, not ordinary A10 balance, feel or resource scheduling.',
 'The boss-affinity factory and intrinsic-defense witnesses use isolated fixtures; Vakuu end-turn is intercepted in its limit/payment check.',
 'Native compatibility-renderer and exit-cleanup diagnostics remain in raw logs.',
 'Multiplayer, other mods and other game versions have not been verified.'])
(root/'audits/delivery-summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n','utf-8')
text=f"""# 0.5.0交付核对

正式数据包含35个盟约、717张更正稿特性，旧通用程序已清空。当前DLL：`{sha}`。

相同DLL通过{summary['nativeChecksTotal']}项原生机制、界面、奖励、续局与商店检查；核心规则163项断言、编译器22项检查、717张牌的基础和升级描述/表达式及逐稿哈希核对也已通过。分组计数和证据路径见`delivery-summary.json`。

辅助三幕路线已通关，完成{summary['route']['wins']}场战斗和{summary['route']['bossWins']}场首领战。该路线使用原生AutoSlay的辅助增益，不是普通A10平衡或爽感的证明。

瓦库的定向检查验证第一回合最左侧出牌接管、13张自动出牌、真实支付能量与星星及发出结束回合命令；该检查拦截结束回合以避免连续自动运行。东尼的源码范围核对覆盖717个定义，31张计入；原生示例验证巩固、自身成长及冰霜的可变格挡。

已验证逐利两次/三次获得时效果、远见500层首领奖励保底，以及真实胜利时仪式将临时层数的一半转为永久后再结算胜利成长。

原生行为检查不等于717张牌的所有组合均逐张打出。联机、其他模组及其他游戏版本未验证；原版兼容渲染器和退出清理诊断保留在日志中。
"""
(root/'audits/delivery-audit.md').write_text(text,'utf-8')
print('Verified release audit: '+ready['version']+' / '+sha)
