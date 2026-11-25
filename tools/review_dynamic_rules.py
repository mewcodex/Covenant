"""Record the remaining symbolic membership rules alongside the explicit card review.

This is a source-review index, not a substitute for the native behavior checks.
"""
import hashlib, json, pathlib
from catalog_semantics import REVIEWED_MEMBERSHIPS

ROOT=pathlib.Path(__file__).resolve().parents[1]
SOURCE=ROOT.parent/'proj/113.1'
MODEL='MegaCrit.Sts2.Core.Models/CardModel.cs'
ENERGY='MegaCrit.Sts2.Core.Entities.Cards/CardEnergyCost.cs'
TYPE='MegaCrit.Sts2.Core.Entities.Cards/CardType.cs'
RARITY='MegaCrit.Sts2.Core.Entities.Cards/CardRarity.cs'
DYNAMIC={
    'Zero': ('读取当前实例的原始能量费用，排除X费；另计5个明确支持组件。升级改变原始费用，临时减费不改变。',[ENERGY,MODEL]),
    'Heavy': ('读取当前实例的原始能量费用至少为2，排除X费。实际支付至少2能量的收益与组件归属分别判定。',[ENERGY,MODEL]),
    'Retain': ('读取ShouldRetainThisTurn，并计入7个明确保留支持组件。',[MODEL]),
    'Ritual': ('读取当前实例的CardType.Power，能力牌离场后不留在四堆计数中。',[MODEL,TYPE]),
    'Mastery': ('读取IsUpgraded；战斗临时升级只改变战斗副本，不改变永久牌组。',[MODEL]),
    'Harmony': ('只计原版Entropy，不把其他变化牌作为调和组件。',['MegaCrit.Sts2.Core.Models.Cards/Entropy.cs']),
    'Ancient': ('读取CardRarity.Ancient，所有原版先古定义均可计入。',[MODEL,RARITY]),
    'Encounter': ('常规六角色、无色、诅咒和状态池之外的原版定义可以计入；战斗临时实例排除。',[MODEL,'MegaCrit.Sts2.Core.Models/CardPoolModel.cs']),
    'Curse': ('读取当前实例的CardType.Curse。',[MODEL,TYPE]),
    'Status': ('读取当前实例的CardType.Status。',[MODEL,TYPE]),
    'Solitude': ('所有牌均计入。牌组有同名非诅咒牌时在战斗中失效，基础牌参与判定。升级不改变同名判定。',[MODEL,TYPE]),
}

def source(path):
    return dict(path=path,sha256=hashlib.sha256((SOURCE/path).read_bytes()).hexdigest())

bonds=[s.strip() for s in (ROOT/'Core/Bond.cs').read_text('utf-8-sig').split('{',1)[1].split('}',1)[0].split(',') if s.strip()]
covered=set(REVIEWED_MEMBERSHIPS)|set(DYNAMIC)
if set(bonds)!=covered:raise RuntimeError(f'Unreviewed or unexpected group: {set(bonds)^covered}')
data=dict(status='source-reviewed',scope='Membership rules only; native gameplay and full-route evidence remain separate.',
    sourceVersion='v0.113.1/d09d48e5',explicitGroups=list(REVIEWED_MEMBERSHIPS),
    symbolicGroups={k:dict(rule=v[0],sources=[source(p) for p in v[1]]) for k,v in DYNAMIC.items()},
    totalGroups=len(covered),explicitDecisions=len(json.loads((ROOT/'Data/semantic-review.json').read_text('utf-8'))),
    authoredSupplements={'Profit':dict(rule='原有4个金币来源／收益组件之外，所有无色池卡牌计入逐利投资组件；不改变其各自特性目标。',sources=[source('MegaCrit.Sts2.Core.Models.CardPools/ColorlessCardPool.cs')])},
    runtimeMembershipSourceSha256=hashlib.sha256((ROOT/'Game/CardMembership.cs').read_bytes()).hexdigest(),
    behavioralEvidence=['foundation-latest.log','combat-runtime.json','reward-runtime.json','save-runtime.json','merchant-runtime.json','climb/climb-runtime.json'])
output=ROOT/'audits/rules-review.json';output.write_text(json.dumps(data,ensure_ascii=False,indent=2)+'\n','utf-8')
print(json.dumps(dict(groups=len(covered),explicitGroups=len(REVIEWED_MEMBERSHIPS),symbolicGroups=len(DYNAMIC),explicitDecisions=data['explicitDecisions']),ensure_ascii=False))
