"""The approved additions to the native card reworks."""
import hashlib

E = '{energyPrefix:energyIcons(1)}'
GUILTY = f'你可以花费{E}打出此牌，抽2张牌，按牌面计数增加【咒缚】层数，然后使剩余战斗数增加3并消耗此牌。'
BOMB = '此牌倒数期间，你每花费{energyPrefix:energyIcons(3)}，就使倒计时缩短1回合。若因此引爆，伤害无视格挡，并使你层数最高的可叠层盟约增加{amount}层。'
EGG = '此牌可以免费打出，使此牌获得1点经验。当前经验为{1}点。'
SWOOP = '获得2点经验，并使1个随机已激活的可叠层盟约增加{0}层。当前经验为{1}点。'
GOD = '获得3点经验，并使至多2个不同的随机已激活可叠层盟约各增加{0}层。当前经验为{1}点。'
FINALE = '抽牌堆有牌时也可以打出此牌。若如此做，将抽牌堆中每张牌随机丢弃或消耗。每因此处理1张牌，有{0}%的概率受到1点伤害。此概率随所有盟约的总层数降低。'
SNAKE = '每打出1张【毒棘】牌，手牌中的此牌下次打出时施加的中毒增加{0}层。回合结束时，有概率对生命值最高的敌人打出此牌。'
BAD_LUCK = '当你即将死亡时，无视永恒将此牌从场上和牌组中移除并将生命恢复至上限。本场战斗结束后，将牌组中随机{0}%的非诅咒牌变化为随机诅咒牌。'
CURSE_PLAYS = {
    'AscendersBane': (0, '获得2点覆甲。消耗此牌。'),
    'Clumsy': (0, '获得3点格挡。随机丢弃1张其他手牌。'),
    'CurseOfTheBell': (2, '获得1层人工制品。'),
    'Debt': (1, '失去5%金币，获得等量的格挡和【咒缚】层数，并消耗此牌。'),
    'Decay': (1, '随机对[jitter][red]任意[/red][/jitter]目标造成{2}点伤害{0}次。'),
    'Doubt': (1, '对所有敌人施加1层虚弱。'),
    'Folly': (0, '抽2张牌并消耗此牌。'),
    'Greed': (1, '从弃牌堆随机取回至多2张诅咒牌'),
    'Injury': (0, '使【咒缚】增加1层。'),
    'Normality': (1, '使手牌中随机1张非X费牌可以免费打出一次。你必须优先打出那张牌。'),
    'Regret': (1, '对所有敌人造成等同于你剩余手牌数量的伤害。'),
    'Shame': (1, '使所有敌人的格挡减半。'),
    'Writhe': (0, '查看抽牌堆顶部的3张牌，选择1张加入手牌，并随机消耗另一张。'),
}
CURSE_GROWTH = {
    'AscendersBane': ('Curse', '消耗此牌时，使已激活的【咒缚】增加2层。', '2', '1'),
    'CurseOfTheBell': ('Curse', '使【咒缚】增加5层。', '5', '1'),
    'Decay': ('Curse', '使【咒缚】增加{1}层。', '3+floor(A/3)', '1'),
    'Folly': ('Curse', '使【咒缚】增加2层。', '2', '1'),
    'Greed': ('Curse', '，每取回1张牌就使【咒缚】增加2层。', '2*returned_Curse', '1'),
    'Injury': ('Curse', '', '1', '1'),
    'Shame': ('Curse', '若减少了格挡，使【咒缚】增加3层。', '3', 'native_shame_loss>0'),
}
CURSE_EXTRA_MEMBERS = {
    'Debt': ('Profit', '特性以金币换取格挡和咒缚层数。'),
    'Doubt': ('Breach', '特性对敌人施加虚弱。'),
    'Normality': ('Chain', '特性决定下一张手动打出的牌。'),
}

# Membership can be shared by support cards without replacing their own authored traits.
AID_MEMBERS = {
    'TheBomb', 'ByrdonisEgg', 'ByrdSwoop', 'GodByrd', 'GrandFinale',
    'MakeItSo', 'Splash', 'TheLovers', 'TheWorld', 'Entropy', 'Deflect', 'Slice',
    'DrainPower', 'Begone', 'Terraforming', 'Headbutt',
}
SPECS = {
    'Guilty': ('Curse', GUILTY, GUILTY, (), ()),
    'TheBomb': ('Aid', BOMB.replace('{amount}', '12'), BOMB.replace('{amount}', '16'), (), ()),
    'ByrdonisEgg': ('Aid', EGG, EGG, ('0', 'experience'), ('0', 'experience')),
    'ByrdSwoop': ('Aid', SWOOP, SWOOP, ('1+floor(min(100,experience+2)/15)', 'experience'), ('2+floor(min(100,experience+2)/14)', 'experience')),
    'GodByrd': ('Aid', GOD, GOD, ('2+floor(min(100,experience+3)/10)', 'experience'), ('4+floor(min(100,experience+3)/9)', 'experience')),
    'GrandFinale': ('Aid', FINALE, FINALE, ('max(0,100-total_layers/20)',), ('max(0,95-total_layers/15)',)),
    'Snakebite': ('Poison', SNAKE, SNAKE, ('1+floor(A/5)',), ('2+floor(A/4)',)),
    'BadLuck': ('Curse', BAD_LUCK, BAD_LUCK, ('min(100,1+L/10)',), ('min(100,1+L/10)',)),
    'PoorSleep': ('Curse', '回合结束时，若此牌被保留，使已激活的【咒缚】增加4层。', '', (), ()),
    'Enthralled': ('Curse', '激活时，获得16点格挡，并使【咒缚】增加16层。', '', (), ()),
    'TheHermit': ('Sleight', '选择诅咒牌时，改为打出并消耗该牌；否则使已激活的【巧变】增加2层。', '选择诅咒牌时，改为打出并消耗该牌；否则使已激活的【巧变】增加3层。', (), ()),
    'SporeMind': ('Curse', '使抽牌堆和弃牌堆中随机{0}张未附魔的牌获得播种。使已激活的【咒缚】增加6层。', '', ('1+floor(A/3)',), ()),
}
SPECS.update({name: ('Curse', '', '', (), ()) for name in CURSE_PLAYS})


def source_hash(name, legacy, rework27):
    if name not in SPECS:
        return rework27.source_hash(name, legacy)
    return hashlib.sha256(repr((SPECS[name], CURSE_PLAYS.get(name), CURSE_GROWTH.get(name))).encode()).hexdigest()


def programs(name, instruction, native_powers):
    bond, base, upgraded, base_amounts, upgrade_amounts = SPECS[name]

    def program(text, amounts):
        scope = 'own' if name in {'TheBomb', 'BadLuck'} else 'hand'
        rules = [instruction('redesign', 'redesign', bond, 0, scope=scope, requireActive=False)]
        if name == 'BadLuck':
            text = '抽到此牌时，使已激活的【咒缚】增加6层。' + text
            rules.append(instruction('draw', 'grow', 'Curse', 6))
        if name in CURSE_PLAYS:
            cost, effect = CURSE_PLAYS[name]
            fee = '免费' if cost == 0 else f'花费{{energyPrefix:energyIcons({cost})}}'
            trigger = '激活且凡庸限制已生效时' if name == 'Normality' else '激活时'
            text += f'{trigger}，你可以{fee}打出此牌，{effect}'
            rules.clear()
            rules.append(instruction('curse_play', 'redesign', 'Curse', cost))
            if name in CURSE_GROWTH:
                growth_bond, growth_text, growth, condition = CURSE_GROWTH[name]
                text += growth_text
                rules.append(instruction('exhaust' if name=='AscendersBane' else 'play', 'grow', growth_bond, growth, condition))
            if name == 'Decay':
                amounts = ('1+A', '3+floor(A/3)', '3+floor(A/2)')
        elif name == 'PoorSleep':
            rules = [instruction('retained', 'grow', 'Curse', 4)]
        elif name == 'TheHermit':
            rules = [instruction('play', 'grow', 'Sleight', 2 if text==base else 3, 'native_hermit_discard>0')]
        elif name in {'Enthralled', 'SporeMind'}:
            rules = [instruction('curse_effect', 'redesign', 'Curse', 0, requireActive=name=='Enthralled'),
                     instruction('play', 'grow', 'Curse', 16 if name=='Enthralled' else 6)]
        data = dict(text=text, nativePowers=native_powers, rules=rules, causalPowers=[])
        if amounts:
            # Each numeric placeholder already uses the same shared description formatter as other reworks.
            while len(rules) < len(amounts):
                rules.append(instruction('redesign', 'redesign', bond, 0, scope=scope, requireActive=name in CURSE_PLAYS))
            data.update(amountText=text, displayAmounts=list(amounts))
            if name == 'GrandFinale':
                data['displayFormats'] = ['0.##']
            elif name == 'BadLuck':
                data['displayFormats'] = ['0.#']
        return data

    if name in {'Enthralled','PoorSleep','SporeMind'}:
        upgraded, upgrade_amounts = base, base_amounts
    return program(base, base_amounts), program(upgraded, upgrade_amounts)
