"""
Rig + animate the low-poly monsters (Goblin, Orc, Golem) and export FBX for Unity.

Works with Blender 4.2 or newer. Two ways to run:

  1) Command line (recommended):
       blender -b -P rig_monsters.py -- "C:/path/to/YourUnityProject/Assets/Resources/Models"

  2) Inside Blender: open the Scripting tab, open this file, set MODELS_DIR below, press Run Script.

For each monster folder it reads <Name>/<Name>.obj and writes <Name>/<Name>_Rigged.fbx
with an armature and five actions: Idle, Walk, Attack, Hit, Die.
The game picks the rigged FBX automatically (it prefers models that have an Animator).
"""
import bpy, sys, os, math
from mathutils import Vector, Quaternion, Matrix

MODELS_DIR = r""  # e.g. r"C:\Users\me\MyGame\Assets\Resources\Models"  (used when no command-line path is given)
ONLY = []          # e.g. ["Orc"] to rig just one; empty = all three

FPS = 24

# ------------------------------------------------------------------ helpers
def B(x, y, z):
    """OBJ/Unity coords (x, y-up, z-forward) -> Blender coords (x, -z, y)."""
    return Vector((x, -z, y))

def seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return (p - (a + ab * t)).length

def qaxis(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.images):
        for d in list(coll):
            coll.remove(d)

# ------------------------------------------------------------------ creature specs
# joints are given in the same coordinates the models were built in (Unity-style: y up, z forward)
def humanoid(root_h, hips, spine, chest, neck, head, sh, elbow, wrist, fist, hip_j, knee, ankle, toe):
    j = {
        "Root": ((0, 0, 0), root_h),
        "Hips": (root_h, hips),
        "Spine": (hips, spine),
        "Chest": (spine, chest),
        "Neck": (neck[0], neck[1]),
        "Head": (neck[1], head),
    }
    for side, sx in ((".L", 1), (".R", -1)):
        m = lambda p: (p[0] * sx, p[1], p[2])
        j["UpperArm" + side] = (m(sh), m(elbow))
        j["Forearm" + side] = (m(elbow), m(wrist))
        j["Hand" + side] = (m(wrist), m(fist))
        j["Thigh" + side] = (m(hip_j), m(knee))
        j["Shin" + side] = (m(knee), m(ankle))
        j["Foot" + side] = (m(ankle), m(toe))
    return j

PARENT = {"Root": None, "Hips": "Root", "Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck"}
for s in (".L", ".R"):
    PARENT.update({"UpperArm" + s: "Chest", "Forearm" + s: "UpperArm" + s, "Hand" + s: "Forearm" + s,
                   "Thigh" + s: "Hips", "Shin" + s: "Thigh" + s, "Foot" + s: "Shin" + s})

def radii(torso, head, neck, upper, fore, hand, thigh, shin, foot):
    r = {"Root": 0.0001, "Hips": torso * 0.8, "Spine": torso, "Chest": torso, "Neck": neck, "Head": head}
    for s in (".L", ".R"):
        r.update({"UpperArm" + s: upper, "Forearm" + s: fore, "Hand" + s: hand, "Thigh" + s: thigh, "Shin" + s: shin, "Foot" + s: foot})
    return r

def side(x):  # +x in model space is the ".L" side
    return ".L" if x >= 0 else ".R"

SPECS = {
    "Goblin": dict(
        palette=["skin", "skin2", "skin3", "cloth", "leather", "eye", "pupil", "tooth", "nail", "metal", "rope", "mouth"],
        joints=humanoid((0, 0.45, 0), (0, 0.58, 0), (0, 0.70, 0), (0, 0.80, 0), ((0, 0.80, 0), (0, 0.88, 0.02)), (0, 1.10, 0.04),
                        (0.16, 0.75, -0.01), (0.32, 0.58, 0.0), (0.42, 0.40, 0.04), (0.46, 0.27, 0.06),
                        (0.08, 0.43, -0.01), (0.10, 0.25, 0.04), (0.095, 0.06, 0.0), (0.095, 0.03, 0.15)),
        radii=radii(0.18, 0.20, 0.08, 0.06, 0.05, 0.06, 0.08, 0.06, 0.07),
        rules=lambda c, p: ("Hips" if c in ("cloth", "metal") or (c == "leather" and p[1] < 0.56) else
                            "Head" if c in ("skin3", "eye", "pupil", "tooth", "mouth") or (c == "leather" and p[1] > 1.04)
                                      or (p[1] > 0.88 and c in ("skin", "skin2")) else
                            "Chest" if c == "leather" else None),
        attack_arms=[".L"], scale=1.0),
    "Orc": dict(
        palette=["skin", "skin2", "paint", "leather", "cloth", "metal", "metal2", "tusk", "eye", "hair", "wood", "mouth"],
        joints=humanoid((0, 0.82, 0), (0, 0.98, 0.02), (0, 1.15, 0), (0, 1.42, -0.02), ((0, 1.42, 0), (0, 1.50, 0.06)), (0, 1.76, 0.10),
                        (0.36, 1.37, -0.01), (0.52, 1.00, 0.03), (0.58, 0.70, 0.10), (0.61, 0.55, 0.13),
                        (0.15, 0.80, 0.0), (0.17, 0.47, 0.02), (0.17, 0.10, 0.0), (0.17, 0.04, 0.22)),
        radii=radii(0.30, 0.16, 0.10, 0.10, 0.09, 0.09, 0.11, 0.09, 0.10),
        rules=lambda c, p: ("Hand.L" if c in ("wood", "metal", "metal2") and p[0] > 0.5 else
                            "UpperArm" + side(p[0]) if c in ("metal", "metal2") and abs(p[0]) > 0.25 and p[1] > 1.25 else
                            "Hips" if c in ("cloth", "metal") or (c == "leather" and 0.84 < p[1] < 0.96 and abs(p[0]) < 0.29) else
                            "Chest" if c == "leather" and p[1] > 0.92 and abs(p[0]) < 0.4 else
                            "Head" if c in ("tusk", "eye", "hair", "mouth") or (p[1] > 1.45 and abs(p[0]) < 0.27 and p[2] > -0.02 and c != "leather") else
                            "Chest" if c == "paint" else None),
        attack_arms=[".L"], scale=1.0),
    "Golem": dict(
        palette=["stone", "stone2", "stone3", "dark", "moss", "moss2", "rune", "eye", "crystal", "lichen"],
        joints=humanoid((0, 0.80, 0), (0, 1.00, 0), (0, 1.25, 0), (0, 1.55, 0), ((0, 1.55, 0.05), (0, 1.66, 0.10)), (0, 1.96, 0.13),
                        (0.66, 1.52, 0.0), (0.82, 1.06, 0.05), (0.87, 0.55, 0.12), (0.87, 0.18, 0.22),
                        (0.25, 0.78, 0.0), (0.27, 0.42, 0.01), (0.27, 0.10, 0.04), (0.27, 0.05, 0.36)),
        radii=radii(0.50, 0.20, 0.10, 0.20, 0.25, 0.25, 0.20, 0.20, 0.20),
        rules=lambda c, p: ("UpperArm" + side(p[0]) if abs(p[0]) > 0.40 and p[1] > 1.32 else
                            "Chest" if p[2] < -0.26 and p[1] > 1.0 else
                            "Head" if abs(p[0]) < 0.24 and p[1] > 1.64 and p[2] > 0.0 else None),
        attack_arms=[".L", ".R"], scale=1.0),
}

# ------------------------------------------------------------------ build armature + skin
def build_armature(name, spec):
    arm_data = bpy.data.armatures.new(name + "_Armature")
    arm = bpy.data.objects.new("Armature", arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    for bone, (h, t) in spec["joints"].items():
        eb = arm_data.edit_bones.new(bone)
        eb.head = B(*h)
        eb.tail = B(*t)
        if (eb.tail - eb.head).length < 1e-3:
            eb.tail = eb.head + Vector((0, 0, 0.05))
        eb.roll = 0.0
    for bone, parent in PARENT.items():
        if parent:
            eb = arm_data.edit_bones[bone]
            eb.parent = arm_data.edit_bones[parent]
            eb.use_connect = False
    # a still top bone: Unity mis-applies scale/motion keyed on the topmost bone, so Root gets a parent
    origin = arm_data.edit_bones.new("Origin")
    origin.head, origin.tail, origin.roll = B(0, 0, 0), B(0, 0.05, 0), 0.0
    arm_data.edit_bones["Root"].parent = origin
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm

def face_color(mesh, poly, palette, uv):
    if uv is None:
        return None
    u, v = uv.data[poly.loop_indices[0]].uv
    cx, cy = min(3, max(0, int(u * 4))), min(3, max(0, int((1 - v) * 4)))
    i = cy * 4 + cx
    return palette[i] if i < len(palette) else None

def skin(obj, arm, spec):
    mesh = obj.data
    uv = mesh.uv_layers.active
    groups = {b: obj.vertex_groups.new(name=b) for b in spec["joints"] if b != "Root"}
    segs = {b: (B(*h), B(*t)) for b, (h, t) in spec["joints"].items() if b != "Root"}
    mw = obj.matrix_world
    for poly in mesh.polygons:
        c_world = mw @ poly.center
        p_model = (c_world.x, c_world.z, -c_world.y)  # back to y-up / z-forward for the rules
        color = face_color(mesh, poly, spec["palette"], uv)
        bone = spec["rules"](color, p_model)
        if bone is None:
            bone = min(segs, key=lambda b: seg_dist(c_world, *segs[b]) / spec["radii"][b])
        groups[bone].add(list(poly.vertices), 1.0, "REPLACE")
    obj.parent = arm
    obj.matrix_parent_inverse = arm.matrix_world.inverted()
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm

# ------------------------------------------------------------------ animation
FWD = (1, 0, 0)    # rotating about +X tips the top of a bone toward -Y, which is the model's front
TWIST = (0, 0, 1)
ROLL = (0, 1, 0)

def make_action(arm, act_name, length, keys, loop):
    """keys: {frame: {bone: (world_quat or None, world_offset Vector or None)}}"""
    pose = arm.pose
    act = bpy.data.actions.new(act_name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    frames = sorted(set(keys) | {0, length})
    for f in frames:
        k = keys.get(f, keys.get(0, {}) if (loop and f == length) else {})
        for pb in pose.bones:
            pb.rotation_mode = "QUATERNION"
            rest = pb.bone.matrix_local.to_quaternion()
            q_world, off = k.get(pb.name, (None, None))
            pb.rotation_quaternion = (rest.inverted() @ q_world @ rest) if q_world else Quaternion()
            pb.location = (rest.inverted() @ Vector(off)) if off else Vector()
            pb.keyframe_insert("rotation_quaternion", frame=f, group=pb.name)
            pb.keyframe_insert("location", frame=f, group=pb.name)
    act.frame_range = (0, length)
    return act

def animations(spec, height):
    L, R = ".L", ".R"
    atk = spec["attack_arms"]
    acts = {}
    # Idle: breathe, sway arms (2 s loop)
    acts["Idle"] = (48, True, {
        0:  {"Chest": (qaxis(FWD, 0), None)},
        24: {"Chest": (qaxis(FWD, 3), None), "Head": (qaxis(FWD, -3), None),
             "UpperArm.L": (qaxis(ROLL, -4), None), "UpperArm.R": (qaxis(ROLL, 4), None),
             "Hips": (None, (0, 0, -0.012 * height))},
    })
    # Walk: 1 s loop, legs and arms swing opposite
    def walk(t, s):
        return {"Thigh.L": (qaxis(FWD, -28 * s), None), "Thigh.R": (qaxis(FWD, 28 * s), None),
                "Shin.L": (qaxis(FWD, 30 * max(0, s)), None), "Shin.R": (qaxis(FWD, 30 * max(0, -s)), None),
                "UpperArm.L": (qaxis(FWD, 22 * s), None), "UpperArm.R": (qaxis(FWD, -22 * s), None),
                "Chest": (qaxis(TWIST, 6 * s) @ qaxis(FWD, 6), None),
                "Hips": (None, (0, 0, 0.03 * height * (1 - abs(s))))}
    acts["Walk"] = (24, True, {0: walk(0, 1), 6: walk(6, 0), 12: walk(12, -1), 18: walk(18, 0)})
    # Attack: wind up, smash down, recover (1 s)
    wind, hit = {}, {}
    for s in atk:
        wind["UpperArm" + s] = (qaxis(FWD, -150), None)
        wind["Forearm" + s] = (qaxis(FWD, -30), None)
        hit["UpperArm" + s] = (qaxis(FWD, -55), None)
        hit["Forearm" + s] = (qaxis(FWD, -10), None)
    wind["Chest"] = (qaxis(FWD, -12) @ qaxis(TWIST, 15 if len(atk) == 1 else 0), None)
    hit["Chest"] = (qaxis(FWD, 22) @ qaxis(TWIST, -10 if len(atk) == 1 else 0), None)
    hit["Hips"] = (None, (0, 0, -0.03 * height))
    acts["Attack"] = (24, False, {0: {}, 9: wind, 13: hit, 17: hit, 24: {}})
    # Hit: recoil backwards (0.5 s)
    acts["Hit"] = (12, False, {0: {}, 3: {"Chest": (qaxis(FWD, -20), None), "Head": (qaxis(FWD, -15), None),
                                          "UpperArm.L": (qaxis(ROLL, -15), None), "UpperArm.R": (qaxis(ROLL, 15), None)}, 12: {}})
    # Die: buckle and fall backwards (1.5 s, holds last pose)
    fall = {"Root": (qaxis(FWD, -88), (0, 0, 0.05 * height)),
            "Thigh.L": (qaxis(FWD, -35), None), "Thigh.R": (qaxis(FWD, -20), None),
            "Shin.L": (qaxis(FWD, 45), None), "Shin.R": (qaxis(FWD, 30), None),
            "UpperArm.L": (qaxis(ROLL, -60), None), "UpperArm.R": (qaxis(ROLL, 60), None),
            "Head": (qaxis(FWD, -20), None)}
    knees = {"Thigh.L": (qaxis(FWD, -30), None), "Thigh.R": (qaxis(FWD, -30), None),
             "Shin.L": (qaxis(FWD, 50), None), "Shin.R": (qaxis(FWD, 50), None),
             "Chest": (qaxis(FWD, 25), None), "Root": (None, (0, 0, -0.08 * height))}
    acts["Die"] = (36, False, {0: {}, 10: knees, 26: fall, 36: fall})
    return acts

# ------------------------------------------------------------------ main
def rig_one(name, folder):
    spec = SPECS[name]
    obj_path = os.path.join(folder, name, name + ".obj")
    if not os.path.exists(obj_path):
        print(f"[skip] {obj_path} not found")
        return
    clear_scene()
    bpy.context.scene.render.fps = FPS
    bpy.ops.wm.obj_import(filepath=obj_path, forward_axis="NEGATIVE_Z", up_axis="Y")
    obj = [o for o in bpy.context.selected_objects if o.type == "MESH"][0]
    obj.name = name
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.shade_flat()
    for mat in obj.data.materials:  # crisp palette in Blender's viewport too
        if mat and mat.use_nodes:
            for n in mat.node_tree.nodes:
                if n.type == "TEX_IMAGE":
                    n.interpolation = "Closest"
    height = max((obj.matrix_world @ Vector(c)).z for c in obj.bound_box)

    arm = build_armature(name, spec)
    skin(obj, arm, spec)

    for act_name, (length, loop, keys) in animations(spec, height).items():
        make_action(arm, act_name, length, keys, loop)
    arm.animation_data.action = bpy.data.actions["Idle"]
    for pb in arm.pose.bones:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector()

    out = os.path.join(folder, name, name + "_Rigged.fbx")
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.export_scene.fbx(
        filepath=out, use_selection=False, object_types={"ARMATURE", "MESH"},
        apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
        add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
        bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode="AUTO", mesh_smooth_type="FACE")
    print(f"[ok] {name}: {out}")

def main():
    folder = MODELS_DIR
    if "--" in sys.argv:
        args = sys.argv[sys.argv.index("--") + 1:]
        if args:
            folder = args[0]
    if not folder or not os.path.isdir(folder):
        raise SystemExit("Set MODELS_DIR (or pass it after --) to your Unity project's Assets/Resources/Models folder")
    for name in (ONLY or list(SPECS)):
        rig_one(name, folder)
    print("Done. Switch back to Unity and press Play.")

main()
