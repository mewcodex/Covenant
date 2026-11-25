"""Original 1-bit silhouettes. No antialiasing, gradients, borrowed game art or font glyph logos."""
import pathlib
import sys
from PIL import Image,ImageDraw,ImageFont

ROOT=pathlib.Path(__file__).resolve().parents[1];OUT=ROOT/'Assets/Icons';OUT.mkdir(parents=True,exist_ok=True)
NAMES=['Fury','Blood','Ember','Poison','Blades','Sleight','Resonance','Overload','Zero','Stars','Forge','Creation','Doom','Bones','Ethereal','Arrows','Melody','Emotion','Strike','Guard','Chain','Heavy','Retain','Ritual','Mastery','Harmony','Ancient','Encounter','Curse','Status','Profit','Foresight','Solitude','Vakuu','Tony','Aid','Channel']
TITLES=['狂战','血誓','余烬','毒棘','飞刃','巧变','共鸣','过载','零界','星辉','铸王','造物','厄兆','役骨','幽逝','游弋','合奏','悲欢','打击','坚守','连携','重装','延续','仪式','绝技','调和','先古','奇遇','咒缚','残响','逐利','远见','独行','瓦库','东尼','助力','倾注']
NAMES.extend(['Breach','Rotation','Initiative','Fate','Reflow','Oracle','Spectrum'])
TITLES.extend(['破绽','轮转','先机','命运','回流','神喻','万象'])
selected = set(sys.argv[1:])
if selected.difference(NAMES):
 raise ValueError(f'Unknown icons: {selected.difference(NAMES)}')
for n in NAMES:
 if selected and n not in selected:continue
 im=Image.new('1',(32,32));d=ImageDraw.Draw(im)
 def line(p,w=2):d.line(p,fill=1,width=w)
 def poly(p):d.polygon(p,fill=1)
 def ring(box,w=2):d.ellipse(box,outline=1,width=w)
 def rect(box,fill=1,w=2):d.rectangle(box,fill=fill) if fill is not None else d.rectangle(box,outline=1,width=w)
 def sword(x=16):poly([(x-2,3),(x+2,3),(x+2,19),(x+6,19),(x+6,22),(x+2,22),(x+2,28),(x-2,28),(x-2,22),(x-6,22),(x-6,19),(x-2,19)])
 if n=='Fury':
  poly([(3,9),(8,5),(15,12),(12,16),(8,13),(8,21),(3,21)]);poly([(29,9),(24,5),(17,12),(20,16),(24,13),(24,21),(29,21)]);line([(8,6),(24,27)],3);line([(24,6),(8,27)],3)
 elif n=='Blood':poly([(16,2),(7,16),(6,22),(10,28),(22,28),(26,22),(25,16)]);line([(12,19),(12,23),(15,25)],2);d.line([(12,19),(12,23),(15,25)],fill=0,width=2)
 elif n=='Ember':poly([(17,2),(11,13),(6,10),(4,23),(10,29),(22,29),(28,22),(24,12),(21,17)]);d.polygon([(16,16),(11,25),(16,28),(22,24)],fill=0)
 elif n=='Poison':ring((9,4,23,17),3);line([(16,18),(16,29)],3);line([(10,23),(22,23)],2);poly([(5,4),(9,5),(5,10),(2,8)]);poly([(25,4),(23,8),(28,10),(30,6)]);rect((12,9,13,10),0);rect((19,9,20,10),0)
 elif n=='Blades':
  for x,y in [(5,11),(14,3),(23,11)]:poly([(x,y),(x+3,y+3),(x+3,y+11),(x+5,y+11),(x+5,y+13),(x+2,y+13),(x+2,y+17),(x,y+17)])
 elif n=='Sleight':rect((4,7,17,23),None);rect((13,3,26,19),None);line([(6,27),(24,27),(28,23)],2);line([(24,23),(28,23),(28,27)],2)
 elif n=='Resonance':ring((11,11,21,21),3);d.arc((5,5,27,27),35,145,fill=1,width=2);d.arc((5,5,27,27),215,325,fill=1,width=2);line([(16,1),(16,6)],2);line([(16,26),(16,31)],2)
 elif n=='Overload':poly([(18,2),(7,18),(14,18),(11,30),(26,12),(18,12)])
 elif n=='Zero':ring((6,4,26,28),3);line([(6,27),(26,5)],2)
 elif n=='Stars':poly([(16,2),(19,12),(30,12),(21,18),(24,29),(16,22),(7,29),(10,18),(2,12),(13,12)])
 elif n=='Forge':sword();line([(4,7),(11,7)],3);line([(6,3),(6,11)],3);line([(23,7),(29,7)],2)
 elif n=='Creation':poly([(16,3),(29,10),(29,23),(16,30),(3,23),(3,10)]);d.line([(16,5),(16,16),(27,10),(16,16),(5,10),(16,16),(16,28)],fill=0,width=2);line([(24,2),(24,8)],2);line([(21,5),(27,5)],2)
 elif n=='Doom':ring((4,4,28,28),2);line([(16,6),(16,16),(23,20)],3);rect((14,26,18,30))
 elif n=='Bones':ring((3,5,10,12),2);ring((22,20,29,27),2);line([(8,10),(25,23)],5);ring((22,5,29,12),2);ring((3,20,10,27),2);line([(25,10),(8,23)],5)
 elif n=='Ethereal':poly([(16,3),(24,7),(27,15),(27,29),(21,25),(16,29),(11,25),(5,29),(5,15),(8,7)]);rect((10,13,12,16),0);rect((20,13,22,16),0)
 elif n=='Arrows':d.arc((2,3,27,29),270,90,fill=1,width=2);line([(15,3),(15,29)],1);line([(4,16),(29,16)],2);poly([(25,11),(31,16),(25,21)])
 elif n=='Melody':line([(10,22),(10,5),(26,2),(26,20)],3);line([(10,11),(26,8)],3);d.ellipse((3,20,11,27),fill=1);d.ellipse((19,18,27,25),fill=1)
 elif n=='Emotion':ring((3,4,28,29),2);line([(16,5),(16,28)],2);rect((9,12,11,14));rect((21,12,23,14));d.arc((6,14,14,22),0,180,fill=1,width=2);d.arc((18,20,26,27),180,360,fill=1,width=2)
 elif n=='Strike':sword();line([(3,7),(7,11)],2);line([(25,7),(29,3)],2);line([(25,24),(29,28)],2)
 elif n=='Guard':poly([(16,2),(28,7),(26,21),(16,30),(6,21),(4,7)]);d.polygon([(16,6),(23,10),(22,19),(16,25),(10,19),(9,10)],fill=0);line([(16,7),(16,24)],2)
 elif n=='Chain':d.rounded_rectangle((3,3,18,16),radius=5,outline=1,width=3);d.rounded_rectangle((14,16,29,29),radius=5,outline=1,width=3);line([(11,11),(21,21)],3)
 elif n=='Heavy':poly([(10,3),(22,3),(23,9),(28,13),(30,28),(2,28),(4,13),(9,9)]);rect((13,6,19,10),0);rect((10,16,22,18),0)
 elif n=='Retain':rect((8,3,24,22),None);line([(3,16),(3,28),(26,28),(29,25)],2);line([(24,24),(29,24),(29,29)],2);line([(12,8),(20,8)],2)
 elif n=='Ritual':poly([(16,3),(30,27),(2,27)]);d.polygon([(16,9),(25,24),(7,24)],fill=0);ring((11,13,21,23),2)
 elif n=='Mastery':poly([(4,10),(10,15),(16,3),(22,15),(28,10),(25,25),(7,25)]);rect((8,28,24,29));d.ellipse((14,17,18,21),fill=0)
 elif n=='Harmony':d.arc((3,8,19,24),45,315,fill=1,width=3);d.arc((13,8,29,24),225,495,fill=1,width=3);line([(10,10),(22,22)],3)
 elif n=='Ancient':poly([(16,2),(29,11),(3,11)]);rect((3,28,29,30));rect((5,13,9,26));rect((14,13,18,26));rect((23,13,27,26))
 elif n=='Encounter':rect((3,5,28,28),None);poly([(16,8),(19,14),(25,17),(19,20),(16,26),(13,20),(7,17),(13,14)]);rect((3,2,7,9));rect((25,24,29,31))
 elif n=='Curse':ring((6,3,26,23),2);line([(8,7),(24,21)],2);line([(24,7),(8,21)],2);line([(16,23),(16,30)],3);line([(10,27),(22,27)],2)
 elif n=='Status':rect((5,3,26,29),None);line([(11,10),(21,10)],2);line([(11,16),(21,16)],2);line([(11,22),(17,22)],2);rect((2,7,7,9));rect((2,14,7,16));rect((2,21,7,23))
 elif n=='Profit':ring((3,5,21,23),3);ring((11,13,29,31),3);line([(12,8),(12,19)],2);line([(8,13),(16,13)],2)
 elif n=='Foresight':poly([(2,16),(8,9),(16,5),(24,9),(30,16),(24,23),(16,27),(8,23)]);d.ellipse((9,9,23,23),fill=0);ring((13,13,19,19),3)
 elif n=='Solitude':d.ellipse((12,3,20,11),fill=1);poly([(10,14),(22,14),(26,29),(6,29)]);line([(3,8),(3,24)],2);line([(29,8),(29,24)],2)
 elif n=='Vakuu':
  # Broad, upward-hooked demon horns and a compact four-eyed face.
  poly([(10,18),(4,17),(1,12),(2,7),(6,2),(5,9),(7,12),(11,12)])
  poly([(22,18),(28,17),(31,12),(30,7),(26,2),(27,9),(25,12),(21,12)])
  poly([(11,7),(21,7),(25,14),(23,25),(16,30),(9,25),(7,14)])
  d.polygon([(10,16),(15,18),(13,20),(10,19)],fill=0);d.polygon([(22,16),(17,18),(19,20),(22,19)],fill=0)
  d.ellipse((13,10,15,12),fill=0);d.ellipse((18,10,20,12),fill=0)
  d.line([(12,24),(16,26),(20,24)],fill=0,width=2)
 elif n=='Tony':
  # Side silhouette of Bowlbug (Rock): faceted stone, one large eye, mandibles and three bent legs.
  poly([(12,13),(13,5),(18,2),(24,3),(29,8),(30,16),(26,21),(13,21)])
  d.line([(18,3),(20,10),(28,9)],fill=0,width=1);d.line([(20,10),(17,17),(28,18)],fill=0,width=1)
  poly([(5,11),(11,10),(16,16),(14,23),(7,25),(3,21),(2,16)])
  d.ellipse((4,14,12,22),fill=0);d.ellipse((6,16,10,20),fill=1)
  line([(4,23),(2,25),(4,28),(8,28)],2)
  for x in (13,20,27):line([(x,21),(x+2,26),(x,30)],2)
 elif n=='Aid':
  poly([(16,2),(24,11),(19,11),(19,20),(13,20),(13,11),(8,11)])
  poly([(5,14),(10,19),(7,19),(7,26),(3,26),(3,19),(0,19)])
  poly([(27,14),(32,19),(29,19),(29,26),(25,26),(25,19),(22,19)])
  rect((10,25,21,28))
 elif n=='Channel':
  poly([(3,3),(9,3),(16,12),(23,3),(29,3),(19,16),(25,23),(20,23),
        (16,19),(12,23),(7,23),(13,16)])
  poly([(11,26),(21,26),(16,30)])
 elif n=='Breach':
  d.arc((2,2,29,29),8,125,fill=1,width=3);d.arc((2,2,29,29),166,280,fill=1,width=3)
  poly([(12,16),(19,5),(19,13),(27,11),(18,22),(16,29),(14,21),(7,23)])
 elif n=='Rotation':
  d.arc((3,4,28,28),202,345,fill=1,width=3);d.arc((3,4,28,28),21,163,fill=1,width=3)
  poly([(24,11),(30,13),(30,6)]);poly([(8,21),(2,19),(2,26)]);ring((11,11,20,20),2)
 elif n=='Initiative':
  poly([(3,16),(14,5),(14,12),(18,16),(14,20),(14,27)]);poly([(13,16),(24,5),(24,12),(29,16),(24,20),(24,27)])
 elif n=='Fate':
  line([(6,24),(2,8),(10,5)],2);line([(26,24),(30,8),(22,5)],2)
  d.rounded_rectangle((8,3,23,29),radius=2,outline=1,width=2)
  poly([(16,7),(18,13),(22,16),(18,18),(16,24),(14,18),(10,16),(14,13)])
 elif n=='Reflow':
  line([(5,12),(5,6),(25,6),(25,10)],3);poly([(20,10),(30,10),(25,15)])
  line([(26,20),(26,26),(6,26),(6,22)],3);poly([(1,22),(11,22),(6,17)])
  poly([(16,9),(11,17),(15,17),(14,23),(21,14),(17,14)])
 elif n=='Oracle':
  poly([(4,9),(10,9),(16,13),(22,9),(28,9),(28,24),(22,24),(16,28),(10,24),(4,24)])
  d.line([(7,13),(10,13),(16,16),(22,13),(25,13)],fill=0,width=2)
  d.line([(16,16),(16,23)],fill=0,width=2)
  poly([(16,0),(18,4),(22,6),(18,8),(16,11),(14,8),(10,6),(14,4)])
 elif n=='Spectrum':
  poly([(16,2),(28,9),(28,23),(16,30),(4,23),(4,9)])
  d.line([(16,4),(16,16),(26,22)],fill=0,width=2)
  d.line([(6,22),(16,16)],fill=0,width=2)
  d.polygon([(16,9),(22,13),(22,19),(16,23),(10,19),(10,13)],fill=0)
  poly([(16,12),(20,16),(16,20),(12,16)])
 else:raise ValueError(n)
 # White ink with strictly binary transparency, retaining the 1-bit source mask.
 rgba=Image.new('RGBA',(32,32),(255,255,255,0));rgba.putalpha(im.convert('L'));rgba.save(OUT/f'{n}.png')
 assert set(rgba.getchannel('A').getdata())<={0,255}

if selected:
 sys.exit(0)

sheet=Image.new('RGB',(792,((len(NAMES)+8)//9)*142+26),'#0e1519');draw=ImageDraw.Draw(sheet)
font=ImageFont.truetype('C:/Windows/Fonts/msyh.ttc',16)
for i,(name,title) in enumerate(zip(NAMES,TITLES)):
 x=(i%9)*88;y=(i//9)*142
 icon=Image.open(OUT/f'{name}.png').resize((64,64),Image.Resampling.NEAREST)
 sheet.paste(icon,(x+12,y+13),icon);draw.text((x+44,y+91),title,font=font,fill='#d5ebe6',anchor='mm')
sheet.save(ROOT/'Assets/icon-sheet.png')
print(f'{len(NAMES)} original 1-bit icons, 32x32, alpha=0/255; Assets/icon-sheet.png')
