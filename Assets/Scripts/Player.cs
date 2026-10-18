using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class Player : Combatant
    {
        public static Player Instance { get; private set; }

        const float AggroRange = 14f;
        const float RespawnDelay = 3f;
        const float PotionCooldown = 1.5f;
        const float PvpScale = 0.5f;          // players hit each other for half damage
        const float PkPointSeconds = 300f;    // one PK point wears off every 5 minutes

        public PlayerSave Data { get; private set; }
        public bool AutoBattle { get; set; }
        public bool PkMode { get; set; }
        public Vector2 MoveInput { get; set; } // written by the on-screen joystick
        public event System.Action Changed;

        // Auto-quest: hunt in this zone (and only this monster, when set) until the quest is done.
        public MonsterSpawner QuestZone { get; private set; }

        readonly float[] skillCd = new float[3];
        Monster target;
        RemotePlayer pvpTarget;
        Vector3 spawnPoint;
        Transform body, wings;
        Aura aura;
        GearVisual gear;
        WeaponTrail trail;
        int combo;          // next step of the basic attack combo (0, 1, 2 = finisher)
        float lastSwing;
        float attackTimer, respawnTimer, lastHitTime, regenAcc, saveTimer, potionCd;
        float buffUntil, buffAtk, buffDef;
        string lastAttackerSession, lastAttackerName;
        float lastAttackerTime;
        bool dirty;

        public bool HasClass => !string.IsNullOrEmpty(Data.classId);
        public ClassDef Class => Classes.Get(Data.classId);
        public Monster Target => target;
        public RemotePlayer PvpTarget => pvpTarget;
        public bool IsRed => Data.pkPoints > 0;
        public bool BuffActive => Time.time < buffUntil;
        public float BuffLeft => Mathf.Max(0, buffUntil - Time.time);
        public bool InTown => DungeonRun.Active == null && Maps.InTown(transform.position);
        public float RespawnIn => respawnTimer;

        // ---- Stats ----

        // Everything gear, talents, title and mount add on top of the base stats. Rebuilt on every Commit.
        public struct Bonus
        {
            public float atkPct, defPct, hpPct, crit, exc, zen, reduce, speedPct, skillPct, moveSpeed;
            public bool lifeOnKill;
            public int setPieces;
            public string setName;
        }

        Bonus bonus;
        public Bonus Bonuses => bonus;

        public int WingPower => Upgrades.WingPower(Data.wingTier, Data.wingLevel);
        float AtkBuff => BuffActive ? 1f + buffAtk : 1f;
        float DefBuff => BuffActive ? 1f + buffDef : 1f;
        public bool Riding => Data.mountId >= 0 && Data.mountId < Growth.Mounts.Length && (Data.mountsOwned & (1 << Data.mountId)) != 0;

        public void RecalcBonus()
        {
            var b = new Bonus();
            var sets = new Dictionary<string, int>();
            for (int i = 0; i < Data.equipped.Count; i++)
            {
                var it = Data.equipped[i];
                if (it.IsEmpty || !CanEquip(it, out _)) continue;
                if (it.opts != null)
                    foreach (int o in it.opts)
                        switch ((ExOpt)o)
                        {
                            case ExOpt.ExcDamage: b.exc += 0.1f; break;
                            case ExOpt.AtkPct: b.atkPct += 0.05f; break;
                            case ExOpt.DefPct: b.defPct += 0.05f; break;
                            case ExOpt.HpPct: b.hpPct += 0.04f; break;
                            case ExOpt.CritRate: b.crit += 0.05f; break;
                            case ExOpt.ZenDrop: b.zen += 0.3f; break;
                            case ExOpt.LifeOnKill: b.lifeOnKill = true; break;
                            case ExOpt.DmgReduce: b.reduce += 0.04f; break;
                            case ExOpt.AtkSpeed: b.speedPct += 0.07f; break;
                        }
                if (it.sockets != null)
                    foreach (int g in it.sockets)
                    {
                        if (g <= 0) continue;
                        int lv = g % 10;
                        switch ((GemType)(g / 10))
                        {
                            case GemType.Ruby: b.atkPct += 0.015f * lv; break;
                            case GemType.Sapphire: b.defPct += 0.02f * lv; break;
                            case GemType.Emerald: b.hpPct += 0.02f * lv; break;
                            case GemType.Topaz: b.crit += 0.01f * lv; break;
                        }
                    }
                if (!string.IsNullOrEmpty(it.set))
                {
                    sets.TryGetValue(it.set, out int n);
                    sets[it.set] = n + 1;
                }
            }
            foreach (var kv in sets)
                if (kv.Value > b.setPieces) { b.setPieces = kv.Value; b.setName = kv.Key; }
            // Ancient set: 2 pieces ATK +5%, 3 DEF +8%, 4 HP +8%, 5 ATK +10% more.
            if (b.setPieces >= 2) b.atkPct += 0.05f;
            if (b.setPieces >= 3) b.defPct += 0.08f;
            if (b.setPieces >= 4) b.hpPct += 0.08f;
            if (b.setPieces >= 5) b.atkPct += 0.1f;

            var t = Data.talents;
            b.atkPct += t[0] * 0.02f;
            b.defPct += t[1] * 0.02f;
            b.hpPct += t[2] * 0.02f;
            b.crit += t[3] * 0.01f;
            b.skillPct += t[4] * 0.03f;
            b.speedPct += t[5] * 0.01f;

            if (Data.titleId >= 0 && Data.titleId < Growth.Titles.Length && Growth.Titles[Data.titleId].Unlocked(Data))
            {
                var title = Growth.Titles[Data.titleId];
                b.atkPct += title.Atk;
                b.defPct += title.Def;
                b.hpPct += title.Hp;
            }
            if (Riding)
            {
                var m = Growth.Mounts[Data.mountId];
                b.moveSpeed += m.Speed;
                b.atkPct += m.AtkPct;
                b.defPct += m.DefPct;
            }
            bonus = b;
        }

        public override int Atk
        {
            get
            {
                var c = Class;
                float raw = Data.str * c.StrAtk + Data.agi * c.AgiAtk + Data.ene * c.EneAtk + GearStat(Slot.Weapon) + GearStat(Slot.Necklace) + WingPower * 5;
                return Mathf.RoundToInt(raw * (1f + bonus.atkPct) * AtkBuff);
            }
        }

        public override int Def
        {
            get
            {
                float armor = GearStat(Slot.Armor) + GearStat(Slot.Helm) + GearStat(Slot.Boots) + GearStat(Slot.Gloves) + GearStat(Slot.Pants);
                return Mathf.RoundToInt(((Data.agi / 3f + Data.vit / 5f + armor) * Class.DefMul + WingPower * 3) * (1f + bonus.defPct) * DefBuff);
            }
        }

        public override int MaxHp
        {
            get
            {
                var c = Class;
                float raw = c.BaseHp + Data.vit * c.HpPerVit + Data.level * c.HpPerLevel + Data.rebirth * 200 + WingPower * 40
                            + GearAt(7) + GearAt(GameData.Ring2Index);
                return Mathf.RoundToInt(raw * (1f + bonus.hpPct));
            }
        }

        public float CritChance => 0.1f + bonus.crit;
        public float AttackInterval => Class.AttackInterval / Mathf.Min(2.5f, (1f + Data.agi * 0.0015f) * (1f + bonus.speedPct));
        public float MoveSpeed => 6f + Class.MoveBonus + Upgrades.WingSpeed(Data.wingTier, Data.wingLevel) + bonus.moveSpeed;

        public float SkillPower(int i)
        {
            float p = 1f + 0.06f * (Data.skillLevels[i] - 1);
            if (Class.MainStat != "ENE") p *= 1f + Data.ene * 0.001f; // energy sharpens everyone's skills a little
            return p * (1f + bonus.skillPct);
        }

        // Player damage roll: critical (x1.5) and excellent damage (x1.3) from gear and talents.
        int Roll(float multiplier, out bool crit)
        {
            float dmg = Atk * multiplier * Random.Range(0.9f, 1.1f);
            crit = Random.value < CritChance;
            if (crit) dmg *= 1.5f;
            if (Random.value < bonus.exc) { dmg *= 1.3f; crit = true; }
            return Mathf.RoundToInt(dmg);
        }

        protected override int ReduceIncoming(int dmg) => Mathf.Max(1, Mathf.RoundToInt(dmg * (1f - Mathf.Min(0.5f, bonus.reduce))));

        // Everything in one number: level, rebirth, stats, skills, gear, enhancement, wings, pet, talents and mount.
        public int CombatPower
        {
            get
            {
                int skills = 0;
                foreach (int lv in Data.skillLevels) skills += lv - 1;
                int plus = 0;
                foreach (var it in Data.equipped) if (!it.IsEmpty) plus += it.plus * it.plus * 8 + (it.rarity == Rarity.Common ? 0 : 40);
                int talents = 0;
                foreach (int t in Data.talents) talents += t;
                return Atk * 4 + Def * 3 + MaxHp / 2 + Data.level * 10 + Data.rebirth * 1000
                       + skills * 60 + plus + WingPower * 60 + Data.petLevel * 25 + talents * 80 + (Riding ? 300 * (Data.mountId + 1) : 0);
            }
        }

        // The armour set glows from +7: the lowest enhance level among helm, armour, gloves, pants and boots.
        public int GlowPlus
        {
            get
            {
                int min = int.MaxValue;
                foreach (var s in new[] { Slot.Armor, Slot.Helm, Slot.Boots, Slot.Gloves, Slot.Pants })
                {
                    var it = Data.equipped[(int)s];
                    if (it.IsEmpty) return 0;
                    min = Mathf.Min(min, it.plus);
                }
                return min;
            }
        }

        public int MainStatValue => Class.MainStat == "STR" ? Data.str : Class.MainStat == "AGI" ? Data.agi : Data.ene;

        public bool CanEquip(Item it, out string why)
        {
            why = null;
            if (it.IsEmpty) return false;
            if (!string.IsNullOrEmpty(it.classId) && it.classId != Data.classId) why = "For " + Classes.Get(it.classId).Name + " only";
            else if (Data.rebirth < it.ReqRebirth) why = "Needs " + it.ReqRebirth + " Rebirth";
            else if (MainStatValue < it.ReqStat) why = "Needs " + Class.MainStat + " " + it.ReqStat;
            return why == null;
        }

        int GearStat(Slot s) => GearAt((int)s);

        int GearAt(int index)
        {
            var it = Data.equipped[index];
            return it.IsEmpty || !CanEquip(it, out _) ? 0 : it.Stat;
        }

        // ---- Setup ----

        public static Player Create(Vector3 position)
        {
            var root = new GameObject("Player");
            root.transform.position = position;
            var p = root.AddComponent<Player>();
            p.BuildBody();
            Pet.Create(p);
            return p;
        }

        void Awake()
        {
            Instance = this;
            Data = SaveSystem.Load();
            RecalcBonus();
            Hp = MaxHp;
            spawnPoint = transform.position;
        }

        void BuildBody()
        {
            if (body != null) Destroy(body.gameObject);
            body = MakeBody(transform, Class, out anim);
            aura = Aura.Attach(body, Class.BodyColor);
            aura.SetLevel(HasClass ? GlowPlus : 0);
            gear = body.GetComponentInChildren<GearVisual>();
            if (gear != null) gear.Apply(GearVisual.TiersOf(Data));
            var model = gear != null ? gear.transform : body;
            trail = WeaponTrail.Attach(model, Fx.TrailColor(Class.Id));
            BuildWings();
            BuildMount();
        }

        void BuildWings()
        {
            if (wings != null) Destroy(wings.gameObject);
            wings = MakeWings(transform, Data.wingTier, Data.wingLevel);
            if (wings != null && Riding) wings.localPosition = new Vector3(0, 2.15f, -0.35f);
        }

        // Model per class (Resources/Models/DarkKnight, DarkWizard, Elf, or Player), else placeholder shapes.
        // Also used for other players seen online (RemotePlayer).
        public static Transform MakeBody(Transform parent, ClassDef cls, out ModelAnimator animator)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(parent, false);

            var model = ModelLibrary.Spawn(cls.Id, body, 1.9f, out animator)
                        ?? ModelLibrary.Spawn("Player", body, 1.9f, out animator);
            if (model != null) return body;

            animator = null;
            Part(body, PrimitiveType.Capsule, new Vector3(0, 1f, 0), Vector3.one, cls.BodyColor);
            if (cls.Id == "DarkWizard")
            {
                Part(body, PrimitiveType.Cylinder, new Vector3(0.55f, 1.1f, 0.2f), new Vector3(0.08f, 1f, 0.08f), new Color(0.45f, 0.3f, 0.15f));
                Part(body, PrimitiveType.Sphere, new Vector3(0.55f, 2.15f, 0.2f), Vector3.one * 0.25f, new Color(0.4f, 0.8f, 1f));
            }
            else if (cls.Id == "Elf")
                Part(body, PrimitiveType.Cube, new Vector3(0.5f, 1.1f, 0.35f), new Vector3(0.06f, 1.3f, 0.12f), new Color(0.5f, 0.32f, 0.15f));
            else
                Part(body, PrimitiveType.Cube, new Vector3(0.55f, 1.1f, 0.45f), new Vector3(0.12f, 0.12f, 1.3f), new Color(0.85f, 0.85f, 0.9f));
            return body;
        }

        static void Part(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            Paint.Color(go, color);
        }

        public static int WingCode(int tier, int level) => tier * 100 + level;
        public static Transform MakeWings(Transform parent, int code) => MakeWings(parent, code / 100, code % 100);

        // Feathered blades behind the back: more of them and bigger each tier, a little longer each level.
        public static Transform MakeWings(Transform parent, int tier, int level)
        {
            if (tier <= 0) return null;
            var wings = new GameObject("Wings").transform;
            wings.SetParent(parent, false);
            wings.localPosition = new Vector3(0, 1.35f, -0.35f);
            float size = 0.75f + tier * 0.18f + level * 0.015f;
            var color = Upgrades.WingColor(tier);
            int feathers = Mathf.Min(tier + 1, 4);
            for (int side = -1; side <= 1; side += 2)
                for (int f = 0; f < feathers; f++)
                {
                    float k = 1f - f * 0.18f;
                    var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(w.GetComponent<Collider>());
                    w.transform.SetParent(wings, false);
                    w.transform.localPosition = new Vector3(side * 0.55f * size * k, 0.25f - f * 0.22f, -0.1f - f * 0.03f);
                    w.transform.localRotation = Quaternion.Euler(0, side * 25f, side * (-30f + f * 22f));
                    w.transform.localScale = new Vector3(1.15f * size * k, 0.42f * size, 0.04f);
                    Paint.Color(w, Color.Lerp(color, Color.white, f * 0.15f));
                }
            if (tier >= 3) Aura.Attach(wings, color).SetLevel(tier >= 4 ? 9 : 7);
            return wings;
        }

        // ---- Frame ----

        void Update()
        {
            float dt = Time.deltaTime;
            saveTimer -= dt;
            if (dirty && saveTimer <= 0) Flush();
            if (!HasClass) return;
            TickPk(dt);

            if (IsDead)
            {
                respawnTimer -= dt;
                if (respawnTimer <= 0) Respawn();
                return;
            }

            for (int i = 0; i < skillCd.Length; i++) skillCd[i] = Mathf.Max(0, skillCd[i] - dt);
            attackTimer -= dt;
            potionCd -= dt;
            Regenerate(dt);
            if (Data.autoPotion && Hp < MaxHp * 0.45f) UsePotion();
            if (Hp > MaxHp) Hp = MaxHp;

            Vector2 input = MoveInput + KeyboardInput();
            if (input.sqrMagnitude > 1f) input.Normalize();

            if (input.sqrMagnitude > 0.01f)
            {
                Step(new Vector3(input.x, 0, input.y), MoveSpeed * input.magnitude);
                QuestZone = null; // steering yourself ends the auto-quest walk
                if (!AutoBattle) { target = null; pvpTarget = null; }
                return;
            }

            if (pvpTarget != null)
            {
                if (!CanHit(pvpTarget) || FlatDistance(pvpTarget) > Class.AttackRange + 1.5f) pvpTarget = null;
                else
                {
                    Face(pvpTarget.transform.position - transform.position);
                    BasicAttack();
                    return;
                }
            }

            var goal = DungeonRun.Active != null ? DungeonRun.Active.AutoGoal : null;
            if (AutoBattle && goal.HasValue)
            {
                Vector3 toGoal = goal.Value - transform.position;
                toGoal.y = 0;
                if (toGoal.magnitude > 1f) Step(toGoal, MoveSpeed);
                return;
            }

            if (AutoBattle) PickAutoTarget();
            if (target == null || target.IsDead) return;

            float range = Class.AttackRange;
            Vector3 toTarget = target.transform.position - transform.position;
            if (FlatDistance(target) > range)
            {
                if (AutoBattle) Step(toTarget, MoveSpeed);
                else target = null;
                return;
            }

            Face(toTarget);
            if (AutoBattle)
                for (int i = skillCd.Length - 1; i >= 0; i--)
                {
                    if (Class.Skills[i].Kind == SkillKind.Buff && BuffActive) continue;
                    if (UseSkill(i)) return;
                }
            BasicAttack();
        }

        void PickAutoTarget()
        {
            if (QuestZone != null)
            {
                string only = Quests.MonsterFilter(Data);
                if (target == null || target.IsDead || target.Spawner != QuestZone)
                    target = FindNearest(transform.position, 200f, m => m.Spawner == QuestZone && (only == null || m.DisplayName == only));
                if (target == null)
                {
                    Vector3 toZone = QuestZone.Center - transform.position;
                    toZone.y = 0;
                    if (toZone.magnitude > 2f) Step(toZone, MoveSpeed); // walk there and wait for respawns
                }
                return;
            }
            // Nearby first; otherwise go looking only for monsters you can handle (not a world boss).
            if (target == null || target.IsDead)
                target = FindNearest(transform.position, AggroRange, null)
                         ?? FindNearest(transform.position, 200f, m => !m.IsWorldBoss && m.Level <= Data.level + 5);
        }

        public void StartQuestHunt(MonsterSpawner zone)
        {
            QuestZone = zone;
            AutoBattle = zone != null;
            target = null;
        }

        void Regenerate(float dt)
        {
            float rate = Time.time - lastHitTime > 3f ? 0.05f : 0.01f;
            if (InTown) rate = 0.15f;
            regenAcc += MaxHp * rate * dt;
            int whole = (int)regenAcc;
            if (whole > 0)
            {
                regenAcc -= whole;
                Hp = Mathf.Min(MaxHp, Hp + whole);
            }
        }

        void TickPk(float dt)
        {
            if (Data.pkPoints <= 0) return;
            Data.pkClock -= dt;
            if (Data.pkClock > 0) return;
            Data.pkPoints--;
            Data.pkClock = PkPointSeconds;
            HUD.Message(Data.pkPoints > 0 ? "PK level down: " + Data.pkPoints : "Your name is no longer red", 3f);
            Commit();
        }

        static Vector2 KeyboardInput()
        {
            // Typing in chat or a name box must not walk the character.
            var es = UnityEngine.EventSystems.EventSystem.current;
            if (es != null && es.currentSelectedGameObject != null)
            {
                var field = es.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>();
                if (field != null && field.isFocused) return Vector2.zero;
            }
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k == null) return Vector2.zero;
            float x = (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1 : 0) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1 : 0);
            float y = (k.wKey.isPressed || k.upArrowKey.isPressed ? 1 : 0) - (k.sKey.isPressed || k.downArrowKey.isPressed ? 1 : 0);
            return new Vector2(x, y);
#else
            return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        }

        static Monster FindNearest(Vector3 from, float maxDistance, System.Predicate<Monster> filter)
        {
            Monster best = null;
            float bestDist = maxDistance;
            foreach (var m in Monster.All)
            {
                if (m.IsDead || (filter != null && !filter(m))) continue;
                float d = Flat(m.transform.position - from);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        Monster TargetWithin(float range) =>
            target != null && !target.IsDead && FlatDistance(target) <= range ? target : FindNearest(transform.position, range, null);

        // ---- Combat ----

        bool CanHit(RemotePlayer rp) => rp != null && !InTown && rp.CanBeAttacked(PkMode);

        RemotePlayer PvpWithin(float range)
        {
            if (CanHit(pvpTarget) && FlatDistance(pvpTarget) <= range) return pvpTarget;
            return WorldSync.Instance != null && !InTown ? WorldSync.Instance.NearestAttackable(transform.position, range, PkMode) : null;
        }

        // Attack button: a player you may fight comes first (PK mode, or an outlaw), else the nearest monster.
        public void AttackPressed()
        {
            if (IsDead || !HasClass) return;
            var rp = PvpWithin(Class.AttackRange + 1.5f);
            if (rp != null)
            {
                pvpTarget = rp;
                target = null;
                BasicAttack();
                return;
            }
            pvpTarget = null;
            target = TargetWithin(Class.AttackRange + 1.5f);
            if (target != null) BasicAttack();
        }

        // A three-step combo: two quick cuts, then a heavier finisher. Each step plays its own clip,
        // draws a slash trail, steps into the target, and lands its hit at the clip's impact frame.
        void BasicAttack()
        {
            if (attackTimer > 0) return;
            RemotePlayer rp = pvpTarget;
            Monster m = rp == null ? target : null;
            if (rp == null && (m == null || m.IsDead || FlatDistance(m) > Class.AttackRange + 1.5f)) return;
            if (Time.time - lastSwing > AttackInterval * 2.2f + 0.4f) combo = 0;
            int step = combo;
            combo = (combo + 1) % 3;
            lastSwing = Time.time;
            bool finisher = step == 2;
            float dur = AttackInterval * (finisher ? 1.35f : 1f);
            attackTimer = dur;
            Vector3 aim = rp != null ? rp.transform.position : m.transform.position;
            FaceNow(aim);
            PlayClip("Attack0" + (step + 1), ModelAnimator.Anim.Attack, dur * 0.95f);
            if (!Class.Ranged)
            {
                if (trail != null) trail.Emit(dur * 0.75f);
                StartCoroutine(Lunge(aim, finisher ? 0.7f : 0.3f, dur * 0.35f));
            }
            StartCoroutine(Impact(step, rp, m, dur * (finisher ? 0.5f : 0.42f)));
        }

        IEnumerator Impact(int step, RemotePlayer rp, Monster m, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (IsDead) yield break;
            bool fin = step == 2;
            var color = Fx.TrailColor(Class.Id);
            if (rp != null)
            {
                DealPvp(rp, fin ? 1.5f : 1f, Class.BodyColor, fin);
                yield break;
            }
            if (m == null || m.IsDead) yield break;
            if (Class.Ranged)
            {
                Deal(m, fin ? 1.5f : 1f, Class.BodyColor, fin);
                if (fin) // the finisher also lets fly at two more monsters near the target
                {
                    int extra = 0;
                    foreach (var o in Monster.All.ToArray())
                    {
                        if (extra >= 2) break;
                        if (o == m || o.IsDead || Flat(o.transform.position - m.transform.position) > 3.5f) continue;
                        Deal(o, 0.7f, Class.BodyColor, false);
                        extra++;
                    }
                }
                yield break;
            }

            // Melee cleave: the target plus everyone in front (the finisher hits all around).
            Vector3 fwd = transform.forward;
            float reach = Class.AttackRange + (fin ? 1.6f : 1f);
            int hits = 0;
            foreach (var o in Monster.All.ToArray())
            {
                if (o == null || o.IsDead || (o.Stationary && o != m)) continue;
                Vector3 d = o.transform.position - transform.position;
                d.y = 0;
                bool main = o == m;
                if (main ? d.magnitude > reach + 1f : d.magnitude > reach || (!fin && Vector3.Angle(fwd, d) > 70f)) continue;
                int dmg = Roll(main ? (fin ? 1.6f : 1f) : (fin ? 0.8f : 0.5f), out bool crit);
                o.TakeDamage(dmg, crit, this);
                Fx.Spark(o.transform.position + Vector3.up * 1.1f, color, crit ? 2f : 1.4f);
                o.Knock(transform.position, fin ? 1.1f : 0.4f);
                if (++hits >= 8) break;
            }
            Vector3 chest = transform.position + Vector3.up * 1.1f;
            if (step == 0) Fx.Arc(chest, transform.rotation * Quaternion.Euler(0, 0, 40f), 2.2f, -80f, 160f, color, 1f);
            else if (step == 1) Fx.Arc(chest, transform.rotation * Quaternion.Euler(0, 0, -8f), 2.4f, 80f, -160f, color, 1f);
            else
            {
                Fx.Shockwave(transform.position + fwd * 1.2f, reach, color);
                CameraFollow.Shake(0.22f, 0.3f);
            }
            if (hits > 0) Fx.HitStop(fin ? 0.08f : 0.04f);
        }

        void FaceNow(Vector3 at)
        {
            Vector3 d = at - transform.position;
            d.y = 0;
            if (d.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(d.normalized);
        }

        // Step in toward the target as the blow starts (never into it).
        IEnumerator Lunge(Vector3 at, float distance, float seconds)
        {
            Vector3 d = at - transform.position;
            d.y = 0;
            float room = d.magnitude - 1.1f;
            distance = Mathf.Min(distance, room);
            if (distance <= 0.02f) yield break;
            Vector3 dir = d.normalized;
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                Vector3 p = transform.position + dir * (distance * Time.deltaTime / seconds);
                p.x = Mathf.Clamp(p.x, Bounds.xMin, Bounds.xMax);
                p.z = Mathf.Clamp(p.z, Bounds.yMin, Bounds.yMax);
                transform.position = p;
                yield return null;
            }
        }

        // Melee hits land now; ranged ones fly as a projectile and land on arrival.
        void Deal(Monster m, float multiplier, Color color, bool ringOnHit)
        {
            int dmg = Roll(multiplier, out bool crit);
            if (Class.Ranged)
                Projectile.Fire(transform.position + Vector3.up * 1.3f, m, dmg, crit, this, color, ringOnHit ? 1.2f : 0f);
            else
            {
                m.TakeDamage(dmg, crit, this);
                if (ringOnHit) Effect.Ring(m.transform.position, 1.2f, color);
            }
        }

        // Another player: the hit is sent to their game, which applies their own DEF.
        void DealPvp(RemotePlayer rp, float multiplier, Color color, bool ringOnHit)
        {
            int dmg = Roll(multiplier * PvpScale, out bool crit);
            System.Action land = () =>
            {
                if (rp == null) return;
                if (WorldSync.Instance != null) WorldSync.Instance.SendHit(rp, dmg, crit);
                FloatingText.Spawn(rp.transform.position + Vector3.up * 2f, crit ? dmg + "!" : dmg.ToString(), new Color(1f, 0.55f, 0.2f), crit ? 44 : 34);
                if (ringOnHit) Effect.Ring(rp.transform.position, 1.2f, color);
            };
            if (Class.Ranged) Projectile.Fire(transform.position + Vector3.up * 1.3f, rp.transform, color, () => rp != null, land);
            else land();
        }

        public float SkillCooldownRatio(int i) => skillCd[i] / Class.Skills[i].Cooldown;

        public bool UseSkill(int i)
        {
            if (IsDead || !HasClass || skillCd[i] > 0) return false;
            var sk = Class.Skills[i];
            float mult = sk.Multiplier * SkillPower(i);
            switch (sk.Kind)
            {
                case SkillKind.Single:
                {
                    var rp = PvpWithin(sk.Range);
                    if (rp != null && pvpTarget == rp) { DealPvp(rp, mult, sk.Color, true); break; }
                    var t = TargetWithin(sk.Range);
                    if (t == null) return false;
                    target = t;
                    Deal(t, mult, sk.Color, true);
                    break;
                }
                case SkillKind.Dash:
                {
                    Transform to;
                    var rp = pvpTarget != null ? PvpWithin(sk.Range) : null;
                    var t = rp == null ? TargetWithin(sk.Range) : null;
                    if (rp != null) to = rp.transform;
                    else if (t != null) { target = t; to = t.transform; }
                    else return false;
                    Vector3 d = to.position - transform.position;
                    d.y = 0;
                    Effect.Ring(transform.position, 1f, sk.Color);
                    if (d.magnitude > sk.Radius)
                    {
                        Vector3 p = to.position - d.normalized * sk.Radius * 0.8f;
                        p.x = Mathf.Clamp(p.x, Bounds.xMin, Bounds.xMax);
                        p.z = Mathf.Clamp(p.z, Bounds.yMin, Bounds.yMax);
                        transform.position = new Vector3(p.x, transform.position.y, p.z);
                    }
                    if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d.normalized);
                    if (rp != null) DealPvp(rp, mult, sk.Color, true);
                    else
                    {
                        Deal(t, mult, sk.Color, true);
                        t.Knock(transform.position, 1.2f);
                        Fx.Spark(t.transform.position + Vector3.up * 1.1f, sk.Color, 2.2f);
                    }
                    Fx.Arc(transform.position + Vector3.up * 1.1f + transform.forward * 0.6f, transform.rotation * Quaternion.Euler(0, 0, 80f), 2f, -90f, 180f, sk.Color, 1.1f);
                    Fx.Shockwave(transform.position + transform.forward * 0.8f, 3f, sk.Color);
                    CameraFollow.Shake(0.2f, 0.25f);
                    Fx.HitStop(0.06f);
                    break;
                }
                case SkillKind.SelfArea:
                {
                    int n = HitArea(transform.position, sk.Radius, mult) + HitPlayersArea(transform.position, sk.Radius, mult, sk.Color);
                    if (n == 0) return false;
                    Effect.Ring(transform.position, sk.Radius, sk.Color);
                    StartCoroutine(SpinArcs(sk.Color, Mathf.Min(sk.Radius, 4.5f)));
                    if (sk.Radius >= 6f) // the big ones get a second wave
                        Effect.Ring(transform.position, sk.Radius * 0.6f, Color.Lerp(sk.Color, Color.white, 0.4f));
                    if (anim == null || !anim.HasClip("Skill01")) StartCoroutine(Spin(0.45f));
                    CameraFollow.Shake(0.15f, 0.25f);
                    Fx.HitStop(0.05f);
                    break;
                }
                case SkillKind.TargetArea:
                {
                    var t = TargetWithin(sk.Range);
                    if (t == null) return false;
                    target = t;
                    Vector3 c = t.transform.position;
                    HitArea(c, sk.Radius, mult);
                    HitPlayersArea(c, sk.Radius, mult, sk.Color);
                    Effect.Ring(c, sk.Radius, sk.Color);
                    break;
                }
                case SkillKind.Multi:
                {
                    var hits = new List<Monster>();
                    foreach (var m in Monster.All)
                        if (!m.IsDead && FlatDistance(m) <= sk.Range) hits.Add(m);
                    var players = PlayersWithin(transform.position, sk.Range);
                    if (hits.Count == 0 && players.Count == 0) return false;
                    hits.Sort((a, b) => FlatDistance(a).CompareTo(FlatDistance(b)));
                    int max = (int)sk.Radius;
                    foreach (var rp in players) if (max-- > 0) DealPvp(rp, mult, sk.Color, false);
                    for (int n = 0; n < hits.Count && max > 0; n++, max--) Deal(hits[n], mult, sk.Color, false);
                    break;
                }
                case SkillKind.Buff:
                {
                    int lv = Data.skillLevels[i];
                    buffUntil = Time.time + sk.Duration + 0.5f * (lv - 1);
                    buffAtk = sk.BuffAtk + 0.01f * (lv - 1);
                    buffDef = sk.BuffDef;
                    Hp = Mathf.Min(MaxHp, Hp + Mathf.RoundToInt(MaxHp * sk.Heal));
                    Effect.Ring(transform.position, 3f, sk.Color);
                    FloatingText.Spawn(transform.position + Vector3.up * 3f, sk.Name.Replace("\n", " ") + "!", sk.Color, 40);
                    break;
                }
            }
            skillCd[i] = sk.Cooldown;
            bool big = sk.Kind == SkillKind.SelfArea || sk.Kind == SkillKind.Buff;
            float len = big ? 0.8f : 0.65f;
            PlayClip(big ? "Skill01" : "Skill02", ModelAnimator.Anim.Attack, len);
            if (trail != null && !Class.Ranged) trail.Emit(len * 0.85f);
            attackTimer = Mathf.Max(attackTimer, len * 0.8f);
            combo = 0;
            return true;
        }

        // Crescents racing around the hero while the whirlwind spins.
        IEnumerator SpinArcs(Color color, float radius)
        {
            for (int i = 0; i < 3; i++)
            {
                Fx.Arc(transform.position + Vector3.up * (0.9f + 0.15f * i), transform.rotation, radius * (0.75f + 0.12f * i), i * 120f, 300f, color, 1.1f, 0.3f);
                yield return new WaitForSeconds(0.12f);
            }
        }

        IEnumerator Spin(float seconds)
        {
            var b = body;
            for (float t = 0; t < seconds && b != null; t += Time.deltaTime)
            {
                b.localRotation = Quaternion.Euler(0, 360f * t / seconds, 0);
                yield return null;
            }
            if (b != null) b.localRotation = Quaternion.identity;
        }

        int HitArea(Vector3 center, float radius, float multiplier)
        {
            var hits = new List<Monster>();
            foreach (var m in Monster.All)
                if (!m.IsDead && Flat(m.transform.position - center) <= radius) hits.Add(m);
            foreach (var m in hits)
                m.TakeDamage(Roll(multiplier, out bool crit), crit, this);
            return hits.Count;
        }

        List<RemotePlayer> PlayersWithin(Vector3 center, float radius)
        {
            var list = new List<RemotePlayer>();
            if (WorldSync.Instance == null || InTown) return list;
            foreach (var rp in WorldSync.Instance.Remotes)
                if (CanHit(rp) && Flat(rp.transform.position - center) <= radius) list.Add(rp);
            return list;
        }

        int HitPlayersArea(Vector3 center, float radius, float multiplier, Color color)
        {
            var list = PlayersWithin(center, radius);
            foreach (var rp in list) DealPvp(rp, multiplier, color, false);
            return list.Count;
        }

        // ---- PvP (messages from WorldSync) ----

        public void ReceivePvpHit(int raw, bool crit, string fromSession, string fromName)
        {
            if (IsDead || InTown || !HasClass) return;
            lastAttackerSession = fromSession;
            lastAttackerName = fromName;
            lastAttackerTime = Time.time;
            TakeDamage(raw, crit, null);
        }

        public void OnPvpKill(string victimName, bool victimWasRed)
        {
            if (victimWasRed)
            {
                int bounty = 500 + Data.level * 20;
                Data.gold += bounty;
                HUD.Message("Outlaw " + victimName + " defeated!  +" + bounty + "g", 3f);
            }
            else
            {
                Data.pkPoints++;
                Data.pkKills++;
                Data.pkClock = PkPointSeconds;
                HUD.Message("You killed " + victimName + ". Your name is RED (PK " + Data.pkPoints + ")", 4f);
            }
            Commit();
        }

        // Outlaws (red names) lose gold, and maybe a bag item, whenever they die.
        void RedPenalty()
        {
            int lost = Data.gold / 10;
            Data.gold -= lost;
            string msg = "Outlaw penalty: lost " + lost + "g";
            if (Data.bag.Count > 0 && Random.value < 0.5f)
            {
                int i = Random.Range(0, Data.bag.Count);
                msg += " and dropped " + Data.bag[i].Label;
                Data.bag.RemoveAt(i);
            }
            HUD.Message(msg, 4f);
        }

        // ---- Rewards and progress ----

        public void OnKill(Monster m)
        {
            Vector3 p = m.transform.position + Vector3.up * 2.6f;
            int gold = Mathf.RoundToInt(m.GoldReward * (1f + bonus.zen));
            Data.gold += gold;
            int exp = Mathf.RoundToInt(m.ExpReward * Social.PartyExpBonus(transform.position));
            FloatingText.Spawn(p, "+" + exp + " EXP  +" + gold + "g", new Color(1f, 0.85f, 0.3f), 26);
            AddExp(exp);
            Data.kills++;
            if (bonus.lifeOnKill) Hp = Mathf.Min(MaxHp, Hp + MaxHp / 8);

            float lv = m.Level;
            if (Random.value < 0.3f) GiveItem(GameData.RollDrop(m.Level, Data, m.IsBoss ? 5f : 1f), p + Vector3.up);
            if (Random.value < 0.03f + lv * 0.0004f) Loot(ref Data.jewelBless, "Jewel of Bless", new Color(0.75f, 0.45f, 1f), p, 1.6f);
            if (Random.value < 0.015f + lv * 0.0003f) Loot(ref Data.jewelSoul, "Jewel of Soul", new Color(1f, 0.5f, 0.75f), p, 2f);
            if (lv >= 5 && Random.value < 0.06f) Loot(ref Data.feathers, "Condor Feather", Upgrades.WingColor(1), p, 2.4f);
            if (lv >= 20 && Random.value < 0.012f) Loot(ref Data.flames, "Condor Flame", new Color(1f, 0.45f, 0.15f), p, 2.8f);
            if (Random.value < 0.12f) Loot(ref Data.petFood, "Pet Food", new Color(1f, 0.6f, 0.8f), p, 3.2f);
            if (Random.value < 0.08f) Loot(ref Data.potions, "HP Potion", new Color(1f, 0.35f, 0.35f), p, 3.6f);
            if (lv >= 10 && Random.value < 0.012f) Loot(ref Data.jewelChaos, "Jewel of Chaos", new Color(0.95f, 0.75f, 0.3f), p, 4.4f);
            if (lv >= 10 && Random.value < 0.008f) Loot(ref Data.jewelLife, "Jewel of Life", new Color(0.4f, 0.9f, 1f), p, 4.8f);
            if (lv >= 15 && Random.value < 0.03f)
            {
                var gem = (GemType)Random.Range(1, 5);
                int glv = lv >= 80 ? 2 : 1;
                Data.gemBag[GameData.GemIndex(gem, glv)]++;
                FloatingText.Spawn(p + Vector3.up * 4f, "+1 " + gem + " Lv" + glv, new Color(0.5f, 0.9f, 1f), 24);
            }

            if (Data.level >= GameData.MaxLevel && m.Level >= GameData.RebirthKillMinLevel) Data.rebirthKills++;
            Quests.OnKill(this, m);
            Commit();
        }

        static void Loot(ref int counter, string name, Color color, Vector3 p, float up)
        {
            counter++;
            FloatingText.Spawn(p + Vector3.up * up, "+1 " + name, color, 24);
        }

        public void GiveItem(Item item, Vector3 textPos)
        {
            if (Data.bag.Count >= GameData.BagSize)
            {
                Data.gold += item.SellPrice;
                FloatingText.Spawn(textPos, "Bag full: sold " + item.name, Color.gray, 24);
            }
            else
            {
                Data.bag.Add(item);
                FloatingText.Spawn(textPos, item.name, GameData.RarityColor(item.rarity), 30);
            }
        }

        public void AddExp(int amount)
        {
            if (Data.level >= GameData.MaxLevel) { Data.exp = 0; return; }
            Data.exp += amount;
            bool up = false;
            while (Data.level < GameData.MaxLevel && Data.exp >= GameData.ExpToNext(Data.level, Data.rebirth))
            {
                Data.exp -= GameData.ExpToNext(Data.level, Data.rebirth);
                Data.level++;
                Data.freePoints += GameData.PointsPerLevel(Data.rebirth);
                up = true;
            }
            if (Data.level >= GameData.MaxLevel) Data.exp = 0;
            if (!up) return;
            Hp = MaxHp;
            FloatingText.Spawn(transform.position + Vector3.up * 3f, "LEVEL UP! " + Data.level, new Color(0.4f, 1f, 0.4f), 52);
            if (Data.level >= GameData.MaxLevel) HUD.Message("Level 100! Start the Rebirth quest in Character", 4f);
            Quests.Check(this);
        }

        public void ChooseClass(string id)
        {
            GameData.StartClass(Data, Classes.Get(id));
            BuildBody();
            Hp = MaxHp;
            Commit();
        }

        // ---- Stats and skills ----

        public void AddStat(string stat, int amount)
        {
            amount = Mathf.Min(amount, Data.freePoints);
            if (amount <= 0) return;
            switch (stat)
            {
                case "STR": Data.str += amount; break;
                case "AGI": Data.agi += amount; break;
                case "VIT": Data.vit += amount; break;
                default: Data.ene += amount; break;
            }
            Data.freePoints -= amount;
            Commit();
        }

        // Spends all free points in the usual split for the class (main stat 3 : 1 : 1).
        public void AutoAddStats()
        {
            string main = Class.MainStat;
            string[] split = main == "STR" ? new[] { "STR", "STR", "STR", "AGI", "VIT" }
                           : main == "AGI" ? new[] { "AGI", "AGI", "AGI", "STR", "VIT" }
                           : new[] { "ENE", "ENE", "ENE", "VIT", "AGI" };
            int i = 0;
            while (Data.freePoints > 0) AddStat(split[i++ % split.Length], 1);
        }

        public string UpgradeSkill(int i)
        {
            int lv = Data.skillLevels[i];
            if (lv >= Classes.MaxSkillLevel) return "Already max level";
            int cost = Classes.SkillUpgradeGold(lv);
            if (Data.gold < cost) return "Need " + cost + "g";
            Data.gold -= cost;
            Data.skillLevels[i]++;
            Commit();
            return Class.Skills[i].Name.Replace("\n", " ") + " is now Lv " + Data.skillLevels[i];
        }

        // ---- Rebirth ----

        public string RebirthBlocker()
        {
            if (Data.rebirth >= GameData.MaxRebirth) return "Max rebirth reached";
            if (Data.level < GameData.MaxLevel) return "Reach Lv " + GameData.MaxLevel;
            if (Data.rebirthKills < GameData.RebirthKillsNeeded)
                return "Quest: defeat Lv" + GameData.RebirthKillMinLevel + "+ monsters " + Data.rebirthKills + "/" + GameData.RebirthKillsNeeded;
            if (Data.gold < GameData.RebirthGold(Data.rebirth)) return "Need " + GameData.RebirthGold(Data.rebirth) + "g";
            return null;
        }

        public string Rebirth()
        {
            string blocker = RebirthBlocker();
            if (blocker != null) return blocker;
            Data.gold -= GameData.RebirthGold(Data.rebirth);
            Data.rebirth++;
            Data.level = 1;
            Data.exp = 0;
            Data.rebirthKills = 0;
            int bonus = GameData.RebirthBonus(Data.rebirth);
            Data.freePoints += bonus;
            Hp = MaxHp;
            Effect.Ring(transform.position, 6f, new Color(1f, 0.9f, 0.4f));
            Effect.Ring(transform.position, 3f, Color.white);
            FloatingText.Spawn(transform.position + Vector3.up * 3f, "REBIRTH " + Data.rebirth + "R!", new Color(1f, 0.85f, 0.3f), 60);
            Quests.Check(this);
            Commit();
            return "Rebirth " + Data.rebirth + "! +" + bonus + " stat points, Tier " + (Data.rebirth + 1) + " gear unlocked";
        }

        // ---- Items ----

        public string Equip(int bagIndex)
        {
            var item = Data.bag[bagIndex];
            if (!CanEquip(item, out string why)) return why;
            int idx = (int)item.slot;
            if (item.slot == Slot.Ring)
            {
                var r1 = Data.equipped[(int)Slot.Ring];
                var r2 = Data.equipped[GameData.Ring2Index];
                idx = r1.IsEmpty ? (int)Slot.Ring : r2.IsEmpty ? GameData.Ring2Index : r1.Stat <= r2.Stat ? (int)Slot.Ring : GameData.Ring2Index;
            }
            var current = Data.equipped[idx];
            Data.equipped[idx] = item;
            Data.bag.RemoveAt(bagIndex);
            if (!current.IsEmpty) Data.bag.Insert(bagIndex, current);
            Hp = Mathf.Min(Hp, MaxHp);
            RefreshLook();
            Commit();
            return "Equipped " + item.Label;
        }

        // Sells every bag item of the ticked grades (0 Normal, 1 Excellent, 2 Ancient), any tier or +level.
        public int SellBag(bool[] grades, out int gold)
        {
            int sold = 0;
            gold = 0;
            for (int i = Data.bag.Count - 1; i >= 0; i--)
            {
                var it = Data.bag[i];
                if (!grades[(int)it.rarity]) continue;
                gold += it.SellPrice;
                Data.bag.RemoveAt(i);
                sold++;
            }
            Data.gold += gold;
            Commit();
            return sold;
        }

        // Sells one bag item, whatever it is.
        public string SellItem(int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= Data.bag.Count) return "Nothing there";
            var it = Data.bag[bagIndex];
            Data.bag.RemoveAt(bagIndex);
            Data.gold += it.SellPrice;
            Commit();
            return "Sold " + it.Label + " for " + it.SellPrice.ToString("N0") + "g";
        }

        // Jewel of Bless for +1..+6 (always works), Jewel of Soul for +7 and up (can fail and drop a level).
        public string Enhance(int index)
        {
            var item = Data.equipped[index];
            if (item.IsEmpty) return "Nothing equipped";
            if (!item.CanEnhance) return "Already +" + GameData.MaxPlus;
            bool soul = item.UsesSoul;
            if (soul ? Data.jewelSoul < 1 : Data.jewelBless < 1) return "Need a Jewel of " + (soul ? "Soul" : "Bless");
            if (Data.gold < item.EnhanceGold) return "Need " + item.EnhanceGold + "g";
            if (soul) Data.jewelSoul--; else Data.jewelBless--;
            Data.gold -= item.EnhanceGold;
            string result;
            if (Random.value < item.EnhanceChance)
            {
                item.plus++;
                result = "Success! " + item.Label;
                if (item.plus == 7 || item.plus == 9 || item.plus == 11 || item.plus == 13) result += "  (it shines brighter)";
            }
            else
            {
                item.plus = Mathf.Max(0, item.plus - 1);
                result = "Failed... dropped to +" + item.plus;
            }
            RefreshLook();
            Quests.Check(this);
            Commit();
            return result;
        }

        public string UpgradeWings()
        {
            var step = Upgrades.NextWingStep(Data);
            if (step == Upgrades.WingStep.Max) return "Wings are at max tier";
            Upgrades.WingCost(Data, out int feathers, out int flames, out int gold);
            if (Data.feathers < feathers || Data.flames < flames || Data.gold < gold) return "Not enough materials";
            Data.feathers -= feathers;
            Data.flames -= flames;
            Data.gold -= gold;
            if (step == Upgrades.WingStep.Craft) { Data.wingTier = 1; Data.wingLevel = 0; }
            else if (step == Upgrades.WingStep.Level) Data.wingLevel++;
            else { Data.wingTier++; Data.wingLevel = 0; }
            BuildWings();
            Hp = Mathf.Min(Hp, MaxHp);
            Quests.Check(this);
            Commit();
            return step == Upgrades.WingStep.Level ? "Wings +" + Data.wingLevel : Class.Wings[Data.wingTier - 1] + "!";
        }

        // ---- Gems and sockets ----

        public int GemCount(GemType t, int lv) => Data.gemBag[GameData.GemIndex(t, lv)];

        public string SocketGem(int equipIndex, GemType t)
        {
            var it = Data.equipped[equipIndex];
            if (it.IsEmpty || it.SocketCount == 0) return "This item has no sockets";
            int slot = System.Array.IndexOf(it.sockets, 0);
            if (slot < 0) return "All sockets are full";
            for (int lv = 5; lv >= 1; lv--)
            {
                int i = GameData.GemIndex(t, lv);
                if (Data.gemBag[i] <= 0) continue;
                Data.gemBag[i]--;
                it.sockets[slot] = (int)t * 10 + lv;
                Commit();
                return "Socketed " + t + " Lv" + lv;
            }
            return "No " + t + " gems";
        }

        public string ClearSockets(int equipIndex)
        {
            var it = Data.equipped[equipIndex];
            if (it.SocketCount == 0) return "No sockets";
            for (int i = 0; i < it.sockets.Length; i++) it.sockets[i] = 0;
            Commit();
            return "Gems removed (they break)";
        }

        // Three gems of a level make one of the next level.
        public string CombineGems()
        {
            int made = 0;
            foreach (GemType t in new[] { GemType.Ruby, GemType.Sapphire, GemType.Emerald, GemType.Topaz })
                for (int lv = 1; lv < 5; lv++)
                {
                    int i = GameData.GemIndex(t, lv);
                    while (Data.gemBag[i] >= 3)
                    {
                        Data.gemBag[i] -= 3;
                        Data.gemBag[GameData.GemIndex(t, lv + 1)]++;
                        made++;
                    }
                }
            Commit();
            return made > 0 ? "Combined " + made + " gems" : "Need 3 gems of the same kind and level";
        }

        // ---- Talents, mounts, titles, shop ----

        public string AddTalent(int i)
        {
            if (Growth.TalentPoints(Data) - Growth.TalentSpent(Data) <= 0) return "No talent points (they come with Rebirth)";
            if (Data.talents[i] >= Growth.MaxTalent) return "Already max";
            Data.talents[i]++;
            Commit();
            return Growth.TalentNames[i] + " " + Data.talents[i] + "/" + Growth.MaxTalent;
        }

        public string ResetTalents()
        {
            if (Data.gold < Growth.TalentResetZen) return "Need " + Growth.TalentResetZen + " Zen";
            Data.gold -= Growth.TalentResetZen;
            for (int i = 0; i < Data.talents.Length; i++) Data.talents[i] = 0;
            Commit();
            return "Talents reset";
        }

        public string BuyMount(int i)
        {
            var m = Growth.Mounts[i];
            if ((Data.mountsOwned & (1 << i)) != 0)
            {
                Data.mountId = Data.mountId == i ? -1 : i;
                BuildMount();
                Commit();
                return Data.mountId == i ? "Riding " + m.Name : "On foot";
            }
            if (Data.rebirth < m.MinRebirth || (Data.rebirth == 0 && Data.level < m.MinLevel))
                return m.MinRebirth > 0 ? "Needs " + m.MinRebirth + " Rebirth" : "Needs Lv " + m.MinLevel;
            if (Data.gold < m.Zen || Data.diamonds < m.Diamonds) return "Not enough " + (m.Zen > 0 ? "Zen" : "Diamonds");
            Data.gold -= m.Zen;
            Data.diamonds -= m.Diamonds;
            Data.mountsOwned |= 1 << i;
            Data.mountId = i;
            BuildMount();
            Commit();
            return "Got " + m.Name + "!";
        }

        public void ToggleRide()
        {
            if (Data.mountsOwned == 0) { HUD.Message("No mount yet. Get one in Growth > Mount", 2.5f); return; }
            if (Data.mountId >= 0) Data.mountId = -1;
            else for (int i = Growth.Mounts.Length - 1; i >= 0; i--) if ((Data.mountsOwned & (1 << i)) != 0) { Data.mountId = i; break; }
            BuildMount();
            Commit();
        }

        public string EquipTitle(int i)
        {
            var t = Growth.Titles[i];
            if (!t.Unlocked(Data)) return "Locked: " + t.Requirement;
            Data.titleId = Data.titleId == i ? -1 : i;
            Commit();
            return Data.titleId == i ? "Title: " + t.Name : "Title removed";
        }

        public string Buy(Growth.ShopItem item)
        {
            if (Data.gold < item.Zen) return "Need " + item.Zen + " Zen";
            if (Data.diamonds < item.Diamonds) return "Need " + item.Diamonds + " Diamonds";
            Data.gold -= item.Zen;
            Data.diamonds -= item.Diamonds;
            item.Give(this);
            Commit();
            return "Bought " + item.Name;
        }

        Transform mount;

        void BuildMount()
        {
            if (mount != null) Destroy(mount.gameObject);
            mount = null;
            if (body != null) body.localPosition = Vector3.zero;
            if (wings != null) wings.localPosition = new Vector3(0, 1.35f, -0.35f);
            if (!Riding) return;
            mount = Growth.MakeMount(transform, Data.mountId);
            if (body != null) body.localPosition = Vector3.up * 0.8f;
            if (wings != null) wings.localPosition = new Vector3(0, 2.15f, -0.35f);
        }

        public bool UpgradePet()
        {
            int lv = Data.petLevel;
            if (lv >= Upgrades.MaxPet) return false;
            if (lv == 0)
            {
                if (Data.gold < Upgrades.PetAdoptCost) return false;
                Data.gold -= Upgrades.PetAdoptCost;
            }
            else
            {
                if (Data.petFood < Upgrades.PetFood(lv) || Data.gold < Upgrades.PetGold(lv)) return false;
                Data.petFood -= Upgrades.PetFood(lv);
                Data.gold -= Upgrades.PetGold(lv);
            }
            Data.petLevel++;
            Quests.Check(this);
            Commit();
            return true;
        }

        public void RefreshLook()
        {
            if (gear != null) gear.Apply(GearVisual.TiersOf(Data));
            if (aura != null) aura.SetLevel(GlowPlus);
        }

        // Takes the item in an equipment slot (Data.equipped index) off and puts it in the bag.
        public string Unequip(int index)
        {
            var it = Data.equipped[index];
            if (it.IsEmpty) return "Nothing there";
            if (Data.bag.Count >= GameData.BagSize) return "Bag is full";
            Data.bag.Add(it);
            Data.equipped[index] = new Item();
            Hp = Mathf.Min(Hp, MaxHp);
            RefreshLook();
            Commit();
            return "Took off " + it.Label;
        }

        // ---- Potions ----

        public bool UsePotion()
        {
            if (IsDead || Data.potions <= 0 || potionCd > 0 || Hp >= MaxHp) return false;
            int heal = Mathf.RoundToInt(MaxHp * Upgrades.PotionHeal);
            Hp = Mathf.Min(MaxHp, Hp + heal);
            Data.potions--;
            potionCd = PotionCooldown;
            FloatingText.Spawn(transform.position + Vector3.up * 2.4f, "+" + heal, new Color(0.4f, 1f, 0.4f), 30);
            Commit();
            return true;
        }

        public string BuyPotions(int count)
        {
            int cost = Upgrades.PotionPrice(Data.level) * count;
            if (Data.gold < cost) return "Need " + cost + "g";
            Data.gold -= cost;
            Data.potions += count;
            Commit();
            return "Bought " + count + " HP Potions for " + cost + "g";
        }

        // ---- Daily dungeons ----

        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        public int BloodCastleLeft => Upgrades.BloodCastleRuns - (Data.bcDate == Today ? Data.bcRuns : 0);
        public int DevilSquareLeft => Upgrades.DevilSquareRuns - (Data.dsDate == Today ? Data.dsRuns : 0);

        public bool UseRun(bool bloodCastle)
        {
            if ((bloodCastle ? BloodCastleLeft : DevilSquareLeft) <= 0) return false;
            if (bloodCastle)
            {
                if (Data.bcDate != Today) { Data.bcDate = Today; Data.bcRuns = 0; }
                Data.bcRuns++;
            }
            else
            {
                if (Data.dsDate != Today) { Data.dsDate = Today; Data.dsRuns = 0; }
                Data.dsRuns++;
            }
            Commit();
            return true;
        }

        public void AddMaterials(int gold, int exp, int bless, int soul, int feathers, int flames)
        {
            Data.gold += gold;
            Data.jewelBless += bless;
            Data.jewelSoul += soul;
            Data.feathers += feathers;
            Data.flames += flames;
            AddExp(exp);
            Commit();
        }

        public void Refill() => Hp = MaxHp;

        public void Teleport(Vector3 position, Rect bounds)
        {
            transform.position = position;
            Bounds = bounds;
            target = null;
            pvpTarget = null;
            QuestZone = null;
            if (Pet.Instance != null) Pet.Instance.SnapToOwner();
        }

        public void ResetSave()
        {
            SaveSystem.Delete();
            Data = GameData.NewSave();
            BuildBody();
            Hp = MaxHp;
            Maps.Enter(this, 0, true);
            Commit();
        }

        public void Commit()
        {
            RecalcBonus();
            dirty = true;
            Changed?.Invoke();
        }

        void Flush()
        {
            SaveSystem.Save(Data);
            dirty = false;
            saveTimer = 5f;
        }

        void OnApplicationPause(bool paused) { if (paused && dirty) Flush(); }
        void OnApplicationQuit() { if (dirty) Flush(); }

        void Respawn()
        {
            Hp = MaxHp;
            if (DungeonRun.Active != null) DungeonRun.Active.Leave();
            Maps.ReturnToTown(this);
        }

        protected override Color DamageColor(bool crit) => new Color(1f, 0.3f, 0.3f);

        protected override void OnDamaged(Combatant source) => lastHitTime = Time.time;

        protected override void Die(Combatant killer)
        {
            respawnTimer = RespawnDelay;
            target = null;
            pvpTarget = null;
            buffUntil = 0;
            bool byPlayer = killer == null && lastAttackerSession != null && Time.time - lastAttackerTime < 6f;
            if (byPlayer)
            {
                if (WorldSync.Instance != null) WorldSync.Instance.SendKilled(lastAttackerSession, IsRed);
                HUD.Message("Killed by " + lastAttackerName, 3f);
            }
            if (IsRed) RedPenalty();
            lastAttackerSession = null;
            Commit();
        }
    }
}
