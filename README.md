# ต้นแบบเฟส 1: เกมแนว MU Origin (Unity)

ต้นแบบเล่นคนเดียว ใช้รูปทรงพื้นฐานแทนงานภาพ เพื่อทดสอบว่าลูป "ตีมอน → ได้ของ → อัปพลัง" สนุกไหม
ทุกอย่างสร้างจากโค้ด จึงไม่ต้องจัดฉากหรือทำ prefab เอง

## มีอะไรในต้นแบบนี้
- เดินด้วยจอยสติ๊กบนจอ (หรือ WASD / ลูกศร บนคอม) ปุ่ม Attack สกิล 3 ปุ่ม และ **AUTO**
- **3 คลาสแบบ MU คลาสสิก**: Dark Knight (ตีใกล้ เลือดเยอะ Lunge / Cyclone / Twisting Slash),
  Dark Wizard (เวทระยะไกล Energy Ball / Evil Spirits / Hellfire), Elf (ธนู Power Shot / Multiple Shot / บัฟ Greater Damage)
- **เลเวล 1-100 และ Rebirth** (1R, 2R, ...) พร้อมเควสต์จุติ, **แต้มสเตตัส STR / AGI / VIT / ENE** ใช้ใส่ของระดับสูง
- **อุปกรณ์ Tier 1-6** ตามขั้น Rebirth, ของ Normal / Excellent / Ancient, ตีบวกด้วย **Jewel of Bless** (+1..+6 ติดแน่นอน)
  และ **Jewel of Soul** (+7 ขึ้นไป มีโอกาสพลาดและลดระดับ) ชุดเรืองแสงตั้งแต่ +7 และมีสายฟ้าที่ +11 / +13
- **ปีก 4 Tier** ประจำคลาส อัปด้วย Condor Feather และเลื่อน Tier ด้วย Condor Flame เพิ่ม ATK DEF HP และความเร็ว
- **Blood Castle**, **Devil Square** และ **World Boss** (Skeleton King / Gorgon ทุก 15 นาที ตีร่วมกันได้ทั้งเซิร์ฟเวอร์)
- **Auto-Quest** (แตะแถบเควสต์), **ยาฟื้นพลังอัตโนมัติ**, **CP** รวมทุกอย่าง, **PK Mode** และชื่อแดง
- **7 แผนที่** (Lorencia, Noria, Devias, Lost Tower, Atlans, Tarkan, Icarus) วาร์ปผ่านปุ่ม Map
- ช่องใส่ของ 9 ช่อง, Excellent options, เซ็ต Ancient, Gem/Socket, Chaos Machine + Jewel of Life / Chaos
- ปาร์ตี้ กิลด์ แชท เพื่อน ตั้งชื่อตัวละคร (ออนไลน์)
- Chaos Castle, Golden Invasion, Arena, เช็กชื่อรายวัน, รางวัลออนไลน์, Achievement, Ranking
- Talent, Mount, Title, ร้านค้า Zen และ Diamond
- รายละเอียดทุกระบบ: [docs/systems.md](docs/systems.md)

## วิธีเปิดเล่น (ครั้งแรก)
1. ติดตั้ง **Unity Hub** จาก unity.com แล้วติดตั้ง **Unity 6 LTS** (6000.0.x) ติ๊ก *Android Build Support* ไว้ด้วยถ้าจะลงมือถือ
2. ใน Unity Hub กด **New project** เลือกเทมเพลต **Universal 3D** ตั้งชื่อ แล้วกด Create
3. คัดลอกโฟลเดอร์ `Assets/Scripts` จากไฟล์นี้ ไปวางในโฟลเดอร์ `Assets` ของโปรเจกต์ใหม่ (ลากใส่หน้าต่าง Project ใน Unity ก็ได้)
4. รอ Unity คอมไพล์เสร็จ แล้วเปิดฉาก `SampleScene` (มีให้อยู่แล้ว) กดปุ่ม ▶ Play
5. ในแท็บ Game ให้เลือกขนาดจอเป็นแนวนอน เช่น 1920x1080 จะเห็น UI ตรงตามที่ออกแบบ

ถ้ากด Play แล้วมีข้อความสีแดงในหน้าต่าง Console ให้ก๊อปมาส่งในเธรดได้เลย

## ใส่โมเดล 3D แทนรูปทรง
ดูวิธีใน [docs/models.md](docs/models.md) ใส่โมเดลฟรีจาก Asset Store ให้ตัวละคร มอน และต้นไม้ พร้อมท่าทางเดิน โจมตี ตาย

## ลงมือถือ Android (เมื่ออยากลองบนเครื่องจริง)
File → Build Profiles → เลือก Android → Switch Platform → ตั้ง Orientation เป็น Landscape ใน Player Settings → ต่อมือถือที่เปิด USB debugging → Build And Run

### ลงเครื่องจำลอง MuMu Player (ทดสอบแล้ว)
1. เปิด MuMu Player ให้ขึ้นหน้าจอหลักก่อน
2. เปิดโปรเจกต์ใน Unity โปรเจกต์จะต่อ adb เข้า MuMu ให้เอง (127.0.0.1:16384) จะเห็นข้อความ `[MuMu] Emulator connected` ที่แถบล่าง
   ถ้าเปิด MuMu ทีหลัง ให้กด **Tools → Android → Connect MuMu Emulator**
3. ใน Build Profiles กด **Refresh** ข้าง Run Device แล้วเลือกเครื่อง (MuMu แสดงเป็นชื่อมือถือ เช่น Samsung SM G9980)
4. กด **Build And Run** รอบแรกใช้เวลาหลายนาที (คอมไพล์ IL2CPP) รอบต่อไปเร็วขึ้นมาก

ถ้าขึ้น "No Android devices connected" ให้กด OK แล้วทำข้อ 2 ใหม่

> ทรงพื้นฐานที่สร้างตอนเกมรัน (พื้น โซน ต้นไม้ ปีก หลอดเลือด) ต้องลงสีผ่าน `Paint.Color()` เสมอ
> ถ้าใช้ `material.color` ตรงๆ จะเห็นใน Editor แต่หายไปในไฟล์ build บนมือถือ

## เล่นออนไลน์ (เฟส 2)
เปิดเซิร์ฟเวอร์ด้วย Docker แล้วล็อกอินจากหน้าแรกของเกม ดูวิธีใน [docs/online.md](docs/online.md)
ถ้าไม่เปิดเซิร์ฟเวอร์ กด **Play Offline** เล่นแบบเดิมได้

## ไฟล์โค้ด
| ไฟล์ | หน้าที่ |
|---|---|
| `GameData.cs` | ไอเทม สูตรค่าพลัง ตารางเลเวล ระบบเซฟ |
| `Combatant.cs` | ระบบเลือดและดาเมจที่ใช้ร่วมกันระหว่างผู้เล่นกับมอน |
| `Player.cs` | การเดิน ต่อสู้ ออโต้ สกิล เลเวล กระเป๋า ตีบวก |
| `Monster.cs` | AI มอน หลอดเลือด รางวัลเมื่อตาย |
| `World.cs` | สร้างแผนที่ โซนมอน กล้อง เอฟเฟกต์ และจุดเริ่มเกม |
| `HUD.cs` | UI ทั้งหมด จอยสติ๊ก ตัวเลขดาเมจ หน้าต่างกระเป๋า |
| `UIBuilder.cs` | ตัวช่วยสร้าง UI จากโค้ด |
| `Paint.cs` | ลงสีทรงที่สร้างตอนเกมรัน ด้วยวัสดุ URP ที่ติดไปกับ build เสมอ |
| `Models.cs` | โหลดโมเดล 3D จาก Resources/Models และเล่นท่าทาง (หรือขยับให้เองถ้าโมเดลไม่มีท่า) |
| `Systems.cs` | กระสุน/เวทระยะไกล และสัตว์เลี้ยง |
| `Dungeons.cs` | Blood Castle และ Devil Square |
| `WorldBoss.cs` | บอสโลกตามเวลา แบ่ง HP ร่วมกันผ่านเซิร์ฟเวอร์ |
| `Quests.cs` | สายเควสต์หลักและ Auto-Quest |
| `Maps.cs` | 7 แผนที่ วาร์ป เมือง พ่อค้า |
| `Growth.cs` | Talent, Mount, Title, ร้านค้า, Chaos Machine |
| `Activities.cs` | เช็กชื่อ รางวัลออนไลน์ Achievement Ranking Golden Invasion Chaos Castle Arena |
| `Social.cs` | แชท ปาร์ตี้ กิลด์ เพื่อน (Nakama) |
| `HUDPanels.cs` | หน้าต่างของระบบใหม่ทั้งหมด (UI ชั่วคราว) |
| `Visuals.cs` | แสงเรืองของชุดตีบวก สายฟ้า และป้ายชื่อในโลก |
| `Online.cs` | หน้าล็อกอิน ต่อเซิร์ฟเวอร์ Nakama และเก็บเซฟบนเซิร์ฟเวอร์ |
| `Multiplayer.cs` | ส่งตำแหน่งตัวเองและแสดงผู้เล่นคนอื่นในแมพ, ส่งดาเมจ PK และดาเมจบอสโลก |

## ปรับสมดุลเกมได้ที่ไหน
- ค่ามอนตามเลเวล: `MonsterHp` / `MonsterAtk` / `MonsterDef` / `MonsterExp` ใน `GameData.cs`
- ค่าตัวละคร: `Atk` / `Def` / `MaxHp` ใน `Player.cs` และค่าต่อแต้มสเตตัสของแต่ละคลาสใน `Classes` (`GameData.cs`)
- เลเวลและ Rebirth: `ExpToNext`, `MaxLevel`, `RebirthGold`, `RebirthKillsNeeded` ใน `GameData.cs`
- ตีบวก: `EnhanceChance`, `EnhanceGold` ใน `Item` (`GameData.cs`)
- ปีก: `WingCost`, `WingPower` ใน `Upgrades` (`GameData.cs`)
- โอกาสดรอปของและเพชร: `Player.OnKill`
- ดันเจี้ยนและบอส: `Dungeons.cs`, `WorldBoss.cs`
- โซนมอน: `MonsterSpawner.Field` ใน `World.cs`

## ข้อจำกัดของต้นแบบนี้
- ออนไลน์มีล็อกอิน เซฟบนเซิร์ฟเวอร์ เห็นผู้เล่นคนอื่น PK และบอสโลกร่วมกัน ยังไม่มีแชท และการต่อสู้ยังคำนวณในเครื่องแต่ละคน (เซิร์ฟเวอร์ยังไม่กันโกง)
- ภาพเป็นรูปทรงเรขาคณิตชั่วคราว ข้อความในเกมเป็นภาษาอังกฤษ
