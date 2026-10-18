using System.Collections.Generic;
using UnityEngine;

namespace MuProto
{
    // A glowing ball that flies to its target and does something on arrival (usually deals damage).
    public class Projectile : MonoBehaviour
    {
        const float Speed = 28f;

        Transform target;
        System.Func<bool> stillValid;
        System.Action onHit;

        public static void Fire(Vector3 from, Monster target, int damage, bool crit, Combatant source, Color color, float ringRadius)
        {
            Fire(from, target.transform, color, () => target != null && !target.IsDead, () =>
            {
                target.TakeDamage(damage, crit, source);
                if (ringRadius > 0) Effect.Ring(target.transform.position, ringRadius, color);
            });
        }

        public static void Fire(Vector3 from, Transform target, Color color, System.Func<bool> stillValid, System.Action onHit)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = "Projectile";
            go.transform.position = from;
            go.transform.localScale = Vector3.one * 0.35f;
            Paint.Color(go, color);
            var p = go.AddComponent<Projectile>();
            p.target = target;
            p.stillValid = stillValid;
            p.onHit = onHit;
        }

        void Update()
        {
            if (target == null || (stillValid != null && !stillValid())) { Destroy(gameObject); return; }
            Vector3 aim = target.position + Vector3.up;
            Vector3 d = aim - transform.position;
            float step = Speed * Time.deltaTime;
            if (d.magnitude <= step)
            {
                onHit?.Invoke();
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
}
