"""One focused trait per card, informed by the exported native Stronghold trait table.

Repeated simple roles are allowed. Do not allocate extra abilities just to manufacture
unique signatures. These programs REPLACE, rather than supplement, the legacy trait.
"""
def effect(trigger,kind,amount=1,**kw):
    upgrade=kw.pop('upgradeAmount',0 if kind in {'retain','upgrade','recycle','shiv','soul','frost','lightning'} else 1)
    return dict(trigger=trigger,kind=kind,amount=amount,upgradeAmount=upgrade,**kw)

def design(rows,source_root):
    units={'Ember':3,'Poison':2,'Blades':2,'Sleight':3,'Resonance':2,'Overload':2,'Zero':3,'Stars':2,'Creation':3,'Doom':2,'Bones':3,'Ethereal':2,'Arrows':2,'Emotion':2}
    result={}
    own_map={'play':'own_play','draw':'own_draw','self_exhaust':'own_exhaust'}
    for row in rows:
        name=row['className'];src=(source_root/'MegaCrit.Sts2.Core.Models.Cards'/f'{name}.cs').read_text('utf-8-sig');old=row['trait'];bond=old['bond']
        trigger=own_map.get(old['trigger'],old['trigger'])
        if row['type']=='Power':trigger='attached_turn'
        if 'CardKeyword.Ethereal' in src:trigger='own_exhaust'
        if row['type']=='Quest':trigger='victory'
        direct=trigger in {'own_play','own_draw','own_exhaust','victory'}
        amount=(2 if row['rarity'] in {'Rare','Ancient','Event'} else 1)*(units.get(bond,1) if direct else 1)
        main=effect(trigger,'grow',amount,bond=bond,requireActive=True)
        main['upgradeAmount']=units.get(bond,1) if direct else 1
        if row['type']=='Attack' and bond=='Solitude':main=effect('own_play','vigor',3)
        if row['type']=='Skill' and bond=='Solitude':main=effect('own_play','block',3)
        if row['type']=='Power':
            roles={
                'Fury':effect('attached_turn','vigor',2),'Blood':effect('attached_turn','heal',1),
                'Ember':effect('exhaust','vigor',1),'Poison':effect('poison','block',2),
                'Blades':effect('generated_attack','block',2),'Sleight':effect('discard','enhance',2,filter='Attack'),
                'Resonance':effect('attached_turn','focus',1),'Overload':effect('evoke','aoe',2),
                'Zero':effect('attached_turn','discount',1,filter='Skill'),'Stars':effect('attached_turn','stars',1),
                'Forge':effect('forge','enhance',3,filter='name:SovereignBlade'),
                'Creation':effect('attached_passive','growth_aura',1,filter='generated'),
                'Doom':effect('doom','block',2),'Bones':effect('summon','block',2),
                'Ethereal':effect('attached_turn','block',3),'Arrows':effect('arrow','vigor',2),
                'Melody':effect('melody','enhance',2,filter='Attack'),'Emotion':effect('emotion','block',2),
                'Ritual':effect('attached_turn','discount',1,filter='Power'),
            }
            main=roles.get(bond,main)
        elif row['type']=='Attack' and bond=='Solitude':main=effect('passive','self_damage',2,bond=bond)
        elif row['type']=='Skill' and 'BlockVar' in src and 'Draw' in src:main=effect('own_play','enhance',3,filter='Attack')
        elif row['type']=='Attack' and 'TargetingAllOpponents' in src:main=effect('own_play','block',3)
        elif row['type']=='Skill' and ('GainEnergy' in src or 'EnergyVar' in src):main=effect('own_play','boost_next',1,filter='heavy')
        if 'ColorlessCardPool' in row['pools'] or row['rarity'] in {'Ancient','Event'}:
            main=effect('own_acquire','grow_memberships',3,scope='all')
        result[name]=dict(effects=[main],rationale='A single role tied to the card native mechanic; replaces legacy growth and attached abilities.',sourceSha256=row['sourceSha256'])
    single={
      **{f'Strike{role}':effect('own_play','grow',1,bond='Strike',requireActive=True) for role in ['Ironclad','Silent','Defect','Regent','Necrobinder','Fool']},
      **{f'Defend{role}':effect('own_play','grow',1,bond='Guard') for role in ['Ironclad','Silent','Defect','Regent','Necrobinder','Fool']},
      'Anger':effect('own_play','grow',1,bond='Fury'),
      'Bash':effect('own_play','enhance',5,filter='Attack'),
      'Armaments':effect('own_play','grow',2,bond='Mastery'),
      'BodySlam':effect('own_play','grow',2,bond='Guard',condition='blocked'),
      'BattleTrance':effect('own_play','enhance',4,filter='Attack'),
      'Bloodletting':effect('own_play','block',5),
      'Offering':effect('own_exhaust','grow',2,bond='Blood'),
      'TrueGrit':effect('exhaust','grow',1,bond='Ember',requireActive=True),
      'BurningPact':effect('own_play','grow_other',2,filter='exhaust'),
      'SecondWind':effect('own_play','vigor',4),
      'FiendFire':effect('own_exhaust','grow',6,bond='Ember',requireActive=True),
      'FeelNoPain':effect('exhaust','block',1),
      'DarkEmbrace':effect('attached_passive','growth_aura',1,filter='exhaust'),
      'Corruption':effect('exhaust','grow',1,bond='Ember',requireActive=True),
      'DemonForm':effect('attached_turn','grow',1,bond='Fury',requireActive=True),
      'Rupture':effect('self_loss','grow',1,bond='Blood',requireActive=True),
      'FightMe':effect('own_play','block',8),
      'Impervious':effect('own_exhaust','grow',2,bond='Guard'),
      'Claw':effect('own_play','grow',3,bond='Zero',requireActive=True),
      'Zap':effect('own_play','grow',2,bond='Resonance'),
      'Dualcast':effect('own_play','grow',2,bond='Overload'),
      'MultiCast':effect('own_play','block',5,condition='paid_two'),
      'Hologram':effect('own_play','grow_other',2,filter='zero'),
      'AllForOne':effect('passive','growth_aura',1,filter='zero'),
      'BeamCell':effect('own_play','enhance',4,filter='Attack'),
      'GoForTheEyes':effect('own_play','grow',3,bond='Zero',condition='weak_enemy'),
      'Turbo':effect('own_play','purge'),
      'ChargeBattery':effect('own_play','retain',filter='Power'),
      'Coolheaded':effect('own_play','grow_other',1,filter='Resonance'),
      'ColdSnap':effect('own_play','block',3),
      'Glacier':effect('own_play','plating',2),
      'Defragment':effect('attached_turn','grow',1,bond='Resonance',requireActive=True),
      'Capacitor':effect('attached_turn','grow',1,bond='Overload',requireActive=True),
      'Loop':effect('channel','grow',1,bond='Resonance',requireActive=True),
      'MachineLearning':effect('attached_turn','draw',1),
      'EchoForm':effect('attached_passive','growth_aura',1,filter='Power'),
      'CreativeAi':effect('attached_passive','growth_aura',1,filter='generated'),
      'WhiteNoise':effect('own_exhaust','grow',3,bond='Creation'),
      'DeadlyPoison':effect('own_play','grow',2,bond='Poison'),
      'PoisonedStab':effect('own_play','vigor',3,condition='poisoned_enemy'),
      'BouncingFlask':effect('passive','growth_aura',1,filter='Poison',requireActive=True,bond='Poison'),
      'CorrosiveWave':effect('own_play','weak',1,condition='poisoned_enemy'),
      'NoxiousFumes':effect('poison','grow',1,bond='Poison',requireActive=True),
      'Envenom':effect('attached_passive','growth_aura',1,filter='Poison'),
      'Accelerant':effect('poison','block',2),
      'Snakebite':effect('own_exhaust','grow',4,bond='Poison'),
      'BladeDance':effect('own_play','grow_other',2,filter='generated'),
      'CloakAndDagger':effect('own_play','retain',filter='generated'),
      'InfiniteBlades':effect('attached_passive','growth_aura',1,filter='name:Shiv'),
      'Accuracy':effect('generated_attack','grow',1,bond='Blades',requireActive=True),
      'PhantomBlades':effect('generated_attack','block',2),
      'KnifeTrap':effect('own_exhaust','aoe',4),
      'StormOfSteel':effect('discard','grow',1,bond='Blades',requireActive=True),
      'Shiv':effect('own_play','grow',2,bond='Blades',requireActive=True),
      'Acrobatics':effect('own_play','grow',3,bond='Sleight'),
      'Prepared':effect('own_play','retain',filter='Attack'),
      'CalculatedGamble':effect('own_exhaust','grow',2,bond='Chain'),
      'Reflex':effect('own_discard','grow',3,bond='Sleight'),
      'Tactician':effect('own_discard','enhance',5,filter='Attack'),
      'ToolsOfTheTrade':effect('discard','grow',1,bond='Sleight',requireActive=True),
      'Afterimage':effect('any_play','grow',1,bond='Chain',requireActive=True),
      'Nightmare':effect('own_play','boost_next',3,filter='generated'),
      'Furnace':effect('forge','grow',1,bond='Forge',requireActive=True),
      'Parry':effect('attached_turn','recycle',filter='name:SovereignBlade'),
      'RefineBlade':effect('own_play','enhance',4,filter='name:SovereignBlade'),
      'SovereignBlade':effect('own_play','grow',1,bond='Forge',requireActive=True),
      'Glow':effect('own_play','grow',2,bond='Stars'),
      'Glimmer':effect('own_play','grow_other',2,filter='Stars'),
      'Genesis':effect('attached_turn','grow',1,bond='Stars',requireActive=True),
      'BlackHole':effect('stars','grow',1,bond='Stars',requireActive=True),
      'Bodyguard':effect('own_play','block',4),
      'Poke':effect('own_play','grow',3,bond='Bones'),
      'Calcify':effect('summon','grow',1,bond='Bones',requireActive=True),
      'DeathsDoor':effect('own_play','block',4,condition='low_hp'),
      'Deathbringer':effect('own_play','weak',1),
      'Countdown':effect('doom','grow',1,bond='Doom',requireActive=True),
      'Eidolon':effect('own_exhaust','block',6),
      'SpiritOfAsh':effect('ethereal','grow',1,bond='Ethereal',requireActive=True),
      'Soulbound':effect('attached_passive','growth_aura',1,filter='name:Soul'),
      'Soul':effect('own_exhaust','grow',3,bond='Ember'),
      'FoolishShot':effect('own_play','grow',2,bond='Arrows'),
      'Nail':effect('own_exhaust','grow',2,bond='Arrows'),
      'Recall':effect('own_play','boost_next',2,filter='Arrows'),
      'Motif':effect('melody','grow',1,bond='Melody',requireActive=True),
      'LostLute':effect('attached_passive','growth_aura',1,filter='Melody'),
      'WahChoKi':effect('own_play','grow',3,bond='Melody'),
      'ChoBarbs':effect('own_play','thorns',2),
      'Joy':effect('own_play','grow',2,bond='Emotion'),
      'Sadness':effect('own_exhaust','block',3),
      'AllIsWell':effect('attached_passive','growth_aura',1,filter='Emotion'),
      'HandOfGreed':effect('victory','gold',4),
      'Royalties':effect('passive','shop_discount',2,bond='Profit',scope='outside'),
      'Largesse':effect('passive','shop_discount',1,bond='Profit',scope='outside'),
      'Pillage':effect('victory','gold',3),
      'MyShinies':effect('victory','gold',3),
      'LanternKey':effect('passive','shop_discount',1,bond='Encounter',scope='outside'),
      'Dowsing':effect('victory','grow',2,bond='Foresight'),
      'SpoilsMap':effect('victory','gold',5),
      'ByrdonisEgg':effect('victory','heal',2),
      'SecretTechnique':effect('own_play','boost_next',2,filter='Skill'),
      'SecretWeapon':effect('own_play','boost_next',2,filter='Attack'),
      'Apotheosis':effect('own_play','grow',3,bond='Mastery'),
      'Entropy':effect('attached_passive','growth_aura',1,filter='generated'),
      'Blur':effect('own_play','retain',filter='Skill'),
      'UltimateDefend':effect('own_play','plating',2),
      'Yoga':effect('own_play','enhance',3,filter='Melody'),
      'Void':effect('own_exhaust','energy',1),
      'Wound':effect('own_exhaust','block',3),
      'Burn':effect('own_exhaust','aoe',3),
      'Dazed':effect('own_exhaust','grow',1,bond='Status'),
      'Slimed':effect('own_exhaust','draw',1),
      'AscendersBane':effect('own_exhaust','grow',2,bond='Curse'),
      'Regret':effect('own_exhaust','heal',1),
      'Normality':effect('own_exhaust','draw',1),
      'Debt':effect('own_exhaust','gold',3),
      'FlashOfSteel':effect('own_acquire','grow_memberships',3,scope='all'),
      'Finesse':effect('own_acquire','grow_memberships',3,scope='all'),
      'GoldAxe':effect('passive','shop_discount',2,bond='Profit',scope='outside'),
      'Clumsy':effect('own_remove','grow',4,bond='Curse',scope='all'),
      'Writhe':effect('own_remove','gold',5,scope='all'),
    }
    for name,main in single.items():
        if name not in result:raise ValueError('Unknown authored card '+name)
        result[name]['effects']=[main];result[name]['rationale']='One explicitly authored growth, support, battle, circulation or economic role.'
    # Two deliberately paired exceptions, both already identified as weak-card compensation.
    result['NeutronAegis']['effects']=[effect('own_play','grow',12,bond='Stars'),effect('own_play','aegis_block',6,bond='Stars',upgradeAmount=0)]
    result['KnockoutBlow']['effects']=[effect('own_play','grow',6,bond='Stars'),effect('own_play','stars',2)]
    for design in result.values():
        for item in design['effects']:
            if item['kind']=='grow' and item['trigger'] in {'own_play','own_draw','own_exhaust','victory'}:item['upgradeAmount']=units.get(item['bond'],1)
    return result
