"""
Our own player models for the three classes, modeled, rigged and animated in Blender.

Original designs in the classic MU archetypes (not copies of any game's art):
  DarkKnight  - dark steel plate with gold trim, horned helm, red cape, long sword
  DarkWizard  - long hooded robe, gold mantle, purple cape, staff with a glowing orb
  Elf         - light green leather, white skirt, long blond hair, circlet, bow and quiver

One shared humanoid rig, each part skinned rigidly to one bone. Clips (30 fps):
  Idle, Run, Attack01 (basic attack), Attack02 (skill), GetHit, Die

Blender 4.2+ (tested on 5.2). Run in Blender's Scripting tab, or:
    blender -b -P build_heroes.py -- "C:/path/to/YourUnityProject/Assets/Resources/Models"
Writes Models/<Class>/<Class>_Rigged.fbx and previews in Assets/Tools/Previews.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix, Quaternion

MODELS_DIR = r"D:\MMORPG\My project\Assets\Resources\Models"
FPS = 30

# ---------------------------------------------------------------- helpers (Blender space: +Z up, -Y front)
def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.cameras):
        for d in list(coll):
            coll.remove(d)

def srgb(r, g, b):
    f = lambda c: (c / 255.0) ** 2.2
    return (f(r), f(g), f(b))

def make_mat(name, rgb, rough=0.6, metal=0.0, emit=None, strength=1.0):
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

class Builder:
    def __init__(self, bones):
        self.bm = bmesh.new()
        self.dl = self.bm.verts.layers.deform.verify()
        self.bones = bones
        self.smooth = set()

    def _finish(self, verts, mat, bone, smooth):
        faces = {f for v in verts for f in v.link_faces}
        for f in faces:
            f.material_index = mat
            if smooth:
                self.smooth.add(f)
        for v in verts:
            v[self.dl][self.bones.index(bone)] = 1.0
        return list(verts), faces

    def sphere(self, center, radii, mat, bone, u=12, v=8, smooth=False, rot=None, shape=None):
        M = Matrix.Translation(center) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*radii, 1))
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=1.0, matrix=M)
        if shape:
            for vt in ret["verts"]:
                vt.co = shape(vt.co)
        return self._finish(ret["verts"], mat, bone, smooth)

    def cone(self, base, tip, r1, r2, mat, bone, seg=8, smooth=False):
        base, tip = Vector(base), Vector(tip)
        d = tip - base
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
        M = Matrix.Translation((base + tip) / 2) @ rot.to_matrix().to_4x4()
        ret = bmesh.ops.create_cone(self.bm, cap_ends=True, cap_tris=False, segments=seg,
                                    radius1=r1, radius2=r2, depth=d.length, matrix=M)
        return self._finish(ret["verts"], mat, bone, smooth)

    def ring(self, center, R, r, mat, bone, seg=28, side=8, sy=1.0, rz=1.0, smooth=True):
        """Torus around Z (R major, r minor), squashed by sy on Y and rz on the tube height."""
        cx, cy, cz = center
        grid = []
        for i in range(seg):
            a = 2 * math.pi * i / seg
            row = []
            for j in range(side):
                b = 2 * math.pi * j / side
                rr = R + r * math.cos(b)
                row.append(self.bm.verts.new((cx + rr * math.cos(a), cy + rr * math.sin(a) * sy, cz + r * rz * math.sin(b))))
            grid.append(row)
        for i in range(seg):
            for j in range(side):
                self.bm.faces.new((grid[i][j], grid[(i + 1) % seg][j], grid[(i + 1) % seg][(j + 1) % side], grid[i][(j + 1) % side]))
        return self._finish([v for row in grid for v in row], mat, bone, smooth)

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

def add_origin(data):
    """A still top bone. Unity applies non-uniform scale on the topmost bone along the wrong axes
    (a squashing slime puffed up into a big ball), so every animated bone must have a parent."""
    origin = data.edit_bones.new("Origin")
    origin.head, origin.tail, origin.roll = Vector((0, 0, 0)), Vector((0, 0, 0.05)), 0.0
    for eb in data.edit_bones:
        if eb is not origin and eb.parent is None:
            eb.parent = origin

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
    add_origin(data)
    bpy.ops.object.mode_set(mode="OBJECT")
    return arm

def q(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)   # +X rotation leans a bone's top toward the front (-Y)

def make_action(arm, name, length, keys, loop):
    """keys: {frame: {bone: dict(r=world Quaternion, t=world offset, s=(sx, s_along_bone, sz))}}"""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    frames = sorted(set(keys) | {0, length})
    for f in frames:
        k = keys.get(f, keys.get(0, {}) if (loop and f == length) else keys[max(x for x in keys if x <= f)])
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

def preview(path, target, dist, height, action=None, frame=0):
    scn = bpy.context.scene
    arm = bpy.data.objects["Armature"]
    if action:
        arm.animation_data.action = bpy.data.actions[action]
    scn.frame_set(frame)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
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
    scn.render.resolution_x = scn.render.resolution_y = 560
    scn.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)

# ---------------------------------------------------------------- the model

# ---------------------------------------------------------------- shared humanoid
def box(B, center, size, mat, bone, rot=None, smooth=False):
    M = Matrix.Translation(center) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*size, 1))
    ret = bmesh.ops.create_cube(B.bm, size=1.0, matrix=M)
    return B._finish(ret["verts"], mat, bone, smooth)

def limb(B, a, b, r1, r2, mat, bone, seg=10):
    """A tapered tube from a to b with round caps (shoulder, knee...)."""
    B.cone(a, b, r1, r2, mat, bone, seg=seg, smooth=True)
    B.sphere(a, (r1, r1, r1), mat, bone, u=10, v=6, smooth=True)

BONES = ["Hips", "Spine", "Chest", "Neck", "Head",
         "UpperArm.L", "ForeArm.L", "Hand.L", "UpperArm.R", "ForeArm.R", "Hand.R",
         "Thigh.L", "Shin.L", "Foot.L", "Thigh.R", "Shin.R", "Foot.R", "Cape", "Weapon"]

def skeleton(weapon_side):
    J = {"Hips": ((0, 0, 0.95), (0, 0, 1.08)),
         "Spine": ((0, 0, 1.08), (0, 0, 1.3)),
         "Chest": ((0, 0, 1.3), (0, 0, 1.5)),
         "Neck": ((0, 0, 1.5), (0, 0, 1.58)),
         "Head": ((0, 0, 1.58), (0, 0, 1.86)),
         "Cape": ((0, 0.16, 1.46), (0, 0.24, 0.9))}
    P = {"Hips": None, "Spine": "Hips", "Chest": "Spine", "Neck": "Chest", "Head": "Neck", "Cape": "Chest"}
    for side, s in (("L", 1), ("R", -1)):
        J["UpperArm." + side] = ((s * 0.22, 0, 1.45), (s * 0.27, 0, 1.18))
        J["ForeArm." + side] = ((s * 0.27, 0, 1.18), (s * 0.29, -0.03, 0.94))
        J["Hand." + side] = ((s * 0.29, -0.03, 0.94), (s * 0.3, -0.05, 0.84))
        J["Thigh." + side] = ((s * 0.1, 0, 0.95), (s * 0.11, 0, 0.52))
        J["Shin." + side] = ((s * 0.11, 0, 0.52), (s * 0.11, 0.02, 0.1))
        J["Foot." + side] = ((s * 0.11, 0.02, 0.1), (s * 0.11, -0.13, 0.03))
        P["UpperArm." + side] = "Chest"
        P["ForeArm." + side] = "UpperArm." + side
        P["Hand." + side] = "ForeArm." + side
        P["Thigh." + side] = "Hips"
        P["Shin." + side] = "Thigh." + side
        P["Foot." + side] = "Shin." + side
    hs = 1 if weapon_side == "L" else -1
    hand = Vector(J["Hand." + weapon_side][0])
    J["Weapon"] = (tuple(hand), tuple(hand + Vector((0, -0.12, 0))))
    P["Weapon"] = "Hand." + weapon_side
    return J, P

def body(B, J, skin, cloth, boots, hair=None, eyes=None):
    """The base figure: everything else is armour or clothes on top."""
    v = lambda b, i: Vector(J[b][i])
    B.sphere((0, 0, 1.0), (0.16, 0.11, 0.11), cloth, "Hips", u=14, v=8, smooth=True)
    B.sphere((0, 0, 1.2), (0.15, 0.1, 0.14), cloth, "Spine", u=14, v=8, smooth=True)
    B.cone((0, 0, 1.04), (0, 0, 1.34), 0.15, 0.185, cloth, "Spine", seg=16, smooth=True)
    B.sphere((0, 0, 1.38), (0.2, 0.13, 0.15), cloth, "Chest", u=16, v=10, smooth=True)
    B.cone((0, 0, 1.48), (0, 0, 1.62), 0.05, 0.045, skin, "Neck", seg=10, smooth=True)
    B.sphere((0, -0.01, 1.71), (0.12, 0.12, 0.135), skin, "Head", u=18, v=12, smooth=True)
    for s in (1, -1):
        B.sphere((s * 0.045, -0.115, 1.72), (0.022, 0.01, 0.016), eyes, "Head", u=8, v=6, smooth=True)
    for side in ("L", "R"):
        limb(B, v("UpperArm." + side, 0), v("UpperArm." + side, 1), 0.058, 0.048, cloth, "UpperArm." + side)
        limb(B, v("ForeArm." + side, 0), v("ForeArm." + side, 1), 0.048, 0.04, skin, "ForeArm." + side)
        h = v("Hand." + side, 0)
        B.sphere(h + Vector((0, -0.01, -0.04)), (0.045, 0.04, 0.055), skin, "Hand." + side, u=10, v=6, smooth=True)
        limb(B, v("Thigh." + side, 0), v("Thigh." + side, 1), 0.085, 0.065, cloth, "Thigh." + side)
        limb(B, v("Shin." + side, 0), v("Shin." + side, 1), 0.065, 0.05, boots, "Shin." + side)
        f = v("Foot." + side, 0)
        B.sphere(f + Vector((0, -0.06, -0.04)), (0.06, 0.12, 0.05), boots, "Foot." + side, u=12, v=6, smooth=True)

# ---------------------------------------------------------------- animation helpers
def arm(side, fwd=0.0, out=0.0, elbow=0.0, fold=0.0):
    """fwd: swing forward, out: raise sideways, elbow: bend up, fold: bend across the body
    (with the upper arm held forward, fold swings the forearm back toward the chest: drawing a bow)."""
    s = 1 if side == "L" else -1
    return {"UpperArm." + side: {"r": q(Y, -s * out) @ q(X, -fwd)},
            "ForeArm." + side: {"r": q(X, -elbow) @ q(Y, s * fold)}}

def leg(side, fwd=0.0, knee=0.0):
    return {"Thigh." + side: {"r": q(X, -fwd)}, "Shin." + side: {"r": q(X, knee)}}

def merge(*parts):
    out = {}
    for p in parts:
        for k, val in p.items():
            out[k] = val
    return out

def torso(lean=0.0, twist=0.0, bob=0.0, head=0.0, hips_yaw=0.0, hips_pitch=0.0, hips_t=(0, 0, 0)):
    t = (hips_t[0], hips_t[1], hips_t[2] + bob)
    return {"Hips": {"r": q(Z, hips_yaw) @ q(X, hips_pitch), "t": t},
            "Spine": {"r": q(X, lean * 0.5)}, "Chest": {"r": q(Z, twist) @ q(X, lean * 0.5)},
            "Head": {"r": q(X, head)}}

def cape(swing):
    return {"Cape": {"r": q(X, swing)}}

def run_cycle(hold):
    """hold(phase) -> extra arm pose for the weapon hand (or {}), phase 0/1."""
    k = {}
    for f, ph in ((0, 0), (5, 1), (10, 2), (15, 3)):
        a = 1 if ph in (0, 1) else -1
        if ph % 2 == 0:
            legs = merge(leg("L", 35 * a, 12 if a > 0 else 55), leg("R", -30 * a, 55 if a > 0 else 12))
            arms = merge(arm("L", -30 * a, 8, 25), arm("R", 30 * a, 8, 40))
            bob = -0.03
        else:
            legs = merge(leg("L", 5 * a, 40), leg("R", -5 * a, 40))
            arms = merge(arm("L", 0, 8, 30), arm("R", 0, 8, 30))
            bob = 0.03
        k[f] = merge(torso(lean=12, bob=bob, twist=6 * a), arms, legs, cape(-25), hold(ph))
    return k

def actions_for(cls):
    if cls == "DarkKnight":
        held = lambda ph: arm("R", 15, 10, 12)
        idle0 = merge(torso(), arm("L", 5, 10, 15), held(0), leg("L", 3), leg("R", -3), cape(-3))
        idle1 = merge(torso(lean=3, bob=-0.012), arm("L", 6, 12, 18), arm("R", 17, 10, 15), leg("L", 3), leg("R", -3), cape(-6))
        atk = {0: idle0,
               6: merge(torso(lean=-6, twist=25), arm("R", 165, 15, 25), arm("L", 20, 20, 30), leg("L", 15, 10), leg("R", -10, 10), cape(-10)),
               11: merge(torso(lean=18, twist=-28, bob=-0.06), arm("R", 40, 5, 0), arm("L", -10, 25, 20), leg("L", 30, 25), leg("R", -20, 15), cape(-20)),
               16: merge(torso(lean=14, twist=-20, bob=-0.05), arm("R", 30, 8, 5), arm("L", -5, 20, 20), leg("L", 28, 25), leg("R", -18, 15), cape(-12)),
               24: idle0}
        skill = {0: idle0}
        for i, f in enumerate((5, 10, 15, 20)):
            skill[f] = merge(torso(hips_yaw=90 * (i + 1), bob=0.03), arm("R", 80, 75, 5), arm("L", 0, 80, 10),
                             leg("L", 10, 15), leg("R", -10, 15), cape(-45))
        skill[26] = merge(torso(lean=10, bob=-0.05), arm("R", 50, 30, 10), arm("L", 0, 40, 10), leg("L", 20, 30), leg("R", -15, 20), cape(-15))
        skill[32] = idle0
        hold = lambda ph: arm("R", 20 * (1 if ph in (2, 3) else -0.3), 12, 15)
    elif cls == "DarkWizard":
        held = lambda ph: arm("R", 8, 15, 18)
        idle0 = merge(torso(), arm("L", 8, 12, 25), held(0), cape(-3))
        idle1 = merge(torso(lean=2, bob=-0.01), arm("L", 10, 14, 28), arm("R", 9, 15, 20), cape(-6))
        atk = {0: idle0,
               7: merge(torso(lean=-5, twist=15), arm("R", 120, 20, 40), arm("L", 40, 25, 30), cape(-8)),
               13: merge(torso(lean=14, twist=-12, bob=-0.03), arm("R", 85, 10, 0), arm("L", 70, 10, 10), leg("L", 20, 10), cape(-18)),
               18: merge(torso(lean=10, twist=-8), arm("R", 80, 10, 5), arm("L", 60, 12, 10), leg("L", 15, 10), cape(-12)),
               26: idle0}
        skill = {0: idle0,
                 10: merge(torso(lean=-12, head=-15, bob=0.04), arm("R", 170, 30, 10), arm("L", 170, 30, 10), cape(-15)),
                 16: merge(torso(lean=-12, head=-15, bob=0.06), arm("R", 175, 40, 5), arm("L", 175, 40, 5), cape(-20)),
                 22: merge(torso(lean=22, bob=-0.08), arm("R", 55, 35, 0), arm("L", 55, 35, 0), leg("L", 20, 30), leg("R", -10, 25), cape(-30)),
                 32: idle0}
        hold = lambda ph: arm("R", 8, 15, 22)
    else:  # Elf: bow in the left hand
        held = lambda ph: arm("L", 15, 12, 25)
        idle0 = merge(torso(), held(0), arm("R", 5, 10, 20), leg("L", 2), leg("R", -2))
        idle1 = merge(torso(lean=2, bob=-0.01), arm("L", 17, 14, 27), arm("R", 6, 12, 22), leg("L", 2), leg("R", -2))
        draw = lambda pull, tw: merge(torso(twist=tw), arm("L", 90, 0, 0), arm("R", 88, -5, 0, pull * 1.3))
        atk = {0: idle0, 5: merge(draw(20, 35), leg("L", 12, 5), leg("R", -12, 5)),
               12: merge(draw(110, 40), leg("L", 12, 5), leg("R", -12, 5)),
               15: merge(draw(25, 38), leg("L", 12, 5), leg("R", -12, 5)), 22: idle0}
        skill = {0: idle0, 4: draw(110, 40), 7: draw(25, 38), 10: draw(110, 30), 13: draw(25, 30),
                 16: draw(110, 45), 19: draw(25, 45), 26: idle0}
        hold = lambda ph: arm("L", 25, 12, 25)
    hit = {0: idle0,
           4: merge(torso(lean=-18, head=-15, bob=-0.02), arm("L", 15, 30, 40), arm("R", 15, 30, 40), cape(-15)),
           14: idle0}
    die = {0: idle0,
           10: merge(torso(lean=25, bob=-0.25, head=10), leg("L", 45, 90), leg("R", 35, 95), arm("L", 20, 30, 30), arm("R", 20, 30, 30), cape(-10)),
           24: merge(torso(hips_pitch=-82, hips_t=(0, 0.45, -0.8), head=-10), leg("L", 10, 20), leg("R", 5, 25),
                     arm("L", -20, 70, 10), arm("R", -20, 70, 10), cape(20)),
           30: merge(torso(hips_pitch=-86, hips_t=(0, 0.48, -0.84), head=-15), leg("L", 8, 15), leg("R", 4, 20),
                     arm("L", -25, 75, 5), arm("R", -25, 75, 5), cape(25)),
           45: merge(torso(hips_pitch=-86, hips_t=(0, 0.48, -0.84), head=-15), leg("L", 8, 15), leg("R", 4, 20),
                     arm("L", -25, 75, 5), arm("R", -25, 75, 5), cape(25))}
    return {"Idle": (40, True, {0: idle0, 20: idle1}),
            "Run": (20, True, run_cycle(hold)),
            "Attack01": (max(atk), False, atk),
            "Attack02": (max(skill), False, skill),
            "GetHit": (14, False, hit),
            "Die": (45, False, die)}

# ---------------------------------------------------------------- the three classes
def dark_knight(B, J, M):
    body(B, J, M["skin"], M["under"], M["steel"], eyes=M["eye"])
    steel, gold, dark, red, blade = M["steel"], M["gold"], M["dark"], M["red"], M["blade"]
    # breastplate, belt and faulds
    B.sphere((0, -0.01, 1.38), (0.225, 0.155, 0.17), steel, "Chest", u=18, v=10, smooth=True)
    B.sphere((0, -0.13, 1.4), (0.07, 0.035, 0.09), gold, "Chest", u=10, v=6, smooth=True)      # chest crest
    B.ring((0, 0, 1.07), 0.165, 0.025, gold, "Hips", sy=0.72)
    B.cone((0, 0, 1.05), (0, 0, 0.72), 0.19, 0.25, steel, "Hips", seg=12)
    B.ring((0, 0, 0.73), 0.245, 0.018, gold, "Hips", sy=0.8)
    # spiked pauldrons
    for side, s in (("L", 1), ("R", -1)):
        c = Vector((s * 0.25, 0, 1.47))
        B.sphere(c, (0.12, 0.12, 0.09), steel, "UpperArm." + side, u=12, v=8, smooth=True)
        B.ring(c + Vector((0, 0, -0.04)), 0.115, 0.018, gold, "UpperArm." + side)
        for a in (-35, 0, 35):
            d = Vector((s * math.cos(math.radians(a)), math.sin(math.radians(a)) * 0.6, 0.55)).normalized()
            B.cone(c + d * 0.08, c + d * 0.2, 0.025, 0.0, gold, "UpperArm." + side, seg=6)
        # gauntlets and greaves
        f = Vector(J["ForeArm." + side][0]).lerp(Vector(J["ForeArm." + side][1]), 0.55)
        B.cone(f + Vector((0, 0, 0.09)), f + Vector((0, 0, -0.12)), 0.058, 0.062, steel, "ForeArm." + side, seg=10, smooth=True)
        k = Vector(J["Shin." + side][0])
        B.sphere(k + Vector((0, -0.05, 0)), (0.06, 0.045, 0.06), gold, "Shin." + side, u=10, v=6, smooth=True)   # knee cop
        B.cone(k + Vector((0, 0, -0.05)), k + Vector((0, 0.01, -0.38)), 0.075, 0.068, steel, "Shin." + side, seg=10, smooth=True)
    # horned helm with a dark visor
    B.sphere((0, 0, 1.73), (0.14, 0.145, 0.15), steel, "Head", u=18, v=12, smooth=True)
    box(B, (0, -0.128, 1.71), (0.15, 0.03, 0.035), dark, "Head")
    B.cone((0, -0.02, 1.86), (0, 0.03, 1.98), 0.03, 0.0, gold, "Head", seg=6)                    # crest spike
    for s in (1, -1):
        base = Vector((s * 0.12, -0.01, 1.8))
        mid = base + Vector((s * 0.1, 0.0, 0.07))
        B.cone(base, mid, 0.035, 0.024, gold, "Head", seg=8, smooth=True)
        B.cone(mid, mid + Vector((s * 0.02, 0.03, 0.14)), 0.024, 0.0, gold, "Head", seg=8, smooth=True)
    # cape
    box(B, (0, 0.2, 1.17), (0.36, 0.025, 0.62), red, "Cape", rot=q(X, -8))
    B.ring((0, 0.04, 1.47), 0.17, 0.02, gold, "Chest", sy=0.8)
    # long sword pointing forward-down from the right hand
    g = Vector(J["Hand.R"][0]) + Vector((0, -0.02, -0.06))
    d = Vector((0, -0.55, -0.83)).normalized()
    B.cone(g + Vector((0, 0.04, 0.08)), g + d * 0.04, 0.022, 0.022, dark, "Weapon", seg=8)  # grip
    guard = g + d * 0.04
    box(B, guard, (0.2, 0.04, 0.04), gold, "Weapon", rot=Vector((0, 0, 1)).rotation_difference(d))
    B.cone(guard, guard + d * 0.95, 0.04, 0.008, blade, "Weapon", seg=4)
    B.sphere(g + Vector((0, 0.05, 0.1)), (0.03, 0.03, 0.03), gold, "Weapon", u=8, v=6, smooth=True)   # pommel

def dark_wizard(B, J, M):
    body(B, J, M["skin"], M["robe"], M["robe2"], eyes=M["eye"])
    robe, robe2, gold, purple, wood, orb = M["robe"], M["robe2"], M["gold"], M["purple"], M["wood"], M["orb"]
    # long robe: a flared skirt to the ankles, sash and collar
    B.cone((0, 0, 1.1), (0, 0, 0.12), 0.17, 0.33, robe, "Hips", seg=16, smooth=True)
    B.ring((0, 0, 0.13), 0.32, 0.02, gold, "Hips")
    B.ring((0, 0, 1.06), 0.165, 0.03, gold, "Hips", sy=0.75)
    B.sphere((0, -0.01, 1.37), (0.215, 0.145, 0.16), robe, "Chest", u=16, v=10, smooth=True)
    box(B, (0, -0.135, 1.25), (0.06, 0.02, 0.36), gold, "Spine")                                   # front trim
    # gold mantle over the shoulders
    B.sphere((0, 0, 1.47), (0.3, 0.2, 0.08), gold, "Chest", u=18, v=8, smooth=True)
    B.sphere((0, 0, 1.49), (0.27, 0.18, 0.07), robe2, "Chest", u=18, v=8, smooth=True)
    for side, s in (("L", 1), ("R", -1)):   # wide sleeves
        f = Vector(J["ForeArm." + side][1])
        B.cone(Vector(J["ForeArm." + side][0]), f + Vector((0, 0, 0.02)), 0.06, 0.1, robe, "ForeArm." + side, seg=12, smooth=True)
        B.ring(f + Vector((0, 0, 0.03)), 0.095, 0.015, gold, "ForeArm." + side)
    # deep hood with a tall pointed tip falling back
    def hood(co):  # flatten the front so the face opening shows
        return Vector((co.x, -0.07, co.z)) if co.y < -0.07 and 1.6 < co.z < 1.8 else co
    B.sphere((0, 0.01, 1.73), (0.155, 0.16, 0.16), robe2, "Head", u=18, v=12, smooth=True, shape=hood)
    B.sphere((0, -0.075, 1.7), (0.1, 0.03, 0.1), M["shadow"], "Head", u=12, v=8, smooth=True)    # face in shadow
    for s in (1, -1):
        B.sphere((s * 0.04, -0.125, 1.71), (0.02, 0.01, 0.012), M["eye"], "Head", u=8, v=6, smooth=True)
    B.cone((0, 0.02, 1.85), (0, 0.16, 2.08), 0.11, 0.0, robe2, "Head", seg=12, smooth=True)
    B.ring((0, 0, 1.6), 0.13, 0.02, gold, "Head", sy=0.9)
    # cape
    box(B, (0, 0.19, 1.12), (0.4, 0.025, 0.72), purple, "Cape", rot=q(X, -6))
    # staff: tall shaft held upright with a glowing orb in a gold claw
    g = Vector(J["Hand.R"][0]) + Vector((0, -0.04, -0.06))
    top, bottom = g + Vector((0, -0.04, 0.95)), g + Vector((0, 0.03, -0.7))
    B.cone(bottom, top, 0.022, 0.026, wood, "Weapon", seg=8)
    B.sphere(top + Vector((0, 0, 0.09)), (0.075, 0.075, 0.075), orb, "Weapon", u=14, v=10, smooth=True)
    for a in range(4):
        d = Vector((math.cos(a * math.pi / 2), math.sin(a * math.pi / 2), 0))
        B.cone(top, top + d * 0.08 + Vector((0, 0, 0.17)), 0.016, 0.0, gold, "Weapon", seg=6)

def elf(B, J, M):
    body(B, J, M["skin"], M["leather"], M["boots"], eyes=M["eye"])
    leather, white, gold, hair, wood, string = M["leather"], M["white"], M["gold"], M["hair"], M["wood"], M["string"]
    # light armour: green bodice, white pleated skirt, gold belt
    B.sphere((0, -0.01, 1.37), (0.205, 0.14, 0.15), leather, "Chest", u=16, v=10, smooth=True)
    B.sphere((0, -0.1, 1.4), (0.13, 0.06, 0.09), white, "Chest", u=12, v=8, smooth=True)
    B.cone((0, 0, 1.06), (0, 0, 0.7), 0.17, 0.27, white, "Hips", seg=14, smooth=True)
    B.ring((0, 0, 1.05), 0.17, 0.02, gold, "Hips", sy=0.75)
    for side, s in (("L", 1), ("R", -1)):
        c = Vector((s * 0.23, 0, 1.46))
        B.sphere(c, (0.085, 0.085, 0.06), white, "UpperArm." + side, u=12, v=8, smooth=True)       # shoulder guards
        B.ring(c + Vector((0, 0, -0.03)), 0.08, 0.012, gold, "UpperArm." + side)
        f = Vector(J["ForeArm." + side][0]).lerp(Vector(J["ForeArm." + side][1]), 0.6)
        B.cone(f + Vector((0, 0, 0.07)), f + Vector((0, 0, -0.1)), 0.05, 0.052, M["boots"], "ForeArm." + side, seg=10, smooth=True)
        k = Vector(J["Shin." + side][0])
        B.cone(k + Vector((0, 0, -0.02)), k + Vector((0, 0.01, -0.4)), 0.07, 0.062, M["boots"], "Shin." + side, seg=10, smooth=True)
    # long hair, pointed ears, circlet with a gem
    B.sphere((0, 0.035, 1.75), (0.135, 0.13, 0.14), hair, "Head", u=18, v=12, smooth=True)
    B.sphere((0, 0.11, 1.5), (0.11, 0.06, 0.24), hair, "Head", u=14, v=10, smooth=True)          # hair down the back
    B.sphere((0, -0.09, 1.79), (0.11, 0.045, 0.04), hair, "Head", u=12, v=6, smooth=True)        # fringe
    for s in (1, -1):
        B.cone((s * 0.11, 0.0, 1.71), (s * 0.21, 0.04, 1.8), 0.025, 0.0, M["skin"], "Head", seg=6, smooth=True)
    B.ring((0, 0, 1.79), 0.135, 0.01, gold, "Head", sy=0.95)
    B.sphere((0, -0.135, 1.8), (0.018, 0.01, 0.022), M["gem"], "Head", u=8, v=6, smooth=True)
    # quiver on the back
    B.cone((0.06, 0.16, 1.05), (-0.08, 0.18, 1.5), 0.05, 0.055, M["boots"], "Chest", seg=10)
    for i in range(3):
        B.cone((-0.07 + i * 0.02, 0.18, 1.48), (-0.1 + i * 0.02, 0.19, 1.62), 0.006, 0.006, wood, "Chest", seg=4)
        B.cone((-0.1 + i * 0.02, 0.19, 1.62), (-0.1 + i * 0.02, 0.19, 1.66), 0.018, 0.0, white, "Chest", seg=4)
    # bow in the left hand: an arc of short segments and a string
    g = Vector(J["Hand.L"][0]) + Vector((0, -0.03, -0.06))
    pts = []
    for i in range(9):
        t = (i / 8.0) * 2 - 1
        pts.append(g + Vector((0, -0.12 * (1 - t * t), 0.55 * t)))
    for a, b in zip(pts, pts[1:]):
        B.cone(a, b, 0.018, 0.018, wood, "Weapon", seg=6)
    B.sphere(g, (0.026, 0.026, 0.05), gold, "Weapon", u=8, v=6, smooth=True)
    B.cone(pts[0], pts[-1], 0.004, 0.004, string, "Weapon", seg=4)

CLASSES = {
    "DarkKnight": (dark_knight, "R", {
        "skin": ((232, 190, 160), 0.6, 0), "under": ((48, 44, 52), 0.7, 0), "eye": ((30, 20, 20), 0.2, 0),
        "steel": ((70, 74, 88), 0.32, 0.85), "gold": ((212, 168, 64), 0.3, 0.9), "dark": ((22, 20, 26), 0.4, 0.3),
        "red": ((150, 24, 30), 0.7, 0), "blade": ((200, 210, 225), 0.18, 0.95)}),
    "DarkWizard": (dark_wizard, "R", {
        "skin": ((232, 190, 160), 0.6, 0), "robe": ((40, 46, 110), 0.65, 0), "robe2": ((26, 28, 70), 0.7, 0),
        "eye": ((120, 200, 255), 0.2, 0, (120, 200, 255)), "gold": ((212, 168, 64), 0.3, 0.9), "purple": ((88, 36, 120), 0.7, 0),
        "wood": ((92, 60, 36), 0.7, 0), "orb": ((90, 180, 255), 0.1, 0, (90, 180, 255)), "shadow": ((10, 10, 18), 0.9, 0)}),
    "Elf": (elf, "L", {
        "skin": ((240, 204, 178), 0.6, 0), "leather": ((60, 132, 72), 0.6, 0), "boots": ((110, 78, 48), 0.7, 0),
        "eye": ((40, 120, 70), 0.2, 0), "white": ((236, 236, 228), 0.6, 0), "gold": ((222, 180, 70), 0.3, 0.9),
        "hair": ((246, 214, 120), 0.5, 0), "wood": ((120, 80, 44), 0.6, 0), "string": ((240, 240, 230), 0.5, 0),
        "gem": ((60, 220, 120), 0.1, 0, (60, 220, 120))}),
}

def build_class(name, out_dir):
    clear_scene()
    scn = bpy.context.scene
    scn.render.fps = FPS
    make, weapon_side, palette = CLASSES[name]
    mats, M = [], {}
    for key, spec in palette.items():
        rgb, rough, metal = spec[0], spec[1], spec[2]
        emit = srgb(*spec[3]) if len(spec) > 3 else None
        M[key] = len(mats)
        mats.append(make_mat(name[:2] + "_" + key, srgb(*rgb), rough=rough, metal=metal, emit=emit, strength=2.0))
    J, P = skeleton(weapon_side)
    B = Builder(BONES)
    make(B, J, M)
    obj = B.to_object(name, mats)
    arm_obj = build_armature(name, J, P)
    obj.parent = arm_obj
    obj.modifiers.new("Armature", "ARMATURE").object = arm_obj
    for clip, (length, loop, keys) in actions_for(name).items():
        make_action(arm_obj, clip, length, keys, loop)
    arm_obj.animation_data.action = bpy.data.actions["Idle"]
    scn.frame_set(0)
    path = os.path.join(out_dir, name, name + "_Rigged.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_ARGS)
    print("[ok]", path)
    prev = os.path.normpath(os.path.join(out_dir, "..", "..", "Tools", "Previews"))
    os.makedirs(prev, exist_ok=True)
    preview(os.path.join(prev, name + "_Preview.png"), (0, 0, 1.0), 2.6, 0.9, "Idle", 0)
    preview(os.path.join(prev, name + "_Attack.png"), (0, 0, 1.0), 2.6, 0.9, "Attack01", 12)
    preview(os.path.join(prev, name + "_Run.png"), (0, 0, 1.0), 2.6, 0.9, "Run", 0)

def main():
    folder = MODELS_DIR
    if "--" in sys.argv and sys.argv.index("--") + 1 < len(sys.argv):
        folder = sys.argv[sys.argv.index("--") + 1]
    if not folder or not os.path.isdir(folder):
        raise SystemExit("Set MODELS_DIR (or pass it after --) to your Unity project's Assets/Resources/Models folder")
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for name in CLASSES:
        build_class(name, folder)
    print("Done. Switch back to Unity and press Play.")

main()
