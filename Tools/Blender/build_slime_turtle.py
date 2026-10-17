"""
Model, rig and animate a Slime and a Turtle in Blender, then export FBX for Unity.

Blender 4.2+ (tested on 5.2). Run inside Blender's Python console or with:
    blender -b -P build_slime_turtle.py -- "C:/path/to/YourUnityProject/Assets/Resources/Models"

Writes  Models/Slime/Slime_Rigged.fbx    (monster, Lv1 zone)
        Models/Pet/Pet_Rigged.fbx        (cute turtle used as the player's pet)
(the Turtle monster is built by build_turtleshell.py)
plus preview PNGs in Assets/Tools/Previews.
Each FBX has actions Idle, Walk, Attack, Hit, Die.
"""
import bpy, bmesh, sys, os, math
from mathutils.bvhtree import BVHTree
from mathutils import Vector, Matrix, Quaternion

MODELS_DIR = r""   # e.g. r"D:\MMORPG\My project\Assets\Resources\Models"
ONLY = []          # e.g. ["Slime"]
FPS = 24

# Blender space: +Z up, -Y is the model's front (becomes Unity +Z after export).
FRONT = Vector((0, -1, 0))

# ---------------------------------------------------------------- scene helpers
def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.cameras):
        for d in list(coll):
            coll.remove(d)

def make_mat(name, rgb, rough=0.6, metal=0.0, emit=None, strength=1.5):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1.0)
    m.roughness = rough
    m.use_nodes = True
    bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*rgb, 1.0)
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
        if emit:
            bsdf.inputs["Emission Color"].default_value = (*emit, 1.0)
            bsdf.inputs["Emission Strength"].default_value = strength
    return m

def srgb(r, g, b):
    f = lambda c: (c / 255.0) ** 2.2
    return (f(r), f(g), f(b))

class Builder:
    """Collects parts into one bmesh with material index and per-vertex bone weights."""
    def __init__(self, bones):
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()
        self.bones = bones
        self.smooth = set()

    def _finish(self, verts, mat, bone, weight_fn, smooth):
        verts = list(verts)
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            f.material_index = mat
            if smooth:
                self.smooth.add(f)
        for v in verts:
            if weight_fn:
                for b, w in weight_fn(v.co).items():
                    if w > 1e-4:
                        v[self.dl][self.bones.index(b)] = w
            else:
                v[self.dl][self.bones.index(bone)] = 1.0
        return verts, faces

    def sphere(self, center, radii, mat, bone=None, u=12, v=8, weight_fn=None, smooth=False, rot=None, shape=None):
        M = Matrix.Translation(center) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*radii, 1))
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=1.0, matrix=M)
        if shape:
            for vt in ret["verts"]:
                vt.co = shape(vt.co)
        return self._finish(ret["verts"], mat, bone, weight_fn, smooth)

    def cone(self, base, tip, r1, r2, mat, bone, seg=8, smooth=False):
        base, tip = Vector(base), Vector(tip)
        d = tip - base
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
        M = Matrix.Translation((base + tip) / 2) @ rot.to_matrix().to_4x4()
        ret = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg,
                                    radius1=r1, radius2=r2, depth=d.length, matrix=M)
        return self._finish(ret["verts"], mat, bone, None, smooth)

    def to_object(self, name, mats):
        me = bpy.data.meshes.new(name)
        for f in self.bm.faces:
            f.smooth = f in self.smooth
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        self.bm.to_mesh(me)
        self.bm.free()
        for m in mats:
            me.materials.append(m)
        obj = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(obj)
        for b in self.bones:
            obj.vertex_groups.new(name=b)
        return obj

def smoothstep(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)

# ---------------------------------------------------------------- rig + animation helpers
def build_armature(name, joints, parents):
    data = bpy.data.armatures.new(name + "_Armature")
    arm = bpy.data.objects.new("Armature", data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    for o in bpy.context.view_layer.objects:
        o.select_set(o == arm)
    bpy.ops.object.mode_set(mode="EDIT")
    for b, (h, t) in joints.items():
        eb = data.edit_bones.new(b)
        eb.head, eb.tail, eb.roll = Vector(h), Vector(t), 0.0
    for b, p in parents.items():
        if p:
            data.edit_bones[b].parent = data.edit_bones[p]
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm

def bind(obj, arm):
    obj.parent = arm
    mod = obj.modifiers.new("Armature", "ARMATURE")
    mod.object = arm

def q(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)   # rotation about +X leans the top forward (toward -Y)

def make_action(arm, name, length, keys, loop):
    """keys: {frame: {bone: dict(r=world Quaternion, t=world offset, s=(sx, sy_along_bone, sz))}}"""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    frames = sorted(set(keys) | {0, length})
    for f in frames:
        k = keys.get(f, keys.get(0, {}) if (loop and f == length) else {})
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
            rest = pb.bone.matrix_local.to_quaternion()
            e = k.get(pb.name, {})
            pb.rotation_quaternion = (rest.inverted() @ e["r"] @ rest) if "r" in e else Quaternion()
            pb.location = (rest.inverted() @ Vector(e["t"])) if "t" in e else Vector()
            pb.scale = e.get("s", (1, 1, 1))
            for path in ("rotation_quaternion", "location", "scale"):
                pb.keyframe_insert(path, frame=f, group=pb.name)
    act.frame_range = (0, length)
    return act

FBX_ARGS = dict(
    use_selection=False, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode="AUTO", mesh_smooth_type="FACE")

def preview_dir(models_dir):
    """Previews go to Assets/Tools/Previews so they are not packed into the game build."""
    d = os.path.normpath(os.path.join(models_dir, "..", "..", "Tools", "Previews"))
    os.makedirs(d, exist_ok=True)
    return d

def export(arm, obj, path, actions):
    for name, (length, loop, keys) in actions.items():
        make_action(arm, name, length, keys, loop)
    arm.animation_data.action = bpy.data.actions["Idle"]
    bpy.context.scene.frame_set(0)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_ARGS)
    print("[ok]", path)

def preview(path, target, dist, height, frame_action=None, frame=0):
    scn = bpy.context.scene
    arm = bpy.data.objects["Armature"]
    if frame_action:
        arm.animation_data.action = bpy.data.actions[frame_action]
    scn.frame_set(frame)
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scn.collection.objects.link(cam)
    target = Vector(target)
    cam.location = target + Vector((dist * 0.75, -dist, height))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    scn.camera = cam
    scn.render.engine = "BLENDER_WORKBENCH"
    scn.display.shading.light = "STUDIO"
    scn.display.shading.color_type = "MATERIAL"
    scn.display.shading.show_shadows = True
    scn.display.shading.show_cavity = True
    scn.render.film_transparent = False
    scn.render.resolution_x = scn.render.resolution_y = 640
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
    print("[preview]", path)

# ---------------------------------------------------------------- SLIME
def build_slime(out_dir):
    clear_scene()
    bones = ["Root", "Base", "Top"]
    joints = {"Root": ((0, 0, 0), (0, 0, 0.12)), "Base": ((0, 0, 0.0), (0, 0, 0.32)), "Top": ((0, 0, 0.32), (0, 0, 0.82))}
    parents = {"Root": None, "Base": "Root", "Top": "Base"}
    mats = [make_mat("Slime_Body", srgb(96, 206, 92), rough=0.12, emit=srgb(20, 70, 18), strength=0.6),
            make_mat("Slime_Core", srgb(56, 150, 60), rough=0.3),
            make_mat("Slime_Eye", srgb(18, 22, 20), rough=0.1),
            make_mat("Slime_Shine", srgb(255, 255, 255), rough=0.2, emit=srgb(255, 255, 255), strength=1.0),
            make_mat("Slime_Blush", srgb(240, 130, 140), rough=0.5),
            make_mat("Slime_Mouth", srgb(40, 70, 36), rough=0.4)]
    B = Builder(bones)
    w_body = lambda co: {"Base": 1 - smoothstep(0.12, 0.55, co.z), "Top": smoothstep(0.12, 0.55, co.z)}

    def drop(co):  # unit sphere -> round squat drop with a flat bottom and a soft tip
        x, y, z = co.x, co.y, co.z
        z = z * 0.38 if z < 0 else z * 0.95 + 0.22 * z ** 8
        r = 0.5 * (1.0 + 0.06 * (1 - co.z * co.z))
        return Vector((x * r, y * r * 0.94, 0.30 + z * 0.5))
    B.sphere((0, 0, 0), (1, 1, 1), 0, u=28, v=18, weight_fn=w_body, smooth=True, shape=drop)
    tree = BVHTree.FromBMesh(B.bm)

    def on_front(x, z, sink):
        """Point on the body's front surface at height z, pushed `sink` into it."""
        hit, normal, _, _ = tree.ray_cast(Vector((x, -2.0, z)), Vector((0, 1, 0)))
        return hit - normal * sink, normal

    for i, (x, y, z, r) in enumerate([(0.22, 0.18, 0.22, 0.07), (-0.25, 0.1, 0.34, 0.05), (0.12, 0.27, 0.48, 0.04)]):
        B.sphere((x, y, z), (r, r, r), 1, u=8, v=6, weight_fn=w_body, smooth=True)
    for sx in (-1, 1):
        p, n = on_front(sx * 0.15, 0.44, 0.012)
        face = n.to_track_quat("Y", "Z") @ q(X, 0)
        B.sphere(p, (0.075, 0.03, 0.105), 2, "Top", u=14, v=10, smooth=True, rot=face)                # eyes
        p2, _ = on_front(sx * 0.15 - 0.025, 0.475, -0.012)
        B.sphere(p2, (0.026, 0.012, 0.032), 3, "Top", u=8, v=6, smooth=True, rot=face)                # eye shine
        p3, n3 = on_front(sx * 0.29, 0.33, 0.004)
        B.sphere(p3, (0.065, 0.015, 0.035), 4, "Top", u=8, v=6, smooth=True, rot=n3.to_track_quat("Y", "Z"))  # blush
    pm, nm = on_front(0, 0.33, 0.006)
    B.sphere(pm, (0.055, 0.015, 0.026), 5, "Top", u=10, v=6, smooth=True, rot=nm.to_track_quat("Y", "Z"))  # mouth
    obj = B.to_object("Slime", mats)
    arm = build_armature("Slime", joints, parents)
    bind(obj, arm)

    H = 0.82
    sq = lambda a: (1 + a, 1 - a, 1 + a)   # squash (+) / stretch (-) along the bone
    actions = {
        "Idle": (48, True, {0: {"Root": {"s": sq(0.0)}},
                            24: {"Root": {"s": sq(0.06)}, "Top": {"r": q(X, 3)}}}),
        "Walk": (20, True, {0: {"Root": {"s": sq(0.18)}},
                            5: {"Root": {"s": sq(-0.14), "t": (0, 0, 0.16 * H)}},
                            10: {"Root": {"s": sq(-0.04), "t": (0, 0, 0.24 * H)}, "Top": {"r": q(X, 6)}},
                            15: {"Root": {"s": sq(-0.10), "t": (0, 0, 0.10 * H)}}}),
        "Attack": (24, False, {0: {},
                               8: {"Root": {"s": sq(0.28), "t": (0, 0.06, 0)}, "Top": {"r": q(X, -18)}},
                               12: {"Root": {"s": sq(-0.22), "t": (0, -0.35, 0.18 * H)}, "Top": {"r": q(X, 28)}},
                               15: {"Root": {"s": sq(0.25), "t": (0, -0.3, 0)}, "Top": {"r": q(X, 10)}},
                               24: {}}),
        "Hit": (12, False, {0: {}, 3: {"Root": {"s": sq(0.3), "t": (0, 0.1, 0)}, "Top": {"r": q(X, -20)}},
                            7: {"Root": {"s": sq(-0.1)}}, 12: {}}),
        "Die": (36, False, {0: {}, 6: {"Root": {"s": sq(-0.15), "t": (0, 0, 0.1 * H)}},
                            14: {"Root": {"s": sq(0.4)}},
                            30: {"Root": {"s": (1.8, 0.14, 1.8)}}, 36: {"Root": {"s": (1.8, 0.14, 1.8)}}}),
    }
    export(arm, obj, os.path.join(out_dir, "Slime", "Slime_Rigged.fbx"), actions)
    preview(os.path.join(preview_dir(out_dir), "Slime_Preview.png"), (0, 0, 0.38), 1.55, 0.55)

# ---------------------------------------------------------------- TURTLE (pet)
def build_turtle(out_dir):
    clear_scene()
    bones = ["Root", "Body", "Head", "Leg.FL", "Leg.FR", "Leg.BL", "Leg.BR", "Tail"]
    joints = {"Root": ((0, 0, 0), (0, 0, 0.1)), "Body": ((0, 0, 0.1), (0, 0, 0.42)),
              "Head": ((0, -0.36, 0.2), (0, -0.62, 0.3)), "Tail": ((0, 0.38, 0.13), (0, 0.55, 0.08))}
    for side, sx in (("L", 1), ("R", -1)):
        joints["Leg.F" + side] = ((sx * 0.24, -0.2, 0.16), (sx * 0.33, -0.27, 0.0))
        joints["Leg.B" + side] = ((sx * 0.24, 0.2, 0.16), (sx * 0.33, 0.25, 0.0))
    parents = {"Root": None, "Body": "Root", "Head": "Body", "Tail": "Body",
               "Leg.FL": "Body", "Leg.FR": "Body", "Leg.BL": "Body", "Leg.BR": "Body"}
    mats = [make_mat("Turtle_Shell", srgb(52, 128, 86), rough=0.45),
            make_mat("Turtle_Plate", srgb(98, 176, 104), rough=0.4),
            make_mat("Turtle_Rim", srgb(196, 160, 92), rough=0.55),
            make_mat("Turtle_Skin", srgb(150, 205, 110), rough=0.6),
            make_mat("Turtle_Eye", srgb(18, 18, 22), rough=0.1),
            make_mat("Turtle_Shine", srgb(255, 255, 255), rough=0.2, emit=srgb(255, 255, 255), strength=1.0),
            make_mat("Turtle_Blush", srgb(240, 140, 140), rough=0.5)]
    B = Builder(bones)
    # shell dome: low-poly, alternating plate colours
    def dome(co):
        z = max(co.z, -0.05)
        return Vector((co.x, co.y * 1.08, z * 0.62 + 0.14))
    verts, faces = B.sphere((0, 0, 0), (0.42, 0.42, 0.42), 0, "Body", u=10, v=8, shape=dome)
    for f in faces:
        c = f.calc_center_median()
        if c.z > 0.22:
            ring = int((c.z - 0.22) / 0.07)
            ang = int((math.atan2(c.y, c.x) + math.pi) / (2 * math.pi) * 10)
            if (ring + ang) % 2 == 0:
                f.material_index = 1
    B.sphere((0, 0, 0.42), (0.13, 0.13, 0.05), 1, "Body", u=6, v=4)                       # top plate
    B.sphere((0, 0, 0.14), (0.46, 0.49, 0.06), 2, "Body", u=14, v=6)                      # rim
    B.sphere((0, 0, 0.12), (0.38, 0.42, 0.07), 2, "Body", u=12, v=6)                      # belly
    # head + face
    B.sphere((0, -0.47, 0.25), (0.15, 0.15, 0.14), 3, "Head", u=12, v=8, smooth=True)
    B.sphere((0, -0.35, 0.19), (0.085, 0.12, 0.08), 3, "Head", u=10, v=6, smooth=True)   # neck
    for sx in (-1, 1):
        B.sphere((sx * 0.07, -0.585, 0.29), (0.035, 0.02, 0.045), 4, "Head", u=10, v=6, smooth=True)
        B.sphere((sx * 0.062, -0.6, 0.305), (0.012, 0.008, 0.014), 5, "Head", u=6, v=4, smooth=True)
        B.sphere((sx * 0.11, -0.565, 0.22), (0.03, 0.012, 0.018), 6, "Head", u=6, v=4, smooth=True)
    # legs and tail
    for name, (h, t) in joints.items():
        if name.startswith("Leg"):
            h, t = Vector(h), Vector(t)
            B.cone(h, t + Vector((0, 0, 0.03)), 0.075, 0.065, 3, name, seg=8, smooth=True)
            B.sphere(t + Vector((0, -0.02, 0.03)), (0.075, 0.09, 0.04), 3, name, u=8, v=5, smooth=True)
    B.cone((0, 0.36, 0.14), (0, 0.55, 0.08), 0.05, 0.005, 3, "Tail", seg=6, smooth=True)
    obj = B.to_object("Turtle", mats)
    arm = build_armature("Turtle", joints, parents)
    bind(obj, arm)

    def walk(s):
        return {"Leg.FL": {"r": q(X, -28 * s)}, "Leg.BR": {"r": q(X, -28 * s)},
                "Leg.FR": {"r": q(X, 28 * s)}, "Leg.BL": {"r": q(X, 28 * s)},
                "Body": {"r": q(Y, 4 * s), "t": (0, 0, 0.02 * (1 - abs(s)))},
                "Head": {"r": q(Z, 6 * s)}, "Tail": {"r": q(Z, -15 * s)}}
    actions = {
        "Idle": (48, True, {0: {"Head": {"r": q(X, 0)}},
                            24: {"Head": {"r": q(X, 8), "t": (0, 0.01, -0.01)}, "Body": {"s": (1.02, 0.98, 1.02)},
                                 "Tail": {"r": q(Z, 12)}}}),
        "Walk": (24, True, {0: walk(1), 6: walk(0), 12: walk(-1), 18: walk(0)}),
        "Attack": (24, False, {0: {}, 8: {"Head": {"t": (0, 0.06, 0.02), "r": q(X, -15)}, "Body": {"r": q(X, -6)}},
                               12: {"Head": {"t": (0, -0.12, -0.03), "r": q(X, 20)}, "Body": {"r": q(X, 8)}},
                               16: {"Head": {"t": (0, -0.1, -0.03), "r": q(X, 15)}}, 24: {}}),
        "Hit": (12, False, {0: {}, 3: {"Head": {"t": (0, 0.12, -0.02)}, "Body": {"r": q(X, -10), "s": (1.05, 0.92, 1.05)},
                                       "Leg.FL": {"r": q(X, 20)}, "Leg.FR": {"r": q(X, 20)}}, 12: {}}),
        "Die": (36, False, {0: {}, 8: {"Root": {"t": (0, 0, 0.25)}, "Head": {"t": (0, 0.12, 0)}},
                            20: {"Root": {"r": q(Y, 180), "t": (0, 0, 0.5)}, "Head": {"t": (0, 0.12, 0)}},
                            28: {"Root": {"r": q(Y, 180), "t": (0, 0, 0.56)}, "Head": {"t": (0, 0.06, 0), "r": q(X, -30)},
                                 "Leg.FL": {"r": q(X, 30)}, "Leg.BR": {"r": q(X, -30)}},
                            36: {"Root": {"r": q(Y, 180), "t": (0, 0, 0.56)}, "Head": {"r": q(X, -40)},
                                 "Leg.FL": {"r": q(X, -20)}, "Leg.BR": {"r": q(X, 20)}}}),
    }
    export(arm, obj, os.path.join(out_dir, "Pet", "Pet_Rigged.fbx"), actions)  # the Turtle monster comes from build_turtleshell.py
    preview(os.path.join(preview_dir(out_dir), "Turtle_Preview.png"), (0, -0.05, 0.22), 1.35, 0.6)

# ---------------------------------------------------------------- main
def main():
    folder = MODELS_DIR
    if "--" in sys.argv and sys.argv.index("--") + 1 < len(sys.argv):
        folder = sys.argv[sys.argv.index("--") + 1]
    if not folder or not os.path.isdir(folder):
        raise SystemExit("Set MODELS_DIR (or pass it after --) to your Unity project's Assets/Resources/Models folder")
    bpy.context.scene.render.fps = FPS
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    jobs = {"Slime": build_slime, "Turtle": build_turtle}
    for name in (ONLY or list(jobs)):
        jobs[name](folder)
    print("Done. Switch back to Unity and press Play.")

main()
