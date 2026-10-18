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
                // Our own Blender export (Models/<Name>/<Name>_Rigged) wins over anything else in the folder.
                prefab = Resources.Load<GameObject>("Models/" + name + "/" + name + "_Rigged")
                         ?? Resources.Load<GameObject>("Models/" + name) ?? FirstInFolder("Models/" + name);
                cache[name] = prefab;
            }
            return prefab;
        }

        // Also accept a folder named after the slot (Models/Player/AnyPrefab), preferring an animated prefab,
        // then a rigged mesh (e.g. an FBX exported from Blender), then anything.
        static GameObject FirstInFolder(string path)
        {
            GameObject first = null, rigged = null;
            foreach (var go in Resources.LoadAll<GameObject>(path))
            {
                if (go.GetComponentInChildren<Animator>() != null) return go;
                if (rigged == null && go.GetComponentInChildren<SkinnedMeshRenderer>() != null) rigged = go;
                if (first == null) first = go;
            }
            return rigged ?? first;
        }

        // Instantiates the model under parent, scaled to targetHeight with its feet at the parent's origin.
        // procedural: give models without animation clips a simple code-driven bob / lunge / fall instead.
        public static GameObject Spawn(string name, Transform parent, float targetHeight, out ModelAnimator animator, bool procedural = true)
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
            FixMaterials(model, name);
            GearVisual.Attach(model, name); // hero models: hide the gear pieces until something is equipped

            if (TryBounds(model, out Bounds b) && b.size.y > 0.01f)
            {
                model.transform.localScale *= targetHeight / b.size.y;
                TryBounds(model, out b);
                model.transform.position += Vector3.up * (parent.position.y - b.min.y);
            }

            animator = ModelAnimator.Attach(model, procedural, name);
            return model;
        }

        static bool TryBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            bool hero = GearVisual.HasGear(go); // size heroes by the bare body, whatever they hold or wear
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (hero && r.name != "G_Body") continue;
                if (!any) { bounds = r.bounds; any = true; }
                else bounds.Encapsulate(r.bounds);
            }
            return any;
        }

        // Many free Asset Store models still use the old "Standard" shader, which shows up pink in URP.
        // A material saved in the model's folder with the same name as an imported one replaces it
        // (lets a plain OBJ/FBX ship a ready URP material next to it).
        static void FixMaterials(GameObject go, string slot)
        {
            var overrides = new Dictionary<string, List<Material>>();
            foreach (var m in Resources.LoadAll<Material>("Models/" + slot))
            {
                if (!overrides.TryGetValue(m.name, out var list)) overrides[m.name] = list = new List<Material>();
                list.Add(m);
            }

            if (urpLit == null) urpLit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var o = overrides.TryGetValue(m.name, out var same) ? same.Find(x => x != m) : null;
                    if (o != null) { mats[i] = o; continue; }
                    if (urpLit == null || m.shader == null) continue;
                    if (m.shader.name != "Standard" && m.shader.isSupported) continue;
                    var tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    var color = m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white;
                    var fixedMat = new Material(urpLit) { name = m.name };
                    fixedMat.SetTexture("_BaseMap", tex);
                    fixedMat.SetColor("_BaseColor", color);
                    mats[i] = fixedMat;
                }
                r.sharedMaterials = mats;
            }
        }
    }

    // Plays a model's animation clips by guessing from their names (idle, run, attack, hit, die),
    // so any model pack that ships an Animator Controller works without setup.
    // Models with no clips (e.g. a plain OBJ) get simple code-driven motion instead.
    public class ModelAnimator : MonoBehaviour
    {
        public enum Anim { Idle, Move, Attack, Hit, Die }

        static readonly string[][] Keywords =
        {
            new[] { "idle", "stand" },
            new[] { "run", "walk", "move" },
            new[] { "attack01", "attack", "slash", "punch", "bite" },
            new[] { "hit", "damage", "hurt" },
            new[] { "die", "death", "dead" },
        };

        const float FadeTime = 0.15f;
        const int Slots = 4; // clips that can be fading at once

        readonly AnimationClip[] clips = new AnimationClip[5];
        readonly Dictionary<string, AnimationClip> named = new Dictionary<string, AnimationClip>(); // every clip by name (lower case)
        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        // Mixer weights always add up to 1. If they don't, Unity blends the rig toward its default
        // pose and the model flickers for a frame (this happened when an attack or hit started
        // while the previous clip was still fading out).
        readonly float[] weights = new float[Slots];
        int active = -1;
        AnimationClipPlayable playing;
        Anim current = (Anim)(-1);

        // procedural mode
        bool procedural;
        Vector3 basePos, baseScale;
        Quaternion baseRot;
        float height, phase, actionStart, actionLen;

        // slot: the Resources/Models name, used to find clips inside a rigged FBX that has no Animator Controller.
        public static ModelAnimator Attach(GameObject model, bool allowProcedural = true, string slot = null)
        {
            var animator = model.GetComponentInChildren<Animator>();
            // A rigged FBX imported without an Animator still has clips next to it: give it one.
            if (animator == null && slot != null && model.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                animator = model.AddComponent<Animator>();
            var all = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips : null;
            if ((all == null || all.Length == 0) && animator != null && slot != null)
                all = System.Array.FindAll(Resources.LoadAll<AnimationClip>("Models/" + slot), c => !c.name.StartsWith("__preview__"));
            if (all == null || all.Length == 0)
                return allowProcedural ? AttachProcedural(model) : null;

            var ma = model.AddComponent<ModelAnimator>();
            foreach (var c in all) if (c != null) ma.named[c.name.ToLowerInvariant()] = c;
            for (int i = 0; i < Keywords.Length; i++) ma.clips[i] = Pick(all, Keywords[i]);
            if (ma.clips[0] == null && all.Length > 0) ma.clips[0] = all[0];
            for (int i = 1; i < ma.clips.Length; i++)
                if (ma.clips[i] == null && i != (int)Anim.Hit) ma.clips[i] = ma.clips[0];

            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null; // the playable graph below drives the model instead
            ma.graph = PlayableGraph.Create(model.name);
            ma.mixer = AnimationMixerPlayable.Create(ma.graph, Slots);
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

        static ModelAnimator AttachProcedural(GameObject model)
        {
            var ma = model.AddComponent<ModelAnimator>();
            ma.procedural = true;
            var t = model.transform;
            ma.basePos = t.localPosition;
            ma.baseRot = t.localRotation;
            ma.baseScale = t.localScale;
            ma.height = 0.1f;
            foreach (var r in model.GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer)) ma.height = Mathf.Max(ma.height, r.bounds.size.y);
            ma.phase = Random.value * 10f;
            ma.Play(Anim.Idle);
            return ma;
        }

        // fitInto > 0 speeds the clip up so it finishes within that many seconds.
        public void Play(Anim a, bool restart = false, float fitInto = 0f)
        {
            if (a == current && !restart) return;
            if (procedural)
            {
                current = a;
                actionStart = Time.time;
                actionLen = fitInto > 0 ? fitInto : (a == Anim.Hit ? 0.3f : a == Anim.Die ? 0.6f : 0.7f);
                return;
            }
            var clip = clips[(int)a];
            if (clip == null) return;
            StartClip(clip, a, fitInto);
        }

        public bool HasClip(string name) => !procedural && named.ContainsKey(name.ToLowerInvariant());

        // A one-shot clip by name (combo steps, skills), played like an attack. False if the model has none.
        public bool PlayClip(string name, float fitInto = 0f)
        {
            if (procedural || !graph.IsValid() || !named.TryGetValue(name.ToLowerInvariant(), out var clip)) return false;
            StartClip(clip, Anim.Attack, fitInto);
            return true;
        }

        void StartClip(AnimationClip clip, Anim a, float fitInto)
        {

            // Use a free slot; if every slot is still fading, reuse the faintest one and hand its
            // weight to the clip playing now so nothing ever drops out of the blend.
            int slot = -1;
            for (int i = 0; i < Slots && slot < 0; i++)
                if (i != active && weights[i] <= 0f) slot = i;
            if (slot < 0)
            {
                float min = float.MaxValue;
                for (int i = 0; i < Slots; i++)
                    if (i != active && weights[i] < min) { min = weights[i]; slot = i; }
                weights[active] += weights[slot];
                weights[slot] = 0f;
            }
            var old = mixer.GetInput(slot);
            if (old.IsValid())
            {
                graph.Disconnect(mixer, slot);
                old.Destroy();
            }
            var p = AnimationClipPlayable.Create(graph, clip);
            if (fitInto > 0 && clip.length > fitInto) p.SetSpeed(clip.length / fitInto);
            graph.Connect(p, 0, mixer, slot);
            bool first = active < 0;
            active = slot;
            playing = p;
            current = a;
            if (first) weights[slot] = 1f; // no fade-in from nothing on the very first clip
            Rebalance();
        }

        void Rebalance()
        {
            float others = 0f;
            for (int i = 0; i < Slots; i++)
                if (i != active) others += weights[i];
            weights[active] = Mathf.Max(0f, 1f - others);
            for (int i = 0; i < Slots; i++) mixer.SetInputWeight(i, weights[i]);
        }

        void Update()
        {
            if (procedural) { UpdateProcedural(); return; }
            if (!graph.IsValid()) return;
            // Idle and Move loop even when the clip was imported without Loop Time.
            if ((current == Anim.Idle || current == Anim.Move) && playing.IsValid())
            {
                float len = clips[(int)current].length;
                if (len > 0 && playing.GetTime() > len) playing.SetTime(playing.GetTime() % len);
            }
            float step = Time.deltaTime / FadeTime;
            for (int i = 0; i < Slots; i++)
                if (i != active) weights[i] = Mathf.Max(0f, weights[i] - step);
            Rebalance();
        }

        void UpdateProcedural()
        {
            float t = Time.time + phase, h = height;
            float k = Mathf.Clamp01((Time.time - actionStart) / actionLen);
            Vector3 pos = Vector3.zero, euler = Vector3.zero, scale = Vector3.one;
            switch (current)
            {
                case Anim.Idle: // breathe and sway
                    float b = Mathf.Sin(t * 3f);
                    scale = new Vector3(1f - 0.012f * b, 1f + 0.025f * b, 1f - 0.012f * b);
                    euler.z = 2f * Mathf.Sin(t * 1.3f);
                    break;
                case Anim.Move: // hop and waddle, leaning forward
                    float c = t * 11f;
                    pos.y = Mathf.Abs(Mathf.Sin(c)) * 0.07f * h;
                    euler.z = 7f * Mathf.Sin(c);
                    euler.x = 8f;
                    break;
                case Anim.Attack: // wind up, lunge, recover
                    float lean, fwd;
                    if (k < 0.35f) { float u = k / 0.35f; lean = -18f * u; fwd = -0.05f * h * u; }
                    else if (k < 0.55f) { float u = (k - 0.35f) / 0.2f; lean = Mathf.Lerp(-18f, 30f, u); fwd = Mathf.Lerp(-0.05f * h, 0.25f * h, u); }
                    else { float u = (k - 0.55f) / 0.45f; lean = Mathf.Lerp(30f, 0f, u); fwd = Mathf.Lerp(0.25f * h, 0f, u); }
                    euler.x = lean; pos.z = fwd;
                    break;
                case Anim.Hit: // recoil and squash
                    float s = Mathf.Sin(k * Mathf.PI);
                    euler.x = -15f * s; pos.z = -0.1f * h * s;
                    scale = new Vector3(1f + 0.08f * s, 1f - 0.12f * s, 1f + 0.08f * s);
                    break;
                case Anim.Die: // topple sideways
                    float e = 1f - Mathf.Pow(1f - k, 3f);
                    euler.z = 90f * e; pos.y = 0.08f * h * e;
                    break;
            }
            var tr = transform;
            tr.localPosition = basePos + baseRot * pos;
            tr.localRotation = baseRot * Quaternion.Euler(euler);
            tr.localScale = Vector3.Scale(baseScale, scale);
        }

        void OnDestroy()
        {
            if (graph.IsValid()) graph.Destroy();
        }
    }
}
