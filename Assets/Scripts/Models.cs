using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace MuProto
{
    // Loads optional 3D models from Assets/Resources/Models/<name>.prefab.
    // Anything missing falls back to the built-in placeholder shapes.
    public static class ModelLibrary
    {
        static readonly Dictionary<string, GameObject> cache = new Dictionary<string, GameObject>();
        static Shader urpLit;

        public static GameObject Find(string name)
        {
            if (!cache.TryGetValue(name, out var prefab))
            {
                prefab = Resources.Load<GameObject>("Models/" + name);
                cache[name] = prefab;
            }
            return prefab;
        }

        // Instantiates the model under parent, scaled to targetHeight with its feet at the parent's origin.
        public static GameObject Spawn(string name, Transform parent, float targetHeight, out ModelAnimator animator)
        {
            animator = null;
            var prefab = Find(name);
            if (prefab == null) return null;

            var model = Object.Instantiate(prefab, parent, false);
            model.name = name + " Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;

            foreach (var c in model.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            foreach (var rb in model.GetComponentsInChildren<Rigidbody>()) Object.Destroy(rb);
            FixMaterials(model);

            if (TryBounds(model, out Bounds b) && b.size.y > 0.01f)
            {
                model.transform.localScale *= targetHeight / b.size.y;
                TryBounds(model, out b);
                model.transform.position += Vector3.up * (parent.position.y - b.min.y);
            }

            animator = ModelAnimator.Attach(model);
            return model;
        }

        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // Many free Asset Store models still use the old "Standard" shader, which shows up pink in URP.
        static void FixMaterials(GameObject go)
        {
            if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Lit");
            if (urpLit == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.materials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null) continue;
                    if (m.shader.name != "Standard" && m.shader.isSupported) continue;
                    var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    var fixedMat = new Material(urpLit) { name = m.name + " (URP)" };
                    fixedMat.SetTexture("_BaseMap", tex);
                    fixedMat.SetColor("_BaseColor", color);
                    mats[i] = fixedMat;
                }
                r.materials = mats;
            }
        }
    }

    // Plays a model's animation clips by guessing from their names (idle, run, attack, hit, die),
    // so any model pack that ships an Animator Controller works without setup.
    public class ModelAnimator : MonoBehaviour
    {
        public enum Anim { Idle, Move, Attack, Hit, Die }

        static readonly string[][] Keywords =
        {
            new[] { "idle", "stand" },
            new[] { "run", "walk", "move" },
            new[] { "attack", "slash", "punch", "bite" },
            new[] { "hit", "damage", "hurt" },
            new[] { "die", "death", "dead" },
        };

        const float FadeTime = 0.15f;

        readonly AnimationClip[] clips = new AnimationClip[5];
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        int active;
        float fade = 1f;
        Anim current = (Anim)(-1);

        public static ModelAnimator Attach(GameObject model)
        {
            var animator = model.GetComponentInChildren<Animator>();
            if (animator == null || animator.runtimeAnimatorController == null) return null;

            var all = animator.runtimeAnimatorController.animationClips;
            var ma = model.AddComponent<ModelAnimator>();
            for (int i = 0; i < Keywords.Length; i++) ma.clips[i] = Pick(all, Keywords[i]);
            if (ma.clips[0] == null && all.Length > 0) ma.clips[0] = all[0];
            for (int i = 1; i < ma.clips.Length; i++)
                if (ma.clips[i] == null && i != (int)Anim.Hit) ma.clips[i] = ma.clips[0];

            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null; // the playable graph below drives the model instead
            ma.graph = PlayableGraph.Create(model.name);
            ma.mixer = AnimationMixerPlayable.Create(ma.graph, 2);
            AnimationPlayableOutput.Create(ma.graph, "Anim", animator).SetSourcePlayable(ma.mixer);
            ma.graph.Play();
            ma.Play(Anim.Idle);
            return ma;
        }

        static AnimationClip Pick(AnimationClip[] all, string[] words)
        {
            foreach (var w in words)
                foreach (var c in all)
                    if (c != null && c.name.ToLowerInvariant().Contains(w)) return c;
            return null;
        }

        // fitInto > 0 speeds the clip up so it finishes within that many seconds.
        public void Play(Anim a, bool restart = false, float fitInto = 0f)
        {
            if (a == current && !restart) return;
            var clip = clips[(int)a];
            if (clip == null) return;

            int next = 1 - active;
            var old = mixer.GetInput(next);
            if (old.IsValid())
            {
                graph.Disconnect(mixer, next);
                old.Destroy();
            }
            var p = AnimationClipPlayable.Create(graph, clip);
            if (fitInto > 0 && clip.length > fitInto) p.SetSpeed(clip.length / fitInto);
            graph.Connect(p, 0, mixer, next);
            active = next;
            current = a;
            fade = 0f;
        }

        void Update()
        {
            if (!graph.IsValid()) return;
            fade = Mathf.Min(1f, fade + Time.deltaTime / FadeTime);
            mixer.SetInputWeight(active, fade);
            mixer.SetInputWeight(1 - active, 1f - fade);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
