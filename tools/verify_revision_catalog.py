"""Check that shipped data is the reviewed 0.5 program, with no second legacy effect path."""
import collections
import hashlib
import json
import re
from pathlib import Path
from compile_revision05 import EMBER_EXCLUDED, HEAVY_EXCLUDED, MARKER_CARDS, PROFIT_EXCLUDED, REASSIGNED_HEAVY_TRAITS, TRAIT_BOND_MEMBERS, ZERO_EXCLUDED, build
from approved_traits import AID_MEMBERS
from catalog_exhaust_discard import ember_category_excluded
from revision_reflow import HEAVY_MEMBERS, REFLOW_MEMBERS

root=Path(__file__).resolve().parents[1]
read=lambda p:json.loads(p.read_text('utf-8-sig'))
cards=read(root/'Data/cards.json')
programs=read(root/'audits/0.5/compiled-revisions.json')
proposal={c['card']:c for c in read(root/'proposals/0.5/card-revisions.json')['cards']}
reviewed,status=build(write_artifacts=False)
assert status['pending']==0,status['cardsPending']
assert len(cards)==len(programs)==len(proposal)
assert {c['className'] for c in cards}==set(programs)==set(proposal)
assert ZERO_EXCLUDED=={c['className'] for c in cards if c.get('zeroExcluded')}
ember_excluded=EMBER_EXCLUDED|{c['className'] for c in cards if ember_category_excluded(c)}
assert ember_excluded=={c['className'] for c in cards if c.get('emberExcluded')}
assert PROFIT_EXCLUDED=={c['className'] for c in cards if c.get('profitExcluded')}
assert AID_MEMBERS=={c['className'] for c in cards if 'Aid' in c['bonds']}
assert HEAVY_MEMBERS=={c['className'] for c in cards if 'Heavy' in c['bonds']}
assert REFLOW_MEMBERS=={c['className'] for c in cards if 'Reflow' in c['bonds']}
assert len(HEAVY_EXCLUDED)==24 and HEAVY_EXCLUDED=={c['className'] for c in cards if c.get('heavyExcluded')}
assert HEAVY_EXCLUDED.isdisjoint(TRAIT_BOND_MEMBERS)
assert TRAIT_BOND_MEMBERS=={c['className'] for c in cards if c.get('traitBondMember')}
assert all('Ember' not in c['bonds'] for c in cards if c['className'] in ember_excluded)
actions=set(re.findall(r'case "([a-z_]+)"', (root/'Game/CovenantTerminal.Revisions.cs').read_text('utf-8')))|{'modifier','redesign'}
counts=collections.Counter()
for card in cards:
    name=card['className'];trait=card['trait'];revision=trait['revision']
    assert 'Solitude' not in card['bonds'] and trait['bond']!='Solitude',name
    if name in TRAIT_BOND_MEMBERS:
        assert trait['bond'] in card['bonds'],name
    if name in HEAVY_EXCLUDED:
        assert 'Heavy' not in card['bonds'],name
    if name in REASSIGNED_HEAVY_TRAITS:
        assert trait['bond']==REASSIGNED_HEAVY_TRAITS[name] and trait['bond'] in card['bonds'],name
    if name in {'Armaments', 'TrueGrit'}:
        assert 'Foresight' in card['bonds'] and card.get('foresightRequiresUpgrade') is None,name
    if name in ZERO_EXCLUDED:
        assert 'Zero' not in card['bonds'] and all(rule['bond']!='Zero' for stage in ('base','upgraded') for rule in revision[stage]['rules']),name
    if name in PROFIT_EXCLUDED:
        assert 'Profit' not in card['bonds'] and all(rule['bond']!='Profit' for stage in ('base','upgraded') for rule in revision[stage]['rules']),name
    if ember_category_excluded(card):
        assert all(rule['bond']!='Ember' for stage in ('base','upgraded') for rule in revision[stage]['rules']),name
    assert trait['trigger']=='revision' and trait['amount']==trait['upgradeAmount']==trait['limit']==0 and trait['effects']==[],name
    assert revision==programs[name],name
    assert revision==reviewed[name],f'Shipped program does not match reviewed sources: {name}'
    variants=revision.get('variants')
    if variants:
        assert not card['bonds'] and name=='MadScience' and len(variants)==9,name
        assert all(revision[level]['text'] and not revision[level]['rules'] for level in ('base','upgraded')),name
    for selected in variants.values() if variants else (revision,):
        for level in ('base','upgraded'):
            program=selected[level]
            if name in MARKER_CARDS:
                assert not program['text'] and not program['rules'] and not card['bonds'],name
            else:
                assert program['text'] and program['rules'],name
            for rule in program['rules']:
                assert rule['action'] in actions,(name,rule)
                assert rule['bond'] not in {'Ancient','Solitude'} and rule['action']!='temporary' and rule.get('lifetime')!='next_turn',(name,rule)
                counts[rule['event']]+=1
secondary=read(root/'Data/secondary-covenants.json')
assert len(secondary['vakuu'])==10
for entries in secondary.values():
    names=[x['card'] for x in entries]
    assert len(set(names))==len(names) and set(names)<=set(programs)
proof=read(root/'audits/0.5/revision-description-check.json')
assert proof['status']=='pass' and len(proof['cards'])==len(cards)
sha=hashlib.sha256((root/'audits/0.5/compiled-revisions.json').read_bytes()).hexdigest().upper()
assert proof['stagedSha256']==sha
report=dict(status='pass',cards=len(cards),productionCatalogApplied=True,stagedSha256=sha,
            legacyEffects=0,secondaryMembers={k:len(v) for k,v in secondary.items()},events=dict(counts),
            scope='Exact program match with the proposal and later reviewed compiler rules; native behavior coverage is recorded separately.')
(root/'audits/0.5/catalog-integrity.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n','utf-8')
print(f'REVISION_CATALOG_OK cards={len(cards)} legacyEffects=0')
