using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class Player : Combatant
    {
        public static Player Instance { get; private set; }

        public static readonly string[] SkillNames = { "Slash", "Whirl", "Nova" };
        public static readonly float[] SkillCooldowns = { 3f, 6f, 12f };

        const float MoveSpeed = 6f;
        const float AttackRange = 2.2f;
        const float AttackInterval = 0.6f;
        const float AggroRange = 14f;
        const float RespawnDelay = 3f;

        public PlayerSave Data { get; private set; }
        public bool AutoBattle { get; set; }
        public Vector2 MoveInput { get; set; } // written by the on-screen joystick
        public event System.Action Changed;

        readonly float[] skillCd = new float[3];
        Monster target;
        Vector3 spawnPoint;
        float attackTimer, respawnTimer, lastHitTime, regenAcc, saveTimer;
        bool dirty;

        public override int Atk => 10 + Data.level * 3 + EquipStat(Slot.Weapon);
        public override int Def => Data.level + EquipStat(Slot.Armor) + EquipStat(Slot.Helm) + EquipStat(Slot.Boots);
        public override int MaxHp => 100 + Data.level * 25 + Def * 3;
        public int CombatPower => Atk * 5 + Def * 4 + MaxHp / 2;
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

            var model = ModelLibrary.Spawn("Player", root.transform, 1.9f, out var animator);
            if (model != null)
            {
                var p = root.AddComponent<Player>();
                p.anim = animator;
                return p;
            }

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.GetComponent<Renderer>().material.color = new Color(0.25f, 0.45f, 0.95f);

            var sword = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(sword.GetComponent<Collider>());
            sword.transform.SetParent(root.transform, false);
            sword.transform.localPosition = new Vector3(0.55f, 1.1f, 0.45f);
            sword.transform.localScale = new Vector3(0.12f, 0.12f, 1.3f);
            sword.GetComponent<Renderer>().material.color = new Color(0.85f, 0.85f, 0.9f);

            return root.AddComponent<Player>();
        }

        void Awake()
        {
            Instance = this;
            Data = SaveSystem.Load();
            Hp = MaxHp;
            spawnPoint = transform.position;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            saveTimer -= dt;
            if (dirty && saveTimer <= 0) Flush();

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

            Vector3 toTarget = target.transform.position - transform.position;
            if (FlatDistance(target) > AttackRange)
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

        Monster FindNearest(float maxDistance)
        {
            Monster best = null;
            float bestDist = maxDistance;
            foreach (var m in Monster.All)
            {
                if (m.IsDead) continue;
                float d = FlatDistance(m);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        // Called by the Attack button: lock on to the nearest monster and keep hitting it.
        public void AttackPressed()
        {
            if (IsDead) return;
            if (target == null || target.IsDead || FlatDistance(target) > AttackRange)
                target = FindNearest(AttackRange + 1.5f);
            if (target != null) BasicAttack();
        }

        void BasicAttack()
        {
            if (attackTimer > 0 || target == null || target.IsDead || FlatDistance(target) > AttackRange + 1.5f) return;
            attackTimer = AttackInterval;
            PlayAction(ModelAnimator.Anim.Attack, AttackInterval * 0.9f);
            target.TakeDamage(RollDamage(Atk, 1f, out bool crit), crit, this);
        }

        public float SkillCooldownRatio(int i) => skillCd[i] / SkillCooldowns[i];

        public bool UseSkill(int i)
        {
            if (IsDead || skillCd[i] > 0) return false;
            switch (i)
            {
                case 0:
                    var t = target != null && !target.IsDead && FlatDistance(target) <= AttackRange + 1f ? target : FindNearest(AttackRange + 1f);
                    if (t == null) return false;
                    target = t;
                    t.TakeDamage(RollDamage(Atk, 2.5f, out bool crit), crit, this);
                    Effect.Ring(t.transform.position, 1.2f, new Color(1f, 0.6f, 0.2f));
                    break;
                case 1:
                    if (!HitArea(3.5f, 1.6f)) return false;
                    Effect.Ring(transform.position, 3.5f, new Color(0.3f, 0.9f, 1f));
                    break;
                default:
                    if (!HitArea(6f, 3f)) return false;
                    Effect.Ring(transform.position, 6f, new Color(0.7f, 0.4f, 1f));
                    break;
            }
            skillCd[i] = SkillCooldowns[i];
            PlayAction(ModelAnimator.Anim.Attack, 0.5f);
            return true;
        }

        bool HitArea(float radius, float multiplier)
        {
            var hits = new List<Monster>();
            foreach (var m in Monster.All)
                if (!m.IsDead && FlatDistance(m) <= radius) hits.Add(m);
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

            if (Random.value < 0.35f)
            {
                var item = GameData.RollDrop(m.Level);
                if (Data.bag.Count >= GameData.BagSize)
                {
                    Data.gold += item.SellPrice;
                    FloatingText.Spawn(p + Vector3.up, "Bag full: sold " + item.name, Color.gray, 24);
                }
                else
                {
                    Data.bag.Add(item);
                    FloatingText.Spawn(p + Vector3.up, item.name, GameData.RarityColor(item.rarity), 30);
                }
            }

            while (Data.exp >= GameData.ExpToNext(Data.level))
            {
                Data.exp -= GameData.ExpToNext(Data.level);
                Data.level++;
                Hp = MaxHp;
                FloatingText.Spawn(transform.position + Vector3.up * 3f, "LEVEL UP! " + Data.level, new Color(0.4f, 1f, 0.4f), 52);
            }
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

        public void ResetSave()
        {
            SaveSystem.Delete();
            Data = GameData.NewSave();
            Hp = MaxHp;
            transform.position = spawnPoint;
            Commit();
        }

        void Commit()
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
            transform.position = spawnPoint;
            Hp = MaxHp;
            target = null;
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
