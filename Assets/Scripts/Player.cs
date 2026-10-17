using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class Player : Combatant
    {
        public static Player Instance { get; private set; }

        const float MoveSpeed = 6f;
        const float AggroRange = 14f;
        const float RespawnDelay = 3f;

        public PlayerSave Data { get; private set; }
        public bool AutoBattle { get; set; }
        public Vector2 MoveInput { get; set; } // written by the on-screen joystick
        public event System.Action Changed;

        readonly float[] skillCd = new float[3];
        Monster target;
        Vector3 spawnPoint;
        Transform body, wings;
        float attackTimer, respawnTimer, lastHitTime, regenAcc, saveTimer;
        bool dirty;

        public bool HasClass => !string.IsNullOrEmpty(Data.classId);
        public ClassDef Class => Classes.Get(Data.classId);
        public Monster Target => target;

        public override int Atk => Mathf.RoundToInt((10 + Data.level * 3 + EquipStat(Slot.Weapon)) * Class.AtkMul) + Data.wingsLevel * 4;
        public override int Def => Mathf.RoundToInt((Data.level + EquipStat(Slot.Armor) + EquipStat(Slot.Helm) + EquipStat(Slot.Boots)) * Class.DefMul) + Data.wingsLevel * 2;
        public override int MaxHp => Mathf.RoundToInt((100 + Data.level * 25 + Def * 3) * Class.HpMul) + Data.wingsLevel * 30;
        public int CombatPower => Atk * 5 + Def * 4 + MaxHp / 2 + Data.petLevel * 25;
        public float RespawnIn => respawnTimer;

        int EquipStat(Slot s)
        {
            var it = Data.equipped[(int)s];
            return it.IsEmpty ? 0 : it.Stat;
        }

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
            Hp = MaxHp;
            spawnPoint = transform.position;
        }

        void BuildBody()
        {
            if (body != null) Destroy(body.gameObject);
            body = MakeBody(transform, Class, out anim);
            BuildWings();
        }

        void BuildWings()
        {
            if (wings != null) Destroy(wings.gameObject);
            wings = MakeWings(transform, Data.wingsLevel);
        }

        // Model per class (Resources/Models/Knight, Mage, Archer, or Player), else placeholder shapes.
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
            if (cls.Id == "Mage")
            {
                Part(body, PrimitiveType.Cylinder, new Vector3(0.55f, 1.1f, 0.2f), new Vector3(0.08f, 1f, 0.08f), new Color(0.45f, 0.3f, 0.15f));
                Part(body, PrimitiveType.Sphere, new Vector3(0.55f, 2.15f, 0.2f), Vector3.one * 0.25f, new Color(0.4f, 0.8f, 1f));
            }
            else if (cls.Id == "Archer")
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

        // Two blades behind the back that grow and change colour every 10 levels. Null below level 1.
        public static Transform MakeWings(Transform parent, int lv)
        {
            if (lv <= 0) return null;
            var wings = new GameObject("Wings").transform;
            wings.SetParent(parent, false);
            wings.localPosition = new Vector3(0, 1.35f, -0.35f);
            float size = 0.8f + Mathf.Min(lv, 50) * 0.02f;
            var color = Upgrades.WingColor(lv);
            for (int side = -1; side <= 1; side += 2)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(w.GetComponent<Collider>());
                w.transform.SetParent(wings, false);
                w.transform.localPosition = new Vector3(side * 0.55f * size, 0.2f, -0.1f);
                w.transform.localRotation = Quaternion.Euler(0, side * 25f, side * -30f);
                w.transform.localScale = new Vector3(1.1f * size, 0.6f * size, 0.04f);
                Paint.Color(w, color);
            }
            return wings;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            saveTimer -= dt;
            if (dirty && saveTimer <= 0) Flush();
            if (!HasClass) return;

            if (IsDead)
            {
                respawnTimer -= dt;
                if (respawnTimer <= 0) Respawn();
                return;
            }

            for (int i = 0; i < skillCd.Length; i++) skillCd[i] = Mathf.Max(0, skillCd[i] - dt);
            attackTimer -= dt;
            Regenerate(dt);

            Vector2 input = MoveInput + KeyboardInput();
            if (input.sqrMagnitude > 1f) input.Normalize();

            if (input.sqrMagnitude > 0.01f)
            {
                Step(new Vector3(input.x, 0, input.y), MoveSpeed * input.magnitude);
                if (!AutoBattle) target = null;
                return;
            }

            if (AutoBattle && (target == null || target.IsDead))
                target = FindNearest(AggroRange) ?? FindNearest(200f);

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
                    if (UseSkill(i)) return;
            BasicAttack();
        }

        void Regenerate(float dt)
        {
            float rate = Time.time - lastHitTime > 3f ? 0.05f : 0.01f;
            regenAcc += MaxHp * rate * dt;
            int whole = (int)regenAcc;
            if (whole > 0)
            {
                regenAcc -= whole;
                Hp = Mathf.Min(MaxHp, Hp + whole);
            }
        }

        static Vector2 KeyboardInput()
        {
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

        Monster FindNearest(float maxDistance) => FindNearest(transform.position, maxDistance);

        static Monster FindNearest(Vector3 from, float maxDistance)
        {
            Monster best = null;
            float bestDist = maxDistance;
            foreach (var m in Monster.All)
            {
                if (m.IsDead) continue;
                float d = Flat(m.transform.position - from);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        static float Flat(Vector3 v) { v.y = 0; return v.magnitude; }

        Monster TargetWithin(float range) =>
            target != null && !target.IsDead && FlatDistance(target) <= range ? target : FindNearest(range);

        // Called by the Attack button: lock on to the nearest monster and keep hitting it.
        public void AttackPressed()
        {
            if (IsDead || !HasClass) return;
            target = TargetWithin(Class.AttackRange + 1.5f);
            if (target != null) BasicAttack();
        }

        void BasicAttack()
        {
            if (attackTimer > 0 || target == null || target.IsDead || FlatDistance(target) > Class.AttackRange + 1.5f) return;
            attackTimer = Class.AttackInterval;
            PlayAction(ModelAnimator.Anim.Attack, Class.AttackInterval * 0.9f);
            Deal(target, 1f, Class.BodyColor, false);
        }

        // Melee hits land now; ranged ones fly as a projectile and land on arrival.
        void Deal(Monster m, float multiplier, Color color, bool ringOnHit)
        {
            int dmg = RollDamage(Atk, multiplier, out bool crit);
            if (Class.Ranged)
                Projectile.Fire(transform.position + Vector3.up * 1.3f, m, dmg, crit, this, color, ringOnHit ? 1.2f : 0f);
            else
            {
                m.TakeDamage(dmg, crit, this);
                if (ringOnHit) Effect.Ring(m.transform.position, 1.2f, color);
            }
        }

        public float SkillCooldownRatio(int i) => skillCd[i] / Class.Skills[i].Cooldown;

        public bool UseSkill(int i)
        {
            if (IsDead || !HasClass || skillCd[i] > 0) return false;
            var sk = Class.Skills[i];
            switch (sk.Kind)
            {
                case SkillKind.Single:
                {
                    var t = TargetWithin(sk.Range);
                    if (t == null) return false;
                    target = t;
                    Deal(t, sk.Multiplier, sk.Color, true);
                    break;
                }
                case SkillKind.SelfArea:
                    if (!HitArea(transform.position, sk.Radius, sk.Multiplier)) return false;
                    Effect.Ring(transform.position, sk.Radius, sk.Color);
                    break;
                case SkillKind.TargetArea:
                {
                    var t = TargetWithin(sk.Range);
                    if (t == null) return false;
                    target = t;
                    Vector3 c = t.transform.position;
                    HitArea(c, sk.Radius, sk.Multiplier);
                    Effect.Ring(c, sk.Radius, sk.Color);
                    break;
                }
                case SkillKind.Multi:
                {
                    var hits = new List<Monster>();
                    foreach (var m in Monster.All)
                        if (!m.IsDead && FlatDistance(m) <= sk.Range) hits.Add(m);
                    if (hits.Count == 0) return false;
                    hits.Sort((a, b) => FlatDistance(a).CompareTo(FlatDistance(b)));
                    for (int n = 0; n < hits.Count && n < (int)sk.Radius; n++) Deal(hits[n], sk.Multiplier, sk.Color, false);
                    break;
                }
            }
            skillCd[i] = sk.Cooldown;
            PlayAction(ModelAnimator.Anim.Attack, 0.5f);
            return true;
        }

        bool HitArea(Vector3 center, float radius, float multiplier)
        {
            var hits = new List<Monster>();
            foreach (var m in Monster.All)
                if (!m.IsDead && Flat(m.transform.position - center) <= radius) hits.Add(m);
            foreach (var m in hits)
                m.TakeDamage(RollDamage(Atk, multiplier, out bool crit), crit, this);
            return hits.Count > 0;
        }

        public void OnKill(Monster m)
        {
            Vector3 p = m.transform.position + Vector3.up * 2.6f;
            Data.exp += m.ExpReward;
            int gold = m.GoldReward;
            Data.gold += gold;
            FloatingText.Spawn(p, "+" + m.ExpReward + " EXP  +" + gold + "g", new Color(1f, 0.85f, 0.3f), 26);

            if (Random.value < 0.35f) GiveItem(GameData.RollDrop(m.Level), p + Vector3.up);
            if (Random.value < 0.15f) { Data.feathers++; FloatingText.Spawn(p + Vector3.up * 1.6f, "+1 Feather", Upgrades.WingColor(11), 24); }
            if (Random.value < 0.15f) { Data.petFood++; FloatingText.Spawn(p + Vector3.up * 2.2f, "+1 Pet Food", new Color(1f, 0.6f, 0.8f), 24); }

            AddExp(0);
            Commit();
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
            Data.exp += amount;
            while (Data.exp >= GameData.ExpToNext(Data.level))
            {
                Data.exp -= GameData.ExpToNext(Data.level);
                Data.level++;
                Hp = MaxHp;
                FloatingText.Spawn(transform.position + Vector3.up * 3f, "LEVEL UP! " + Data.level, new Color(0.4f, 1f, 0.4f), 52);
            }
        }

        public void ChooseClass(string id)
        {
            Data.classId = id;
            BuildBody();
            Hp = MaxHp;
            Commit();
        }

        public void Equip(int bagIndex)
        {
            var item = Data.bag[bagIndex];
            var current = Data.equipped[(int)item.slot];
            Data.equipped[(int)item.slot] = item;
            Data.bag.RemoveAt(bagIndex);
            if (!current.IsEmpty) Data.bag.Insert(bagIndex, current);
            Hp = Mathf.Min(Hp, MaxHp);
            Commit();
        }

        public int SellCommons()
        {
            int sold = 0;
            for (int i = Data.bag.Count - 1; i >= 0; i--)
            {
                if (Data.bag[i].rarity != Rarity.Common) continue;
                Data.gold += Data.bag[i].SellPrice;
                Data.bag.RemoveAt(i);
                sold++;
            }
            Commit();
            return sold;
        }

        // Returns null when the attempt could not be made (no item, max level, not enough gold).
        public bool? Enhance(Slot slot)
        {
            var item = Data.equipped[(int)slot];
            if (!item.CanEnhance || Data.gold < item.EnhanceCost) return null;
            Data.gold -= item.EnhanceCost;
            bool success = Random.value < item.EnhanceChance;
            if (success) item.plus++;
            Commit();
            return success;
        }

        public bool UpgradeWings()
        {
            int lv = Data.wingsLevel;
            if (lv >= Upgrades.MaxWings || Data.feathers < Upgrades.WingFeathers(lv) || Data.gold < Upgrades.WingGold(lv)) return false;
            Data.feathers -= Upgrades.WingFeathers(lv);
            Data.gold -= Upgrades.WingGold(lv);
            Data.wingsLevel++;
            BuildWings();
            Commit();
            return true;
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
            Commit();
            return true;
        }

        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        public int DungeonRunsLeft => Upgrades.DungeonRunsPerDay - (Data.dungeonDate == Today ? Data.dungeonRuns : 0);

        public bool UseDungeonRun()
        {
            if (DungeonRunsLeft <= 0) return false;
            if (Data.dungeonDate != Today) { Data.dungeonDate = Today; Data.dungeonRuns = 0; }
            Data.dungeonRuns++;
            Commit();
            return true;
        }

        public void AddMaterials(int gold, int feathers, int petFood)
        {
            Data.gold += gold;
            Data.feathers += feathers;
            Data.petFood += petFood;
            Commit();
        }

        public void Teleport(Vector3 position, Rect bounds)
        {
            transform.position = position;
            Bounds = bounds;
            target = null;
            if (Pet.Instance != null) Pet.Instance.SnapToOwner();
        }

        public void ResetSave()
        {
            SaveSystem.Delete();
            Data = GameData.NewSave();
            BuildBody();
            Hp = MaxHp;
            Teleport(spawnPoint, FieldBounds);
            Commit();
        }

        public void Commit()
        {
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
            Teleport(spawnPoint, FieldBounds);
        }

        protected override Color DamageColor(bool crit) => new Color(1f, 0.3f, 0.3f);

        protected override void OnDamaged(Combatant source) => lastHitTime = Time.time;

        protected override void Die(Combatant killer)
        {
            respawnTimer = RespawnDelay;
            target = null;
        }
    }
}
