using UnityEngine;

namespace MuProto
{
    // Shared HP / damage logic for the player and monsters.
    public abstract class Combatant : MonoBehaviour
    {
        public int Hp { get; protected set; }
        public abstract int MaxHp { get; }
        public abstract int Atk { get; }
        public abstract int Def { get; }
        public bool IsDead => Hp <= 0;

        // Walkable area; the field by default, the arena while inside a dungeon.
        public static readonly Rect FieldBounds = new Rect(-48f, -48f, 96f, 96f);
        public Rect Bounds = FieldBounds;

        protected ModelAnimator anim; // null when using placeholder shapes
        bool movedThisFrame;
        float actionLockUntil;

        public bool InAction => Time.time < actionLockUntil; // attacking or flinching

        public static int RollDamage(int atk, float multiplier, out bool crit)
        {
            crit = Random.value < 0.1f;
            float dmg = atk * multiplier * Random.Range(0.9f, 1.1f);
            if (crit) dmg *= 1.5f;
            return Mathf.RoundToInt(dmg);
        }

        public void TakeDamage(int raw, bool crit, Combatant source)
        {
            if (IsDead) return;
            int dmg = Mathf.Max(1, raw - Def / 2);
            Hp = Mathf.Max(0, Hp - dmg);
            FloatingText.Spawn(transform.position + Vector3.up * 2f, crit ? dmg + "!" : dmg.ToString(), DamageColor(crit), crit ? 44 : 34);
            OnDamaged(source);
            if (Hp == 0) Die(source);
        }

        public float FlatDistance(Component other)
        {
            Vector3 d = other.transform.position - transform.position;
            d.y = 0;
            return d.magnitude;
        }

        protected void Step(Vector3 direction, float speed)
        {
            direction.y = 0;
            if (direction.sqrMagnitude < 0.0001f) return;
            direction.Normalize();
            Vector3 p = transform.position + direction * speed * Time.deltaTime;
            p.x = Mathf.Clamp(p.x, Bounds.xMin, Bounds.xMax);
            p.z = Mathf.Clamp(p.z, Bounds.yMin, Bounds.yMax);
            transform.position = p;
            movedThisFrame = true;
            Face(direction);
        }

        protected void Face(Vector3 direction)
        {
            direction.y = 0;
            if (direction.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), 15f * Time.deltaTime);
        }

        // One-shot animation (attack, hit) that holds off idle/run for lockTime seconds.
        protected void PlayAction(ModelAnimator.Anim a, float lockTime)
        {
            if (anim == null) return;
            anim.Play(a, true, lockTime);
            actionLockUntil = Time.time + lockTime;
        }

        protected virtual void LateUpdate()
        {
            if (anim != null)
            {
                if (IsDead) anim.Play(ModelAnimator.Anim.Die);
                else if (Time.time >= actionLockUntil) anim.Play(movedThisFrame ? ModelAnimator.Anim.Move : ModelAnimator.Anim.Idle);
            }
            movedThisFrame = false;
        }

        protected abstract Color DamageColor(bool crit);
        protected virtual void OnDamaged(Combatant source) { }
        protected abstract void Die(Combatant killer);
    }
}
