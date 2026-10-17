using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // A glowing ball that flies to a monster and deals its damage on arrival.
    public class Projectile : MonoBehaviour
    {
        const float Speed = 28f;

        Monster target;
        Combatant source;
        int damage;
        bool crit;
        Color color;
        float ringRadius;

        public static void Fire(Vector3 from, Monster target, int damage, bool crit, Combatant source, Color color, float ringRadius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Projectile";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.35f;
            Paint.Color(go, color);
            var p = go.AddComponent<Projectile>();
            p.target = target;
            p.source = source;
            p.damage = damage;
            p.crit = crit;
            p.color = color;
            p.ringRadius = ringRadius;
        }

        void Update()
        {
            if (target == null || target.IsDead) { Destroy(gameObject); return; }
            Vector3 aim = target.transform.position + Vector3.up;
            Vector3 d = aim - transform.position;
            float step = Speed * Time.deltaTime;
            if (d.magnitude <= step)
            {
                target.TakeDamage(damage, crit, source);
                if (ringRadius > 0) Effect.Ring(target.transform.position, ringRadius, color);
                Destroy(gameObject);
                return;
            }
            transform.position += d.normalized * step;
        }
    }

    // Follows the player and shoots whatever the player is fighting.
    public class Pet : MonoBehaviour
    {
        public static Pet Instance { get; private set; }

        const float AttackInterval = 1.5f;
        const float AttackRange = 12f;

        Player owner;
        Transform visual;
        bool isModel;
        int builtLevel = -1;
        float attackTimer;
        ModelAnimator anim;   // null for the placeholder ball
        bool walking;
        float actionUntil;

        public int Damage => 5 + owner.Data.petLevel * 4 + owner.Atk / 10;

        public static void Create(Player owner)
        {
            var p = new GameObject("Pet").AddComponent<Pet>();
            p.owner = owner;
            Instance = p;
            p.SnapToOwner();
        }

        // Trot along behind the owner's left shoulder, on the ground.
        Vector3 Slot
        {
            get
            {
                var p = owner.transform.position + owner.transform.rotation * new Vector3(-1.1f, 0, -1.1f);
                p.y = owner.transform.position.y;
                return p;
            }
        }

        public void SnapToOwner()
        {
            if (owner != null) transform.position = Slot;
        }

        void Rebuild()
        {
            builtLevel = owner.Data.petLevel;
            if (visual != null) Destroy(visual.gameObject);
            visual = null;
            if (builtLevel <= 0) return;

            visual = MakeVisual(transform, out anim, out isModel);
        }

        // The pet's look: the Pet model, or a little yellow ball with a beak when it is missing.
        // Also used for other players' pets seen online (RemotePet).
        public static Transform MakeVisual(Transform parent, out ModelAnimator anim, out bool isModel)
        {
            var visual = new GameObject("Visual").transform;
            visual.SetParent(parent, false);
            isModel = ModelLibrary.Spawn("Pet", visual, 0.9f, out anim) != null;
            if (isModel) return visual;

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(visual, false);
            ball.transform.localScale = Vector3.one * 0.6f;
            Paint.Color(ball, new Color(1f, 0.85f, 0.3f));
            var beak = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(beak.GetComponent<Collider>());
            beak.transform.SetParent(visual, false);
            beak.transform.localPosition = new Vector3(0, 0, 0.32f);
            beak.transform.localScale = new Vector3(0.15f, 0.1f, 0.2f);
            Paint.Color(beak, new Color(1f, 0.5f, 0.1f));
            return visual;
        }

        void Update()
        {
            if (owner == null) return;
            if (owner.Data.petLevel != builtLevel) Rebuild();
            if (builtLevel <= 0) return;

            float dt = Time.deltaTime;
            Vector3 to = Slot - transform.position;
            to.y = 0;
            float dist = to.magnitude;
            if (dist > 25f)
            {
                SnapToOwner();
                walking = false;
            }
            else
            {
                // Start walking once it falls behind, stop when it arrives (the gap avoids jitter).
                if (dist > 1.2f) walking = true;
                else if (dist < 0.25f) walking = false;
                if (walking)
                {
                    float speed = Mathf.Clamp(dist * 2.5f, 2f, 9f); // hurry when far behind
                    transform.position += to / dist * Mathf.Min(dist, speed * dt);
                    transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to / dist), 10f * dt);
                }
                else
                    transform.rotation = Quaternion.Slerp(transform.rotation, owner.transform.rotation, 4f * dt);
            }
            if (anim != null && Time.time >= actionUntil)
                anim.Play(walking ? ModelAnimator.Anim.Move : ModelAnimator.Anim.Idle);
            if (!isModel) visual.localPosition = Vector3.up * (0.8f + Mathf.Sin(Time.time * 4f) * 0.12f);

            attackTimer -= dt;
            var t = owner.Target;
            if (attackTimer > 0 || owner.IsDead || t == null || t.IsDead) return;
            Vector3 d = t.transform.position - transform.position;
            d.y = 0;
            if (d.magnitude > AttackRange) return;
            attackTimer = AttackInterval;
            if (anim != null && !walking)
            {
                if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d.normalized);
                anim.Play(ModelAnimator.Anim.Attack, true, 0.6f);
                actionUntil = Time.time + 0.6f;
            }
            int dmg = Combatant.RollDamage(Damage, 1f, out bool crit);
            Projectile.Fire(transform.position + Vector3.up * 0.8f, t, dmg, crit, owner, new Color(1f, 0.6f, 0.8f), 0f);
        }
    }

    // Daily dungeon: an arena away from the field with two waves and a boss, against the clock.
    public class Dungeon : MonoBehaviour
    {
        public static Dungeon Active { get; private set; }

        public const int Waves = 3;
        const float TimeLimit = 120f;
        static readonly Vector3 Center = new Vector3(300f, 0, 0);
        static readonly Rect ArenaBounds = new Rect(286f, -14f, 28f, 28f);
        static GameObject arena;

        MonsterSpawner minions, boss;
        int alive;
        bool finished;

        public int Wave { get; private set; }
        public float TimeLeft { get; private set; }

        public static bool Enter()
        {
            var p = Player.Instance;
            if (Active != null || p == null || p.IsDead || !p.HasClass) return false;
            if (!p.UseDungeonRun()) return false;
            BuildArena();
            Active = new GameObject("Dungeon Run").AddComponent<Dungeon>();
            Active.Begin(p);
            return true;
        }

        static void BuildArena()
        {
            if (arena != null) return;
            arena = GameObject.CreatePrimitive(PrimitiveType.Plane);
            arena.name = "Dungeon Arena";
            arena.transform.position = Center;
            arena.transform.localScale = new Vector3(3.2f, 1, 3.2f);
            Paint.Color(arena, new Color(0.28f, 0.24f, 0.3f));
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    var pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(pillar.GetComponent<Collider>());
                    pillar.transform.SetParent(arena.transform, true);
                    pillar.transform.position = Center + new Vector3(x * 15f, 2f, z * 15f);
                    pillar.transform.localScale = new Vector3(1.5f, 4f, 1.5f) / 3.2f;
                    Paint.Color(pillar, new Color(0.45f, 0.4f, 0.5f));
                }
        }

        void Begin(Player p)
        {
            int lv = p.Data.level;
            string name = lv < 5 ? "Slime" : lv < 10 ? "Goblin" : lv < 20 ? "Orc" : "Golem";
            minions = MonsterSpawner.Create(name, lv, Center + new Vector3(0, 0, 5f), 8f, 0, new Color(0.6f, 0.3f, 0.7f), 1.1f, false);
            boss = MonsterSpawner.Create("Golem", lv + 3, Center + new Vector3(0, 0, 7f), 0.5f, 0, new Color(0.35f, 0.3f, 0.4f), 2.6f, false);
            foreach (var s in new[] { minions, boss })
            {
                s.Bounds = ArenaBounds;
                s.Died += OnMonsterDied;
            }
            p.Teleport(Center + new Vector3(0, 0, -10f), ArenaBounds);
            TimeLeft = TimeLimit;
            NextWave();
        }

        void NextWave()
        {
            Wave++;
            if (Wave < Waves)
            {
                int n = Wave == 1 ? 6 : 8;
                for (int i = 0; i < n; i++) minions.Spawn();
                alive += n;
                HUD.Message("Wave " + Wave + " / " + Waves, 2f);
            }
            else
            {
                boss.Spawn().MakeBoss();
                for (int i = 0; i < 3; i++) minions.Spawn();
                alive += 4;
                HUD.Message("BOSS!", 2f);
            }
        }

        void OnMonsterDied(Monster m)
        {
            alive--;
            if (alive > 0 || finished) return;
            if (Wave < Waves) NextWave();
            else Finish(true);
        }

        void Update()
        {
            if (finished) return;
            TimeLeft -= Time.deltaTime;
            var p = Player.Instance;
            if (p == null || p.IsDead || TimeLeft <= 0) Finish(false);
        }

        public void Leave()
        {
            if (!finished) Finish(false);
        }

        void Finish(bool won)
        {
            finished = true;
            var p = Player.Instance;
            if (won)
            {
                int lv = p.Data.level;
                int gold = 200 + lv * 40;
                p.AddMaterials(gold, 5, 5);
                var rarity = Random.value < 0.3f ? Rarity.Epic : Rarity.Rare;
                p.GiveItem(GameData.MakeItem((Slot)Random.Range(0, 4), rarity, lv), p.transform.position + Vector3.up * 3f);
                p.Commit();
                HUD.Message("Dungeon cleared!  +" + gold + "g  +5 Feathers  +5 Pet Food", 4f);
            }
            else
                HUD.Message(TimeLeft <= 0 ? "Time's up! Dungeon failed" : "Dungeon failed", 3f);

            foreach (var m in new List<Monster>(Monster.All))
                if (m.Bounds == ArenaBounds) Destroy(m.gameObject);
            Destroy(minions.gameObject);
            Destroy(boss.gameObject);
            if (p != null && !p.IsDead) p.Teleport(Vector3.zero, Combatant.FieldBounds);
            Active = null;
            Destroy(gameObject);
        }
    }
}
