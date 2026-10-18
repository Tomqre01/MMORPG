# ระบบเกมแบบ MU คลาสสิก

เวอร์ชันนี้เปลี่ยนระบบเดิมทั้งหมด เซฟเก่า (ทั้งในเครื่องและบนเซิร์ฟเวอร์) จะถูกรีเซ็ตเริ่มใหม่อัตโนมัติ
(`PlayerSave.version` ต้องเท่ากับ `GameData.SaveVersion`)

## 1. สามคลาสหลัก
| คลาส | เด่น | สเตตัสหลัก | สกิล |
|---|---|---|---|
| Dark Knight | ตีใกล้ เลือดเยอะ DEF สูง | STR | **Lunge** พุ่งเข้าหาเป้า, **Cyclone** หมุนดาบรอบตัว, **Twisting Slash** หมุนดาบวงใหญ่ |
| Dark Wizard | เวทระยะไกล เคลียร์มอนเป็นกลุ่ม แต่บาง | ENE | **Energy Ball** ยิงเดี่ยว, **Evil Spirits** วิญญาณพุ่งใส่ 8 ตัว, **Hellfire** ไฟวงกว้างรอบตัว |
| Elf | ธนูระยะไกล ตีเร็ว เดินเร็ว | AGI | **Power Shot** ยิงแรง, **Multiple Shot** ยิงกระจาย 6 ตัว, **Greater Damage** บัฟ ATK +30% DEF +20% และฟื้นเลือด |

อัปเลเวลสกิลได้ที่หน้าต่าง **Char** (ใช้ Zen) สกิลละสูงสุด Lv20 ดาเมจเพิ่ม 6% ต่อเลเวล

## 2. เลเวล สเตตัส และ Rebirth
- เลเวลตันที่ **100** ทุกเลเวลได้ **5 แต้ม** ลง STR / AGI / VIT / ENE เองที่หน้าต่าง Char หรือกด **Auto Add**
  - STR: ATK (Dark Knight มากสุด) · AGI: ATK ของ Elf, DEF และความเร็วตี · VIT: HP · ENE: ATK ของ Dark Wizard และเพิ่มดาเมจสกิลคลาสอื่นเล็กน้อย
- ชุดและอาวุธต้องมี **สเตตัสหลักของคลาส** ถึงเกณฑ์ (ดูช่องในกระเป๋า ช่องสีแดง = ใส่ไม่ได้ และบอกเหตุผลตอนแตะ)
- **Rebirth**: เลเวล 100 แล้วจะเริ่มเควสต์จุติ ล่ามอน Lv50 ขึ้นไป 100 ตัว และจ่าย Zen (20,000 × (R+1))
  จุติแล้วกลับเป็น Lv1 เก็บสเตตัสเดิม ได้แต้มโบนัส 50 แต้ม HP +200 และใส่ของ Tier ถัดไปได้

## 3. อุปกรณ์ ตีบวก และปีก
- ของแบ่ง **Tier 1-6** (Bronze, Silver, Dragon, Black Dragon, Great Dragon, Dark Phoenix) Tier N ต้อง Rebirth อย่างน้อย N-1
  มอนดรอปของ Tier ตามขั้น Rebirth ของเรา ส่วนใหญ่เป็นของคลาสตัวเอง
- ความหายาก: Normal (ขาว), **Excellent** (เขียว ×1.6), **Ancient** (ทอง ×2.5)
- ตีบวกที่กระเป๋า (ปุ่ม Bag ช่องอุปกรณ์ที่สวมอยู่):
  - **Jewel of Bless** ตี +1 ถึง +6 ติด 100%
  - **Jewel of Soul** ตี +7 ขึ้นไป โอกาส 60% แล้วลดลง 5% ทุกขั้น ถ้าพลาดลดลง 1 ระดับ
- **ชุดเรืองแสง**: ดูจากค่าตีบวกต่ำสุดของเกราะ หมวก รองเท้า
  +7 เรืองแสงอ่อน · +9 เรืองแรงขึ้นและมีแสงกระพริบ · +11 มีสายฟ้าแลบรอบตัวเป็นระยะ · +13 กลายเป็นสีทองและสายฟ้าตลอดเวลา
  ผู้เล่นคนอื่นเห็นแสงนี้ด้วย
- **ปีก** (ปุ่ม Wings) ชื่อประจำคลาส 4 Tier เช่น Dark Knight: Satan → Dragon → Doom → Conqueror
  - สร้างปีกแรก: Condor Feather 10 + Condor Flame 1 + 2,000 Zen
  - อัป +1 ถึง +10 ในแต่ละ Tier ใช้ Condor Feather + Zen
  - เลื่อน Tier ใช้ **Condor Flame** + Feather + Zen ปีก Tier สูงจะใหญ่ขึ้น มีขนเพิ่ม และเรืองแสง
  - เพิ่ม ATK, DEF, HP และความเร็วเดิน
  - Feather ดรอปจากมอน Lv5+, Flame จากมอน Lv20+, Blood Castle และ World Boss

## 4. ดันเจี้ยนและบอส
ปุ่ม **Dungeon** เลือกได้ 2 แบบ อย่างละ 2 รอบต่อวัน มอนในดันเจี้ยนตามเลเวลเรา เข้าแล้วเลือดเต็ม
- **Blood Castle** (5 นาที): ฆ่ามอนบนสะพาน 25 ตัว → ตีประตูปราสาท → ทำลายรูปปั้น Statue of Saint ได้ Archangel's Weapon
  → เดินกลับไปส่ง Archangel ที่ต้นสะพาน (เปิด AUTO ไว้ เกมจะเดินกลับให้) รางวัล EXP มาก Bless 3 Soul 1-2 Feather 3 และอาจได้ Flame
- **Devil Square** (2:30): มอนมาเป็นระลอก 5 ระลอก ระลอกสุดท้ายมีบอส ยิ่งทำดาเมจมากยิ่งได้ EXP / Zen / Bless มาก
  ถ้าตายกลางทางได้รางวัลครึ่งเดียว
- **World Boss** ที่ลานบอสฝั่งตะวันตก (กล่องข้อความใต้แถบเควสต์บอกเวลา)
  เกิดทุก 15 นาทีตามเวลาจริง (UTC) สลับ **Skeleton King** Lv45 และ **Gorgon** Lv75 อยู่ 5 นาที
  ทุกคนในเซิร์ฟเวอร์ตีตัวเดียวกัน: แต่ละเกมส่งดาเมจสะสมของตัวเองผ่าน `WorldSync` แล้วหักเลือดบอสในเครื่องคนอื่น
  ใครทำดาเมจ ≥2% ได้รางวัล ใครดาเมจสูงสุดได้ของ Ancient + Soul + Flame เพิ่ม
  บอสทุบพื้นเป็นวงทุก 7 วินาที

## 5. ความสะดวกบนมือถือ
- **Auto-Quest**: แตะแถบ QUEST ใต้หลอดเลือด เกมจะเดินไปโซนที่ถูกต้องและตีให้เอง พอเควสต์จบจะต่อเควสต์ล่าถัดไปให้
  ถ้าเควสต์ต้องใช้หน้าต่าง (รับ Pet, ทำปีก, ตีบวก, ดันเจี้ยน) จะเปิดหน้าต่างนั้นให้ หลังจบสายเควสต์หลักจะมีเควสต์ล่าไม่รู้จบ
- **ยาฟื้นพลัง**: ปุ่ม Potion (มีตัวเลขจำนวน) และปุ่ม **Auto Pot** กินยาเองเมื่อเลือดต่ำกว่า 45% ซื้อยาในกระเป๋า
- **CP**: รวมเลเวล Rebirth ATK DEF HP เลเวลสกิล ค่าตีบวก ปีก และสัตว์เลี้ยง แสดงบนหัวผู้เล่นคนอื่นด้วย
- **PK Mode**: ปุ่ม Peace / PK ข้างปุ่ม AUTO
  - เปิด PK แล้วกด Attack หรือใช้สกิลกับผู้เล่นอื่นนอกเมืองได้ (ดาเมจครึ่งหนึ่งของปกติ อีกฝั่งหัก DEF ของตัวเอง)
  - ฆ่าผู้เล่นที่ชื่อไม่แดง = ได้ **ชื่อแดง** (PK 1 แต้ม หายเองทีละแต้มทุก 5 นาที)
  - ชื่อแดงโดนทุกคนตีได้แม้ไม่ได้เปิด PK และเวลาตายเสีย Zen 10% และอาจทำของในกระเป๋าหล่น
  - ฆ่าคนชื่อแดงได้ค่าหัว ไม่โดนโทษ
  - ในเมือง Lorencia (วงหินกลางแมพ) ตีกันไม่ได้ มอนไม่ตามเข้า และฟื้นเลือดเร็ว

## 6. แผนที่และวาร์ป
ปุ่ม **Map** วาร์ปได้ 7 แผนที่ (เสีย Zen ตามแผนที่) แต่ละแผนที่มีเมืองปลอดภัยตรงกลางและพ่อค้า (เดินเข้าไปใกล้จะเปิดร้าน)
ทุกแผนที่อยู่ในฉากเดียวกัน ห่างกัน 1000 หน่วย เฉพาะแผนที่ที่อยู่จึงมีมอน

| แผนที่ | เข้าได้ | มอน |
|---|---|---|
| Lorencia | Lv1 | Slime 1, Turtle 3, Goblin 5, Orc 10, Golem 20 + ลานบอสโลก |
| Noria | Lv15 | Elite Goblin 15, Forest Orc 22, Moss Golem 28 |
| Devias | Lv30 | Ice Turtle 32, Yeti 38, Orc Warlord 45 |
| Lost Tower | Lv50 | Shadow Goblin 50, Stone Golem 55, Death Orc 62 |
| Atlans | Lv65 | Sea Slime 65, Shell Lord 72, Coral Golem 78 |
| Tarkan | Lv80 | Sand Orc 82, Desert Golem 88, Tarkan Turtle 95 |
| Icarus | 1 Rebirth | Sky Slime 100, Phoenix Orc 105, Sky Golem 110 |

Auto-Quest วาร์ปไปแผนที่ของเควสต์ให้เองฟรี

## 7. อุปกรณ์แบบ MU Origin
- ช่องใส่ 9 ช่อง: อาวุธ เกราะ หมวก รองเท้า ถุงมือ กางเกง สร้อย แหวน 2 วง (สร้อยเพิ่ม ATK แหวนเพิ่ม HP ใส่ได้ทุกคลาส)
- **Excellent** สุ่มออปชัน 1-2 อย่าง: Excellent damage, ATK%, DEF%, HP%, Critical, Zen drop, ฟื้นเลือดเมื่อฆ่า, ลดดาเมจ, ความเร็วตี
- **Ancient** มีชื่อเซ็ตตามคลาส (Hyon / Apollo / Gywen) ใส่หลายชิ้นได้โบนัส: 2 ชิ้น ATK+5%, 3 ชิ้น DEF+8%, 4 ชิ้น HP+8%, 5 ชิ้น ATK+10%
- **Gem/Socket** (Bag > Gems): Ruby ATK, Sapphire DEF, Emerald HP, Topaz Critical ผสม 3 เม็ดเป็นเลเวลถัดไป ของ Excellent มี 0-1 ช่อง Ancient มี 2 ช่อง
- **Chaos Machine** (Bag > Chaos Machine): Jewel of Life ใส่ออปชันเสริม +4 ทีละขั้น (สูงสุด +28), สุ่มออปชัน Excellent ใหม่, ผสม Condor Flame, ผสม Feather จากของขยะ
- ชุดเรืองแสงดูจาก 5 ชิ้น (เกราะ หมวก ถุงมือ กางเกง รองเท้า)

## 8. ปาร์ตี้ กิลด์ แชท เพื่อน (ปุ่ม Social, ต้องล็อกอินออนไลน์)
- แชท World / Party / Guild ข้อความล่าสุดโชว์มุมซ้ายล่างของจอ
- ตั้งชื่อตัวละคร (Set my name) ชื่อนี้ขึ้นบนหัวและในแชท
- ปาร์ตี้สูงสุด 5 คน: Invite nearby แล้วอีกฝั่งกด Accept invite ได้ EXP +15% ต่อสมาชิกที่อยู่ใกล้
- กิลด์ (Nakama groups): ตั้งกิลด์ 30000 Zen, ค้นหาและเข้า, แชทกิลด์, ชื่อกิลด์บนหัว
- เพื่อน: เพิ่มเพื่อนคนที่ยืนใกล้, รับคำขอ, ดูออนไลน์/ออฟไลน์

## 9. กิจกรรม (ปุ่ม Events)
- เช็กชื่อรายวัน 7 วันวนรอบ, รางวัลออนไลน์ 10 / 30 / 60 / 120 นาที
- **Golden Invasion** ทุก 30 นาที (เวลาจริง) มอนทองบุกแผนที่ที่อยู่ 5 นาที ดรอป Bless และ Diamond
- **Chaos Castle** วันละครั้ง: ฆ่ามอนทั้งหมด พื้นถล่มแคบลงทุก 30 วินาที
- **Arena** วันละ 5 ครั้ง: สู้กับเงาของผู้เล่นอื่นที่ CP ใกล้เคียงจาก Ranking (ออฟไลน์เป็นบอท) ได้แต้ม Arena และ Diamond
- **Achievement** 17 อย่าง ได้ Diamond
- **Ranking** ตาม CP จากเซิร์ฟเวอร์ (อัปข้อมูลตัวเองทุก 1 นาที)

## 10. พัฒนาตัวละครเพิ่ม
- **Talent** (ปุ่ม Talent): ได้ 10 แต้มต่อ Rebirth และ 1 แต้มต่อ 10 เลเวลหลังจุติ ลง ATK / DEF / HP / Critical / Skill / Speed รีเซ็ตได้
- **Mount** (ปุ่ม Mount / Ride): Uniria, Dinorant, Dark Horse, Fenrir เพิ่มความเร็วและค่าพลังตอนขี่ คนอื่นเห็นสัตว์ขี่
- **Title** (ปุ่ม Title): ปลดล็อกจากการเล่น สวมได้ 1 อัน ขึ้นบนหัวและเพิ่มค่าพลัง
- **ร้านค้า** (ปุ่ม Shop หรือเดินไปหา Merchant): ขายด้วย Zen และ Diamond
- **Diamond** ได้จากเช็กชื่อ Achievement กิจกรรม และ Arena (ยังไม่มีการเติมเงิน)

## ข้อจำกัดตอนนี้
- การต่อสู้ยังคำนวณในเครื่องผู้เล่น (PK และบอสโลกเชื่อข้อมูลที่อีกเครื่องส่งมา) ยังไม่กันโกง ต้องย้ายไปคำนวณบนเซิร์ฟเวอร์ในขั้นถัดไป
- มอนในสนามแต่ละเครื่องแยกกัน ยกเว้น World Boss ที่แบ่ง HP กัน
- Ranking, Arena และค่าใน Ranking มาจากข้อมูลที่แต่ละเครื่องส่งเอง
- ตัวเลขสมดุลยังเป็นค่าเริ่มต้น ปรับได้ตามหัวข้อ "ปรับสมดุลเกมได้ที่ไหน" ใน README

## UI (MU Origin layout)

All UI is still built in code, now with a shared skin (`UISkin.cs`): dark lacquered panels with gold trim,
glossy orbs with gold rings, gradient bars and red title plates. The sprites are drawn into small
textures at start-up and 9-sliced, so there are still no art files to manage.

- **Top left:** class portrait (tap = Character), level badge, name, CP, HP bar, buff line. A red dot means free stat points.
- **Top centre:** Zen, Diamonds, Bless and Soul; the target's name and HP bar while you fight; dungeon status.
- **Top right:** round minimap centred on you (monsters red, bosses purple, players blue, zones coloured by danger)
  with the map name and coordinates (tap = Teleport); two rows of activity icons that fold away with `>`.
- **Left:** Quest / Team tracker (tap = auto quest or Social), world boss timer, chat box (tap = Social).
- **Bottom:** joystick, full-width EXP bar, CP change pop-up.
- **Bottom right:** Attack, three skills in an arc with cooldown numbers, potion, AUTO, Auto Potion, Peace/PK, Ride,
  BAG, and MENU, which opens a grid with every window.
- **Windows:** dimmed background (tap outside to close), title plate and a red X.

The canvas keeps a 1080 px height and widens on long phones. In the editor, **Tools > UI Shots** (in Play mode)
saves screenshots of the HUD and the main windows to `Assets/UIShots` (ignored by git).

## Maps and monsters from the MU Origin server data

`Assets/Scripts/MapData.cs` is generated from the MU Origin server files (`D:\MU_Server7\Config`) by
`Tools/ServerImport/gen_maps.py` (run it on the PC that has the server folder, from `MU_Server7\Config`).

- **10 field maps**, matching server map codes 1–10: Lorencia, Noria, Devias, Dungeon, Lost Tower, Atlans, Tarkan, Icarus, Aida, Kanturu Ruins.
- **Monsters per map:** each normal monster type (MonsterType 101) becomes a zone with its Thai name (`SName`) and level.
  The zone sits where that type spawns on the server, measured from the town (`MapConfig.xml` birth point) and scaled to the 96 m map.
- **Elites** (`ชั้นยอด·`, MonsterType 301) are single boss-strength monsters that respawn after a minute and pay boss rewards.
- **Left out:** golden monsters, event monsters and the Lv 200 field bosses (Golden Invasion and the World Boss cover those).
- **Power** (`GameData.Power`) puts rebirth and level on one scale like the server's ZhuanSheng + Level:
  - Lv 90 is 90, and 3 Rebirth Lv 92 is 392.
  - Maps unlock by power (Atlans 2R Lv85, Tarkan 3R Lv88, Aida 5R Lv88, Kanturu 6R Lv88).
  - Auto-hunt, quests, dungeons and the minimap use power too.
  - Monster HP and attack grow more gently past power 100, and EXP per kill follows the level inside the rebirth.
- Monsters reuse the five models the project has (Slime, Turtle, Goblin, Orc, Golem). The model is picked from keywords in the Thai name and tinted per map.
