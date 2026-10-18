# Thai gear and wing names per class and tier from the MU Origin server config (Goods.xml, Wing/Wing_N.xml).
import xml.etree.ElementTree as ET
byid={}
for it in ET.parse('Goods.xml').getroot().iter('Item'):
    byid[it.get('ID')]=it.get('Title').strip()
CLASSES=[('DarkKnight',0,'100','1005'),('DarkWizard',1,'101','1015'),('Elf',2,'102','10254')]
# our Slot order: Weapon, Armor, Helm, Boots, Gloves, Pants, Necklace, Ring -> server Categoriy digit
SLOTCAT={'Armor':'01','Helm':'00','Boots':'04','Gloves':'02','Pants':'03','Necklace':'05','Ring':'06'}
print('        static readonly System.Collections.Generic.Dictionary<string, string[]> ServerGear = new System.Collections.Generic.Dictionary<string, string[]>')
print('        {')
for cid,occ,pre,wpre in CLASSES:
    for slot in ['Weapon','Armor','Helm','Boots','Gloves','Pants','Necklace','Ring']:
        names=[]
        for t in range(1,7):
            iid = (wpre+('%02d'%t) if cid!='Elf' else wpre+('%02d'%t)) if slot=='Weapon' else pre+SLOTCAT[slot]+'%02d'%t
            if slot=='Weapon' and cid!='Elf': iid=wpre+'0%02d'%t
            names.append(byid.get(iid,'?'+iid))
        print('            ["%s/%s"] = new[] { %s },'%(cid,slot,', '.join('"%s"'%n for n in names)))
print('        };')
for cid,occ,_,_ in CLASSES:
    w=[l.get('Name').strip() for l in ET.parse('Wing/Wing_%d.xml'%occ).getroot().iter('Level')]
    print('// wings %s: %s'%(cid, ' | '.join(w)))
