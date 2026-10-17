using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    public class Monster : Combatant
    {
        public static readonly List<Monster> All = new List<Monster>();

        const float AggroRadius = 5f;
        const float LeashBeyondZone = 6f; // how far past its zone's edge a monster will chase
        const float AttackRange = 1.8f;
        const float AttackInterval = 1.2f;

        public int Level { get; private set; }
        public bool IsBoss { get; private set; }
        int RewardMul => IsBoss ? 10 : 1;
        public int ExpReward => (8 + Level * 6) * RewardMul;
        public int GoldReward => Random.Range(5 + Level * 2, 8 + Level * 4) * RewardMul;

        int maxHp, atk, def;
        public override int MaxHp => maxHp;
        public override int Atk => atk;
        public override int Def => def;

        MonsterSpawner spawner;
        Combatant aggro;
        Vector3 home, wanderTarget;
        float attackTimer, wanderTimer;
        bool returningHome;
        Transform hpBar, hpFill;

        public static Monster Create(MonsterSpawner spawner, Vector3 position)
        {
            var root = new GameObject(spawner.MonsterName + " Lv" + spawner.Level);
            root.transform.position = position;

            float s = spawner.Scale;
            var model = ModelLibrary.Spawn(spawner.MonsterName, root.transform, s * 1.4f, out var animator);
            if (model == null)
            {
                var body = GameObject.CreatePrimitive(spawner.Level >= 20 ? PrimitiveType.Cube : PrimitiveType.Sphere);
                Destroy(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0, s * 0.5f, 0);
                body.transform.localScale = Vector3.one * s;
                Paint.Color(body, spawner.Color);
            }

            var m = root.AddComponent<Monster>();
            m.anim = animator;
            m.spawner = spawner;
            m.Level = spawner.Level;
            m.maxHp = 40 + m.Level * 30;
            m.atk = 6 + m.Level * 4;
            m.def = m.Level * 2;
            m.Hp = m.maxHp;
            m.home = position;
            m.wanderTarget = position;
            m.BuildHpBar(model != null ? s * 1.4f + 0.4f : s + 0.5f);
            return m;
        }

        // Dungeon boss: much tougher, hits harder, and pays out ten times the rewards.
        public void MakeBoss()
        {
            IsBoss = true;
            maxHp *= 12;
            atk = Mathf.RoundToInt(atk * 1.5f);
            Hp = maxHp;
        }

        void BuildHpBar(float height)
        {
            hpBar = new GameObject("HpBar").transform;
            hpBar.SetParent(transform, false);
            hpBar.localPosition = new Vector3(0, height, 0);
            MakeBarPiece(new Color(0.1f, 0.1f, 0.1f), Vector3.zero, new Vector3(1.2f, 0.14f, 0.04f));
            hpFill = MakeBarPiece(new Color(0.9f, 0.15f, 0.15f), new Vector3(0, 0, -0.03f), new Vector3(1.2f, 0.1f, 0.04f)).transform;
        }

        GameObject MakeBarPiece(Color c, Vector3 localPos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(hpBar, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            Paint.Color(go, c);
            return go;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            if (IsDead) return;
            float dt = Time.deltaTime;
            attackTimer -= dt;

            var player = Player.Instance;
            if (aggro == null && !returningHome && player != null && !player.IsDead && FlatDistance(player) < AggroRadius)
                aggro = player;

            Vector3 fromHome = transform.position - home;
            fromHome.y = 0;
            Vector3 fromZone = transform.position - spawner.transform.position;
            fromZone.y = 0;
            if (aggro != null && (aggro.IsDead || fromZone.magnitude > spawner.Radius + LeashBeyondZone))
            {
                aggro = null;
                returningHome = true;
            }

            if (aggro != null)
            {
                if (FlatDistance(aggro) > AttackRange)
                    Step(aggro.transform.position - transform.position, 3.5f);
                else if (attackTimer <= 0)
                {
                    attackTimer = AttackInterval;
                    PlayAction(ModelAnimator.Anim.Attack, 0.8f);
                    aggro.TakeDamage(RollDamage(atk, 1f, out bool crit), crit, this);
                }
            }
            else if (returningHome)
            {
                Step(home - transform.position, 6f);
                Hp = maxHp;
                if (fromHome.magnitude < 0.5f) returningHome = false;
            }
            else
            {
                wanderTimer -= dt;
                if (wanderTimer <= 0)
                {
                    wanderTimer = Random.Range(2f, 5f);
                    Vector2 r = Random.insideUnitCircle * 3f;
                    wanderTarget = home + new Vector3(r.x, 0, r.y);
                }
                Vector3 d = wanderTarget - transform.position;
                d.y = 0;
                if (d.magnitude > 0.2f) Step(d, 1.5f);
            }
            KeepApart(dt);
        }

        float BodyRadius => spawner.Scale * 0.5f;

        // Nudge away from other monsters and the player so they surround instead of stacking.
        void KeepApart(float dt)
        {
            Vector3 push = Vector3.zero;
            foreach (var o in All)
            {
                if (o == this || o.IsDead) continue;
                push += Separation(o.transform.position, BodyRadius + o.BodyRadius);
            }
            var player = Player.Instance;
            if (player != null && !player.IsDead)
                push += Separation(player.transform.position, BodyRadius + 0.6f);
            if (push.sqrMagnitude > 0.0001f)
                transform.position += Vector3.ClampMagnitude(push, 1f) * Mathf.Min(1f, 10f * dt);
        }

        Vector3 Separation(Vector3 other, float minDist)
        {
            Vector3 d = transform.position - other;
            d.y = 0;
            float m = d.magnitude;
            if (m >= minDist) return Vector3.zero;
            if (m < 0.001f) d = new Vector3(Random.value - 0.5f, 0, Random.value - 0.5f);
            return d.normalized * (minDist - m);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (IsDead) return;
            // The camera never rotates around Y, so an unrotated bar always faces it.
            hpBar.rotation = Quaternion.identity;
            float t = (float)Hp / maxHp;
            hpFill.localScale = new Vector3(1.2f * t, 0.1f, 0.04f);
            hpFill.localPosition = new Vector3(-0.6f * (1f - t), 0, -0.03f);
        }

        protected override Color DamageColor(bool crit) => crit ? new Color(1f, 0.9f, 0.2f) : Color.white;

        protected override void OnDamaged(Combatant source)
        {
            aggro = source;
            returningHome = false;
            if (!IsDead) PlayAction(ModelAnimator.Anim.Hit, 0.3f);
        }

        protected override void Die(Combatant killer)
        {
            if (Player.Instance != null) Player.Instance.OnKill(this);
            spawner.NotifyDeath(this);
            hpBar.gameObject.SetActive(false);
            // Leave the body briefly so a death animation can play.
            Destroy(gameObject, anim != null ? 1.5f : 0f);
        }
    }
}
