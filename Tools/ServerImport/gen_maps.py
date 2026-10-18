import xml.etree.ElementTree as ET, collections, math
mons={m.get('ID'):m for m in ET.parse('GameRes/Config/Monsters.xml').getroot().iter('Monster')}
births={m.get('Code'):(int(m.get('BirthPosX')),int(m.get('BirthPosY'))) for m in ET.parse('GameRes/Config/MapConfig.xml').getroot().iter('Map')}
MODELS=[('Slime',['แมงมุม','หนอน','วิญญาณ','ภูต','นางไม้']),
        ('Turtle',['เต่า','มังกร','แมงป่อง','บาฮามุท','เมอร์เมด','ปีศาจทะเล']),
        ('Golem',['ยักษ์','มนุษย์หิมะ','หิน','เหล็ก','อ็อค','ร่างเทียม','อากามอสส์','เกราะ']),
        ('Goblin',['ก็อบลิน','หัวขโมย','ลอบสังหาร','คนป่า','พ่อมด','แม่มด','ธนู','สุนัข','หมาป่า','ยาร์ค','ฟาเฮล','ศิษย์']),
        ('Orc',[''])]
BASE={'Slime':0.9,'Turtle':0.9,'Golem':1.8,'Goblin':1.0,'Orc':1.3}
def model(n):
    for mdl,keys in MODELS:
        for k in keys:
            if k in n: return mdl
out=[]
for code in range(1,11):
    c=str(code); bx,by=births[c]
    root=ET.parse('Map/%s/Monsters.xml'%c).getroot()
    agg=collections.OrderedDict()
    for s in root.iter('Monster'):
        mid=s.get('Code'); m=mons.get(mid)
        if m is None or int(mid)>=1000000: continue
        t=m.get('MonsterType'); name=m.get('SName').strip()
        if t=='101': elite=False
        elif t=='301' and name.startswith('ชั้นยอด'): elite=True
        else: continue
        a=agg.setdefault(mid,{'name':name.replace('ชั้นยอด·','').strip(),'elite':elite,'lv':int(m.get('Level')),'zs':int(m.get('ZhuanSheng')),'n':0,'pts':[]})
        a['n']+=int(s.get('Num') or 0); a['pts'].append((int(s.get('X'))-bx,int(s.get('Y'))-by))
    zs=list(agg.values())
    far=max(max(abs(sum(p[0] for p in z['pts'])/len(z['pts'])),abs(sum(p[1] for p in z['pts'])/len(z['pts']))) for z in zs)
    seen={}
    rows=[]
    normals=sorted([z for z in zs if not z['elite']],key=lambda z:(z['zs'],z['lv']))
    for z in zs:
        x=sum(p[0] for p in z['pts'])/len(z['pts'])*40/far; y=-sum(p[1] for p in z['pts'])/len(z['pts'])*40/far
        d=math.hypot(x,y)
        if d<13: 
            if d<0.01: x,y,d=0,1,1
            x,y=x/d*13,y/d*13
        nm=z['name']
        if not z['elite']:
            seen[nm]=seen.get(nm,0)+1
            if seen[nm]>1: nm=nm+' II'
        mdl=model(z['name'])
        rank=normals.index(z) if z in normals else len(normals)
        scale=BASE[mdl]*(1+0.05*rank)*(1.5 if z['elite'] else 1)
        power=z['zs']*100+z['lv']
        count=1 if z['elite'] else max(4,min(7,round(z['n']/4)))
        rows.append('Z("%s", "%s", %d, %.0ff, %.0ff, %d, %.2ff%s)'%(nm,mdl,power,x,y,count,scale,', elite: true' if z['elite'] else ''))
    req=min(z['zs']*100+z['lv'] for z in normals)
    out.append('// map %s\n%d\n'%(c,req)+',\n'.join(rows))
print('\n\n'.join(out))
