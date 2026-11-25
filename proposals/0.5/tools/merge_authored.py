import json,os,re,sys,collections,glob
ROOT=sys.argv[1];OUT=os.path.join(ROOT,'proposals','0.5')
C={r['className']:r for r in json.load(open(os.path.join(ROOT,'Data/cards.json'),encoding='utf-8'))}
CAT={c['card']:c for c in json.load(open(os.path.join(ROOT,'audits/single-trait-catalog.json'),encoding='utf-8'))['cards']}
A={}
for f in sorted(glob.glob(os.path.join(OUT,'authored','*.txt'))):
    for ln in open(f,encoding='utf-8'):
        ln=ln.strip()
        if not ln or ln.startswith('#'):continue
        n,k,t=ln.split('|',2)
        assert n in C,(f,n); assert n not in A,('dup',n); assert k in 'PT-',ln
        A[n]=(k,t,os.path.basename(f)[:-4])
missing=[n for n in C if n not in A];print('cards',len(A),'missing',missing)
bad=[]
for n,(k,t,_) in A.items():
    temp='临时增加' in t; perm=bool(re.search(r'(?<!临时)增加\d+层|增加等于.*层数|使【[^】]+】增加',t)) and re.search(r'】增加',t) is not None
    if k=='T' and not temp:bad.append((n,'T无临时',t))
    if k=='P' and ('临时增加' in t or '增加' not in t):bad.append((n,'P无永久',t))
    if k=='-' and '层。' in t and '增加' in t and '】临时' in t:bad.append((n,'-含成长',t))
print('flag mismatches',len(bad));[print(' ',b) for b in bad[:20]]
dup=collections.defaultdict(list)
for n,(k,t,_) in A.items():dup[t].append(n)
dups={t:v for t,v in dup.items() if len(v)>1};print('identical texts',len(dups));[print(' ',v,t) for t,v in dups.items()]
def role(k,t):
    if k in 'PT':return '成长'
    if re.search(r'每\d档|A/|A点|A乘|2A|等于A',t):return '层数兑现'
    if '金币' in t:return '经济'
    if re.search(r'能量消耗减少|抽\d张|额外抽',t):return '运转'
    return '攻防与支援'
VAKUU=set(open(os.path.join(OUT,'tools','vakuu_cards.txt'),encoding='utf-8').read().split())
assert VAKUU<=set(C),VAKUU-set(C)
rows=[]
for n in sorted(A):
    k,t,g=A[n];c=C[n]
    rows.append(dict(card=n,name=CAT[n]['name'],group=g,bond=c['trait']['bond'],rarity=c['rarity'],type=c['type'],cost=c['cost'],
        layer={'P':'永久','T':'临时','-':'无'}[k],vakuu=n in VAKUU,role=role(k,t),current=CAT[n]['text'],proposed=t))
print(collections.Counter(r['role'] for r in rows),collections.Counter(r['layer'] for r in rows))
json.dump(dict(note='0.5 逐卡定稿提案。layer 为该特性给予的层数类型；A 指文本所写盟约的当前档位。',cards=rows),open(os.path.join(OUT,'card-revisions.json'),'w',encoding='utf-8'),ensure_ascii=False,indent=1)
GZ=dict(ironclad='铁甲',silent='静默',defect='机器人',regent='储君',necrobinder='死灵',fool='愚者',colorless='无色',other='先古、事件、衍生、诅咒与状态')
RZ={'Basic':'基础','Common':'普通','Uncommon':'罕见','Rare':'稀有','Ancient':'先古','Event':'事件','Quest':'任务','Token':'衍生','Status':'状态','Curse':'诅咒'}
TZ={'Attack':'攻击','Skill':'技能','Power':'能力','Status':'状态','Curse':'诅咒','Quest':'任务'}
L=['# 逐卡修订表（0.5 提案，逐张设计）','',f'共 {len(rows)} 张，每张对照原版卡牌效果单独设计。文中的 A 指文本所写盟约的当前档位，游戏内显示为算好的数值。','',
   '| 职责 | 张数 |','|---|---|']+[f'| {k} | {v} |' for k,v in collections.Counter(r['role'] for r in rows).most_common()]+['',
   '| 层数类型 | 张数 |','|---|---|']+[f'| {k} | {v} |' for k,v in collections.Counter(r['layer'] for r in rows).most_common()]+['']
for g in GZ:
    rs=[r for r in rows if r['group']==g]
    L+=[f'## {GZ[g]}（{len(rs)} 张）','','| 牌 | 稀有度·类型·费用 | 0.4.0 特性 | 修订后特性 | 层数 | 职责 |','|---|---|---|---|---|---|']
    for r in sorted(rs,key=lambda r:r['name']):
        cost='X' if r['cost']<0 and r['type'] in('Attack','Skill') else '' if r['cost']<0 else str(r['cost'])
        L.append(f"| {r['name']} | {RZ[r['rarity']]}·{TZ.get(r['type'],r['type'])}·{cost} | {r['current']} | {r['proposed']} | {r['layer']} | {r['role']} |")
    L.append('')
open(os.path.join(OUT,'逐卡修订表.md'),'w',encoding='utf-8').write('\n'.join(L))
# review list: permanent growth on cards that look repeatable
def sx(c):return str(c['evidence'].get('Ember','')).startswith('原版定义具有消耗关键词')
rev=[(CAT[n]['name'],A[n][1]) for n in A if A[n][0]=='P' and C[n]['type'] in('Attack','Skill') and not sx(C[n]) and 0<=C[n]['cost']<3 and C[n]['rarity'] not in('Token',)]
print('P on repeatable-looking cards',len(rev));[print('  ',a,'|',b) for a,b in rev]
