# 一次性转换：层数刻度 ×2，并为每条特性补升级后的数值。括号内为升级后数值。
import glob,re,sys,collections
def up(n):                       # 约 +25%～+50%
    return max(n+1,round(n*1.35))
RELAX={  # 升级改为放宽条件，不再提高层数：(原文片段, 带升级标注的片段)
'Midnight':('不高于3','不高于3（4）'),'BodySlam':('不少于20点','不少于20（15）点'),'DemonicShield':('不少于10点','不少于10（8）点'),
'SecondWind':('至少3张','至少3（2）张'),'PactsEnd':('每有3张牌','每有3（2）张牌'),'CalculatedGamble':('每丢弃3张牌','每丢弃3（2）张牌'),
'Scrawl':('每抽到3张牌','每抽到3（2）张牌'),'Expose':('每移除10点格挡','每移除10（7）点格挡'),'Voltaic':('每生成3个','每生成3（2）个'),
'Mirage':('每获得10点格挡','每获得10（7）点格挡'),'Prolong':('每有10点格挡','每有10（7）点格挡'),'Barricade':('每有10点格挡','每有10（7）点格挡'),
'Sacrifice':('每有10点','每有10（7）点'),'Unleash':('每有5点','每有5（4）点'),'Protector':('每有5点','每有5（4）点'),
'Afterimage':('每满5点','每满5（4）点'),'Unmovable':('每翻倍出5点','每翻倍出5（4）点'),'Entrench':('每因此多获得10点','每因此多获得10（7）点'),
'Stack':('每获得5点','每获得5（4）点'),'BlightStrike':('每因此给予5层灾厄','每因此给予5（4）层灾厄'),'NoEscape':('每因此额外给予5层灾厄','每因此额外给予5（4）层灾厄'),
'ReaperForm':('每给予10层','每给予10（7）层'),'Outbreak':('至少10点生命','至少10（7）点生命'),'Apotheosis':('每因此升级3张牌','每因此升级3（2）张牌'),
'SovereignBlade':('每有10点','每有10（7）点'),'Yoga':('每获得3点拉伸','每获得3（2）点拉伸'),'ChoKi':('每因此重新获得3点拉伸','每因此重新获得3（2）点拉伸'),
'BeaconOfHope':('首次达到15点','首次达到15（12）点'),'PanicButton':('不低于20点','不低于20（15）点'),'KinglyKick':('不高于2','不高于2（3）'),
'Melancholy':('不高于1','不高于1（2）'),'Enrapture':None}
ONCE={'KnowThyPlace','Chill','Prolong','Rainbow','Voltaic','Fusion','Transfigure','Underworld','Ignition','Mimic'}  # 升级移除消耗且给永久层数
stat=collections.Counter()
def conv(n,k,t):
    relax=RELAX.get(n)
    if relax:
        assert relax[0] in t,(n,t);t=t.replace(relax[0],relax[1],1)
    def lay(m):
        b=int(m.group(1))*2
        return f'增加{b}层' if relax else f'增加{b}（{up(b)}）层'
    t2=re.sub(r'增加(\d+)层',lay,t)
    t2=re.sub(r'增加等于(.+?)的层数',(lambda m:f'增加等于{m.group(1)}两倍的层数') if relax else (lambda m:f'增加等于{m.group(1)}两倍（三倍）的层数'),t2)
    if t2!=t or relax:
        stat['放宽条件' if relax else '层数提高']+=1
        if n in ONCE:
            assert t2.startswith(('打出时','被它')),(n,t2)
            t2=('每场战斗首次' if t2.startswith('打出时') else '每场战斗中，')+t2;stat['改为每场首次']+=1
        return t2
    # 不给层数的特性
    def bump(m):
        b=int(m.group(2));return f'{m.group(1)}{b}（{b+1 if b<6 else b+2}）{m.group(3)}'
    t2=re.sub(r'(额外造成|额外获得|获得|增加|造成|降低|回复)(\d+)(点|金币)',bump,t,count=1) if re.search(r'每\d档|金币|点格挡|点伤害|增加\d点|点生命',t) and not re.search(r'A/|提高A|增加A点|等于A',t) else t
    if t2!=t:stat['基础值提高']+=1;return t2
    def coef(m):
        k=int(m.group(1));return f'A/{k}（A/{k-1}）' if k>=3 else 'A/2（2A/3）'
    t2=re.sub(r'A/(\d)',coef,t,count=1)
    if t2==t:t2=re.sub(r'(?<![/\d（])2A(?!/)','2A（3A）',t,count=1)
    if t2==t:t2=re.sub(r'(?<![/\d（])A(?=点|乘|的|（)','A（1.5A）',t,count=1)
    if t2!=t:stat['系数提高']+=1;return t2
    stat['升级不变']+=1;return t
unchanged=[]
for f in sorted(glob.glob('authored/*.txt')):
    out=[]
    for ln in open(f,encoding='utf-8').read().split('\n'):
        if not ln.strip() or ln.startswith('#'):out.append(ln);continue
        n,k,t=ln.split('|',2);assert '（' not in t.replace('（【','').replace('】）','') or '（【' in t,ln
        t2=conv(n,k,t)
        if t2==t:unchanged.append((n,t))
        out.append(f'{n}|{k}|{t2}')
    open(f,'w',encoding='utf-8',newline='\n').write('\n'.join(out))
print(stat);[print(' ',u) for u in unchanged]
