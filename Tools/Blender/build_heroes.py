"""
Our own player models for the three classes, modeled, rigged and animated in Blender.

Original designs in the classic MU archetypes (not copies of any game's art):
  DarkKnight  - dark steel plate with gold trim, horned helm, red cape, long sword
  DarkWizard  - long hooded robe, gold mantle, purple cape, staff with a glowing orb
  Elf         - light green leather, white skirt, long blond hair, circlet, bow and quiver

One shared humanoid rig, each part skinned rigidly to one bone. Clips (30 fps):
  Idle, Run, Attack01 (basic attack), Attack02 (skill), GetHit, Die

Each FBX carries every piece of gear (see the "gear" section): Unity shows what is equipped.

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
        self.pl = self.bm.faces.layers.int.new("part")
        self.parts = []
        self.part = "Body"   # which exported object the next shapes go into

    def _finish(self, verts, mat, bone, smooth):
        faces = {f for v in verts for f in v.link_faces}
        if self.part not in self.parts:
            self.parts.append(self.part)
        pi = self.parts.index(self.part)
        for f in faces:
            f.material_index = mat
            f[self.pl] = pi
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

    def to_objects(self, mats, prefix="G_"):
        """One mesh object per part (G_Body, G_Helm1, G_Weapon3...), each with only the materials it uses,
        so Unity gets a SkinnedMeshRenderer per piece it can show, hide and recolour."""
        for f in self.bm.faces:
            f.smooth = f in self.smooth
        bmesh.ops.recalc_face_normals(self.bm, faces=list(self.bm.faces))
        objs = []
        for pi, part in enumerate(self.parts):
            bm = self.bm.copy()
            pl = bm.faces.layers.int["part"]
            bmesh.ops.delete(bm, geom=[f for f in bm.faces if f[pl] != pi], context="FACES")
            used = sorted({f.material_index for f in bm.faces})
            for f in bm.faces:
                f.material_index = used.index(f.material_index)
            me = bpy.data.meshes.new(prefix + part)
            bm.to_mesh(me)
            bm.free()
            for i in used:
                me.materials.append(mats[i])
            obj = bpy.data.objects.new(prefix + part, me)
            bpy.context.scene.collection.objects.link(obj)
            for b in self.bones:
                obj.vertex_groups.new(name=b)
            objs.append(obj)
        self.bm.free()
        return objs

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
# ---------------------------------------------------------------- gear
# Every hero FBX carries all of its gear as separate objects, skinned to the same rig:
#   G_Body                    the base figure in plain clothes (always shown)
#   G_Hair                    shown when no helm hides it
#   G_<Slot><Stage>           Armor/Pants/Gloves/Boots/Helm, stage 0..2 (tiers 1-2, 3-4, 5-6)
#   G_Weapon<Tier>            one weapon per tier 1..6
# Unity (GearVisual.cs) shows the pieces that match what is equipped and recolours the armour
# materials (<prefix>_arm, _arm2, _trim, _glow) with the tier's palette (TIER_COLORS below).
GEAR_SLOTS = ("Armor", "Pants", "Gloves", "Boots", "Helm")
STAGES, TIERS = 3, 6

def stage_of(tier):
    return (tier - 1) // 2

# per class, per tier: (arm, arm2, trim, glow). Mirrored in GearVisual.cs: keep them in sync.
TIER_COLORS = {
    "DarkKnight": [((150, 98, 56), (70, 46, 30), (196, 150, 96), (255, 170, 90)),
                   ((186, 192, 204), (60, 64, 74), (110, 150, 200), (150, 200, 255)),
                   ((70, 74, 88), (22, 20, 26), (212, 168, 64), (255, 200, 90)),
                   ((34, 32, 40), (12, 10, 14), (170, 32, 40), (255, 60, 50)),
                   ((214, 186, 110), (90, 60, 30), (245, 245, 250), (255, 240, 170)),
                   ((110, 22, 30), (30, 8, 10), (255, 150, 40), (255, 120, 30))],
    "DarkWizard": [((96, 70, 46), (60, 44, 30), (190, 150, 96), (255, 180, 90)),
                   ((170, 176, 190), (70, 80, 100), (120, 160, 210), (150, 200, 255)),
                   ((40, 46, 110), (26, 28, 70), (212, 168, 64), (90, 180, 255)),
                   ((30, 26, 40), (70, 20, 80), (170, 40, 200), (200, 90, 255)),
                   ((230, 226, 210), (180, 140, 60), (240, 200, 90), (255, 240, 170)),
                   ((120, 20, 34), (40, 10, 16), (255, 150, 40), (255, 120, 30))],
    "Elf":        [((120, 92, 60), (210, 196, 170), (180, 140, 90), (180, 255, 150)),
                   ((170, 180, 190), (236, 236, 240), (130, 170, 210), (150, 220, 255)),
                   ((60, 132, 72), (236, 236, 228), (222, 180, 70), (60, 220, 120)),
                   ((40, 60, 50), (60, 70, 64), (200, 200, 210), (120, 255, 200)),
                   ((230, 210, 140), (250, 248, 240), (240, 200, 90), (255, 240, 170)),
                   ((140, 30, 50), (250, 220, 200), (255, 160, 60), (255, 120, 30))],
}

def along(J, bone, t):
    return Vector(J[bone][0]).lerp(Vector(J[bone][1]), t)

def aim(d):
    return Vector((0, 0, 1)).rotation_difference(Vector(d).normalized())

SIDES = (("L", 1), ("R", -1))

# ---------------------------------------------------------------- Dark Knight
def dk_armor(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    B.sphere((0, -0.01, 1.38), (0.225 + 0.01 * s, 0.155 + 0.01 * s, 0.17), arm, "Chest", u=18, v=10, smooth=True)
    B.sphere((0, -0.015, 1.2), (0.17, 0.125, 0.12), arm2 if s == 0 else arm, "Spine", u=14, v=8, smooth=True)
    B.sphere((0, -0.14 - 0.01 * s, 1.4), (0.07 + 0.01 * s, 0.035, 0.09), trim, "Chest", u=10, v=6, smooth=True)
    B.ring((0, 0.0, 1.5), 0.15, 0.02, trim, "Chest", sy=0.8)
    B.ring((0, 0.04, 1.47), 0.17, 0.02, trim, "Chest", sy=0.8)       # cape clasp
    if s == 2:
        B.sphere((0, -0.185, 1.4), (0.03, 0.02, 0.035), glow, "Chest", u=10, v=6, smooth=True)
        for z in (1.3, 1.22, 1.14):                                      # ribbed belly plates
            B.ring((0, -0.01, z), 0.15, 0.012, trim, "Spine", sy=0.78)
    for side, sg in SIDES:
        c = Vector((sg * 0.25, 0, 1.47))
        r = 0.1 + 0.025 * s
        B.sphere(c, (r, r, r * 0.75), arm, "UpperArm." + side, u=12, v=8, smooth=True)
        B.ring(c + Vector((0, 0, -0.04)), r * 0.95, 0.018, trim, "UpperArm." + side)
        if s >= 1:
            for a in (-35, 0, 35):
                d = Vector((sg * math.cos(math.radians(a)), math.sin(math.radians(a)) * 0.6, 0.55)).normalized()
                B.cone(c + d * (r * 0.7), c + d * (r + 0.1 + 0.06 * (s - 1)), 0.025, 0.0, trim, "UpperArm." + side, seg=6)
        if s == 2:
            B.sphere(c + Vector((sg * 0.03, 0, -0.08)), (r * 0.9, r * 0.9, r * 0.45), arm2, "UpperArm." + side, u=12, v=6, smooth=True)

def dk_pants(B, J, M, s):
    arm, arm2, trim = M["arm"], M["arm2"], M["trim"]
    B.ring((0, 0, 1.07), 0.165, 0.025, trim, "Hips", sy=0.72)
    for side, sg in SIDES:
        a, b = along(J, "Thigh." + side, 0.1), along(J, "Thigh." + side, 0.85)
        B.cone(a, b, 0.095, 0.078, arm2, "Thigh." + side, seg=10, smooth=True)
    if s == 0:   # short tassets front and back
        for y in (-0.14, 0.14):
            box(B, (0, y, 0.95), (0.22, 0.03, 0.2), arm, "Hips", rot=q(X, 10 if y < 0 else -10))
    else:
        bottom = 0.8 if s == 1 else 0.72
        B.cone((0, 0, 1.05), (0, 0, bottom), 0.19, 0.25, arm, "Hips", seg=12)
        B.ring((0, 0, bottom + 0.01), 0.245, 0.018, trim, "Hips", sy=0.8)
    if s == 2:
        for sg in (1, -1):
            box(B, (sg * 0.24, 0, 0.88), (0.04, 0.2, 0.26), trim, "Hips", rot=q(Y, sg * 12))

def dk_gloves(B, J, M, s):
    arm, arm2, trim = M["arm"], M["arm2"], M["trim"]
    for side, sg in SIDES:
        f = along(J, "ForeArm." + side, 0.55)
        B.cone(f + Vector((0, 0, 0.09 + 0.02 * s)), f + Vector((0, 0, -0.12)), 0.058 + 0.006 * s, 0.062 + 0.006 * s, arm, "ForeArm." + side, seg=10, smooth=True)
        h = Vector(J["Hand." + side][0])
        B.sphere(h + Vector((0, -0.01, -0.04)), (0.052, 0.047, 0.062), arm2, "Hand." + side, u=10, v=6, smooth=True)
        if s >= 1:
            B.sphere(Vector(J["ForeArm." + side][0]), (0.055, 0.055, 0.05), trim, "ForeArm." + side, u=10, v=6, smooth=True)
        if s == 2:
            B.cone(f + Vector((sg * 0.06, 0.0, 0.0)), f + Vector((sg * 0.13, 0.06, 0.12)), 0.028, 0.0, trim, "ForeArm." + side, seg=6)

def dk_boots(B, J, M, s):
    arm, arm2, trim = M["arm"], M["arm2"], M["trim"]
    for side, sg in SIDES:
        k = Vector(J["Shin." + side][0])
        B.cone(k + Vector((0, 0, -0.05)), k + Vector((0, 0.01, -0.38)), 0.075, 0.068, arm, "Shin." + side, seg=10, smooth=True)
        B.sphere(k + Vector((0, -0.05, 0)), (0.06, 0.045, 0.06), trim, "Shin." + side, u=10, v=6, smooth=True)
        f = Vector(J["Foot." + side][0])
        B.sphere(f + Vector((0, -0.06, -0.035)), (0.066, 0.13, 0.056), arm2, "Foot." + side, u=12, v=6, smooth=True)
        if s >= 1:
            B.cone(k + Vector((0, -0.08, 0.0)), k + Vector((0, -0.16, 0.06)), 0.025, 0.0, trim, "Shin." + side, seg=6)
        if s == 2:
            B.cone(k + Vector((sg * 0.07, 0.02, -0.1)), k + Vector((sg * 0.15, 0.08, 0.03)), 0.03, 0.0, trim, "Shin." + side, seg=6)

def dk_helm(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    if s == 0:   # open skull cap with a nose guard
        B.sphere((0, 0.005, 1.765), (0.137, 0.142, 0.11), arm, "Head", u=18, v=10, smooth=True)
        B.ring((0, 0.0, 1.745), 0.133, 0.014, trim, "Head")
        box(B, (0, -0.136, 1.71), (0.026, 0.016, 0.085), arm, "Head")
        return
    B.sphere((0, 0, 1.73), (0.14, 0.145, 0.15), arm, "Head", u=18, v=12, smooth=True)
    box(B, (0, -0.128, 1.71), (0.15, 0.03, 0.035), arm2, "Head")
    B.cone((0, -0.02, 1.86), (0, 0.03, 1.98 + 0.03 * s), 0.03, 0.0, trim, "Head", seg=6)
    for sg in (1, -1):   # cheek guards
        B.sphere((sg * 0.1, -0.08, 1.66), (0.04, 0.05, 0.07), arm, "Head", u=10, v=6, smooth=True)
    if s == 2:
        B.sphere((0, -0.14, 1.78), (0.025, 0.015, 0.03), glow, "Head", u=8, v=6, smooth=True)
        for sg in (1, -1):
            base = Vector((sg * 0.12, -0.01, 1.8))
            mid = base + Vector((sg * 0.12, 0.0, 0.08))
            B.cone(base, mid, 0.04, 0.028, trim, "Head", seg=8, smooth=True)
            B.cone(mid, mid + Vector((sg * 0.02, 0.04, 0.18)), 0.028, 0.0, trim, "Head", seg=8, smooth=True)
    else:
        for sg in (1, -1):   # small wings on the sides
            B.cone((sg * 0.13, 0.0, 1.76), (sg * 0.2, 0.08, 1.86), 0.03, 0.0, trim, "Head", seg=6)

def dk_hair(B, J, M):
    hair = M["hair"]
    B.sphere((0, 0.02, 1.775), (0.13, 0.128, 0.1), hair, "Head", u=16, v=10, smooth=True)
    for a in range(-2, 3):
        base = Vector((a * 0.045, -0.06, 1.8))
        B.cone(base, base + Vector((a * 0.02, -0.06, -0.02)), 0.03, 0.0, hair, "Head", seg=6)
    for a in range(-1, 2):
        base = Vector((a * 0.06, 0.06, 1.84))
        B.cone(base, base + Vector((a * 0.03, 0.08, 0.04)), 0.035, 0.0, hair, "Head", seg=6)

def dk_sword(B, J, M, t):
    g = Vector(J["Hand.R"][0]) + Vector((0, -0.02, -0.06))
    d = Vector((0, -0.55, -0.83)).normalized()
    rot = aim(d)
    X3 = Vector((1, 0, 0))
    L, w, gw = [(0.6, 0.78, 0.95, 1.0, 1.1, 1.15)[t - 1], (0.055, 0.07, 0.08, 0.1, 0.14, 0.11)[t - 1], (0.14, 0.2, 0.22, 0.26, 0.3, 0.3)[t - 1]]
    blade = (M["wblade"], M["wblade"], M["wblade"], M["wdark"], M["wblade"], M["wdark"])[t - 1]
    guard_m = (M["wdark"], M["wblade"], M["wgold"], M["wred"], M["wgold"], M["wgold"])[t - 1]
    guard = g + d * 0.04
    B.cone(g + Vector((0, 0.04, 0.08)), guard, 0.022, 0.022, M["wdark"], "Weapon", seg=8)          # grip
    B.sphere(g + Vector((0, 0.05, 0.1)), (0.03, 0.03, 0.03), guard_m, "Weapon", u=8, v=6, smooth=True)   # pommel
    box(B, guard, (gw, 0.04, 0.045), guard_m, "Weapon", rot=rot)
    box(B, guard + d * (L / 2), (w, 0.018, L), blade, "Weapon", rot=rot)
    B.cone(guard + d * L, guard + d * (L + w * 1.3), w * 0.72, 0.0, blade, "Weapon", seg=4)
    if t == 4:    # glowing red edges and a spiked guard
        for sg in (1, -1):
            box(B, guard + d * (L / 2) + X3 * sg * (w / 2), (0.014, 0.022, L * 0.96), M["wred"], "Weapon", rot=rot)
            B.cone(guard + X3 * sg * gw / 2, guard + X3 * sg * (gw / 2 + 0.07) - d * 0.06, 0.022, 0.0, M["wred"], "Weapon", seg=6)
    if t == 5:    # gold fuller and a gem in the guard
        box(B, guard + d * (L * 0.45), (w * 0.3, 0.024, L * 0.8), M["wgold"], "Weapon", rot=rot)
        B.sphere(guard, (0.035, 0.035, 0.035), M["wlight"], "Weapon", u=10, v=8, smooth=True)
    if t == 6:    # a burning core with flame tongues along the edges, winged guard
        box(B, guard + d * (L / 2), (w * 0.45, 0.024, L * 0.95), M["wember"], "Weapon", rot=rot)
        for i in range(6):
            p = guard + d * (0.15 + i * 0.16)
            for sg in (1, -1):
                B.cone(p + X3 * sg * (w / 2), p + X3 * sg * (w / 2 + 0.06) - d * 0.08, 0.022, 0.0, M["wember"], "Weapon", seg=5)
        for sg in (1, -1):
            B.cone(guard + X3 * sg * gw / 2, guard + X3 * sg * (gw / 2 + 0.1) - d * 0.12, 0.03, 0.0, M["wgold"], "Weapon", seg=6)

def dark_knight(B, J, M):
    B.part = "Body"
    body(B, J, M["skin"], M["under"], M["shoe"], eyes=M["eye"])
    box(B, (0, 0.2, 1.17), (0.36, 0.025, 0.62), M["cape"], "Cape", rot=q(X, -8))
    B.part = "Hair"
    dk_hair(B, J, M)

# ---------------------------------------------------------------- Dark Wizard
def dw_armor(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    B.sphere((0, -0.01, 1.37), (0.215, 0.145, 0.16), arm, "Chest", u=16, v=10, smooth=True)
    B.sphere((0, -0.01, 1.2), (0.165, 0.12, 0.13), arm, "Spine", u=14, v=8, smooth=True)
    box(B, (0, -0.14, 1.25), (0.06, 0.02, 0.36), trim, "Spine")
    B.sphere((0, 0, 1.47), (0.3 + 0.02 * s, 0.2, 0.08), trim, "Chest", u=18, v=8, smooth=True)
    B.sphere((0, 0, 1.49), (0.27 + 0.02 * s, 0.18, 0.07), arm2, "Chest", u=18, v=8, smooth=True)
    for side, sg in SIDES:   # wide sleeves to mid forearm
        a = Vector(J["UpperArm." + side][0])
        B.cone(a, along(J, "UpperArm." + side, 1.0), 0.07, 0.075, arm, "UpperArm." + side, seg=12, smooth=True)
        f = along(J, "ForeArm." + side, 0.5)
        B.cone(Vector(J["ForeArm." + side][0]), f, 0.07, 0.1, arm, "ForeArm." + side, seg=12, smooth=True)
        B.ring(f + Vector((0, 0, 0.01)), 0.095, 0.015, trim, "ForeArm." + side)
        if s >= 1:
            B.sphere((sg * 0.28, 0, 1.47), (0.09, 0.09, 0.045), trim, "UpperArm." + side, u=14, v=6, smooth=True)
        if s == 2:
            B.cone((sg * 0.1, 0.06, 1.5), (sg * 0.17, 0.1, 1.74), 0.04, 0.0, trim, "Chest", seg=6)
    if s == 2:
        B.sphere((0, -0.16, 1.4), (0.035, 0.02, 0.04), glow, "Chest", u=10, v=6, smooth=True)

def dw_pants(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    B.cone((0, 0, 1.1), (0, 0, 0.12), 0.17, 0.33, arm, "Hips", seg=16, smooth=True)
    B.ring((0, 0, 0.13), 0.32, 0.02, trim, "Hips")
    B.ring((0, 0, 1.06), 0.165, 0.03, trim, "Hips", sy=0.75)
    if s >= 1:
        box(B, (0, -0.25, 0.6), (0.14, 0.02, 0.86), arm2, "Hips", rot=q(X, -9.3))
    if s == 2:
        B.cone((0, 0, 1.08), (0, 0, 0.55), 0.19, 0.3, arm2, "Hips", seg=16, smooth=True)
        B.ring((0, 0, 0.56), 0.295, 0.018, trim, "Hips")
        B.sphere((0, -0.16, 1.02), (0.03, 0.02, 0.03), glow, "Hips", u=8, v=6, smooth=True)

def dw_gloves(B, J, M, s):
    arm2, trim, glow = M["arm2"], M["trim"], M["glow"]
    for side, sg in SIDES:
        a, b = along(J, "ForeArm." + side, 0.45), along(J, "ForeArm." + side, 1.0)
        B.cone(a, b, 0.052, 0.046, arm2, "ForeArm." + side, seg=10, smooth=True)
        h = Vector(J["Hand." + side][0])
        B.sphere(h + Vector((0, -0.01, -0.04)), (0.05, 0.045, 0.06), arm2, "Hand." + side, u=10, v=6, smooth=True)
        if s >= 1:
            B.ring(b + Vector((0, 0, 0.01)), 0.05, 0.012, trim, "ForeArm." + side)
        if s == 2:
            B.sphere(along(J, "ForeArm." + side, 0.75) + Vector((sg * 0.05, 0, 0)), (0.015, 0.025, 0.025), glow, "ForeArm." + side, u=8, v=6, smooth=True)

def dw_boots(B, J, M, s):
    arm2, trim = M["arm2"], M["trim"]
    for side, sg in SIDES:
        k = Vector(J["Shin." + side][0])
        B.cone(k + Vector((0, 0, -0.12)), k + Vector((0, 0.01, -0.4)), 0.068, 0.062, arm2, "Shin." + side, seg=10, smooth=True)
        f = Vector(J["Foot." + side][0])
        B.sphere(f + Vector((0, -0.06, -0.035)), (0.064, 0.13, 0.054), arm2, "Foot." + side, u=12, v=6, smooth=True)
        if s >= 1:
            B.ring(k + Vector((0, 0, -0.13)), 0.07, 0.014, trim, "Shin." + side)
        if s == 2:
            B.cone(f + Vector((0, -0.17, -0.03)), f + Vector((0, -0.24, 0.03)), 0.03, 0.0, trim, "Foot." + side, seg=6)

def dw_helm(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    if s == 1:   # a tall pointed hat with a wide brim
        B.sphere((0, 0, 1.8), (0.25, 0.25, 0.02), arm, "Head", u=20, v=6, smooth=True)
        B.sphere((0, 0.005, 1.775), (0.132, 0.135, 0.09), arm, "Head", u=16, v=8, smooth=True)
        B.cone((0, 0, 1.8), (0, 0.13, 2.17), 0.135, 0.0, arm, "Head", seg=14, smooth=True)
        B.ring((0, 0.005, 1.83), 0.125, 0.018, trim, "Head")
        return
    def hood(co):  # flatten the front so the face opening shows
        return Vector((co.x, -0.07, co.z)) if co.y < -0.07 and 1.6 < co.z < 1.8 else co
    B.sphere((0, 0.01, 1.73), (0.155, 0.16, 0.16), arm2, "Head", u=18, v=12, smooth=True, shape=hood)
    B.sphere((0, -0.075, 1.7), (0.1, 0.03, 0.1), M["shadow"], "Head", u=12, v=8, smooth=True)
    for sg in (1, -1):
        B.sphere((sg * 0.04, -0.125, 1.71), (0.02, 0.01, 0.012), glow, "Head", u=8, v=6, smooth=True)
    B.cone((0, 0.02, 1.85), (0, 0.16, 2.08), 0.11, 0.0, arm2, "Head", seg=12, smooth=True)
    B.ring((0, 0, 1.6), 0.13, 0.02, trim, "Head", sy=0.9)
    if s == 2:   # a gold crown over the hood
        B.ring((0, 0.0, 1.84), 0.14, 0.018, trim, "Head")
        for i in range(7):
            a = math.radians(-90 + (i - 3) * 30)
            c = Vector((math.cos(a) * 0.14, math.sin(a) * 0.14, 1.85))
            B.cone(c, c + Vector((0, 0, 0.09 if i == 3 else 0.06)), 0.02, 0.0, trim, "Head", seg=5)
        B.sphere((0, -0.15, 1.85), (0.022, 0.014, 0.026), glow, "Head", u=8, v=6, smooth=True)

def dw_hair(B, J, M):
    hair = M["hair"]
    B.sphere((0, 0.025, 1.765), (0.132, 0.13, 0.115), hair, "Head", u=16, v=10, smooth=True)
    B.sphere((0, 0.1, 1.58), (0.11, 0.05, 0.16), hair, "Head", u=14, v=8, smooth=True)
    B.sphere((0, -0.085, 1.81), (0.1, 0.04, 0.035), hair, "Head", u=12, v=6, smooth=True)

def dw_staff(B, J, M, t):
    g = Vector(J["Hand.R"][0]) + Vector((0, -0.04, -0.06))
    up = (0.75, 0.85, 0.95, 0.95, 1.0, 1.05)[t - 1]
    top, bottom = g + Vector((0, -0.04, up)), g + Vector((0, 0.03, -0.7))
    shaft = (M["wwood"], M["wwood"], M["wwood"], M["wdark"], M["wgold"], M["wred"])[t - 1]
    orb = (M["wblue"], M["wblue"], M["wblue"], M["wpurple"], M["wlight"], M["wember"])[t - 1]
    R = (0.04, 0.05, 0.075, 0.085, 0.08, 0.1)[t - 1]
    B.cone(bottom, top, 0.02, 0.024 + 0.002 * t, shaft, "Weapon", seg=8)
    c = top + Vector((0, 0, R + 0.02))
    B.sphere(c, (R, R, R), orb, "Weapon", u=14, v=10, smooth=True)
    if t == 2:
        B.ring(c, R + 0.02, 0.01, M["wgold"], "Weapon")
    if t == 3:
        for a in range(4):
            d = Vector((math.cos(a * math.pi / 2), math.sin(a * math.pi / 2), 0))
            B.cone(top, top + d * 0.08 + Vector((0, 0, 0.17)), 0.016, 0.0, M["wgold"], "Weapon", seg=6)
    if t == 4:   # three dark curved spikes round a purple orb
        for a in range(3):
            d = Vector((math.cos(a * 2.094), math.sin(a * 2.094), 0))
            mid = top + d * 0.1 + Vector((0, 0, 0.08))
            B.cone(top, mid, 0.022, 0.014, M["wdark"], "Weapon", seg=6)
            B.cone(mid, mid - d * 0.03 + Vector((0, 0, 0.16)), 0.014, 0.0, M["wdark"], "Weapon", seg=6)
    if t == 5:   # a gold crescent cradles the light
        pts = [c + Vector((0.16 * math.cos(a), 0, 0.16 * math.sin(a))) for a in [math.radians(200 + i * 17.5) for i in range(9)]]
        for a, b in zip(pts, pts[1:]):
            B.cone(a, b, 0.02, 0.02, M["wgold"], "Weapon", seg=6)
        B.ring(top, 0.035, 0.012, M["wgold"], "Weapon")
    if t == 6:   # a burning orb in gold wings
        for i in range(8):
            a = i * math.pi / 4
            d = Vector((math.cos(a), math.sin(a), 0))
            B.cone(c + d * R * 0.8, c + d * (R + 0.06) + Vector((0, 0, 0.1)), 0.03, 0.0, M["wember"], "Weapon", seg=5)
        for sg in (1, -1):
            box(B, top + Vector((sg * 0.13, 0, 0.05)), (0.2, 0.015, 0.07), M["wgold"], "Weapon", rot=q(Y, sg * -25))

def dark_wizard(B, J, M):
    B.part = "Body"
    body(B, J, M["skin"], M["under"], M["shoe"], eyes=M["eye"])
    box(B, (0, 0.19, 1.12), (0.4, 0.025, 0.72), M["cape"], "Cape", rot=q(X, -6))
    B.part = "Hair"
    dw_hair(B, J, M)

# ---------------------------------------------------------------- Elf
def elf_armor(B, J, M, s):
    arm, arm2, trim, glow = M["arm"], M["arm2"], M["trim"], M["glow"]
    B.sphere((0, -0.01, 1.37), (0.205, 0.14, 0.15), arm, "Chest", u=16, v=10, smooth=True)
    B.sphere((0, -0.1, 1.4), (0.13, 0.06, 0.09), arm2, "Chest", u=12, v=8, smooth=True)
    B.sphere((0, -0.01, 1.2), (0.155, 0.11, 0.12), arm, "Spine", u=14, v=8, smooth=True)
    for side, sg in SIDES:
        c = Vector((sg * 0.23, 0, 1.46))
        r = 0.085 + 0.015 * s
        B.sphere(c, (r, r, r * 0.7), arm2, "UpperArm." + side, u=12, v=8, smooth=True)
        B.ring(c + Vector((0, 0, -0.03)), r * 0.95, 0.012, trim, "UpperArm." + side)
        if s >= 1:   # layered leaf guards
            for i in range(s + 1):
                p = c + Vector((sg * 0.04, 0, -0.05 - i * 0.05))
                B.sphere(p, (0.07, 0.08, 0.025), arm if i % 2 else trim, "UpperArm." + side, u=10, v=6, smooth=True,
                         rot=q(Y, sg * 35))
    if s == 2:
        B.sphere((0, -0.16, 1.43), (0.03, 0.018, 0.035), glow, "Chest", u=10, v=6, smooth=True)
        B.ring((0, 0.0, 1.5), 0.14, 0.016, trim, "Chest", sy=0.85)

def elf_pants(B, J, M, s):
    arm, arm2, trim = M["arm"], M["arm2"], M["trim"]
    B.cone((0, 0, 1.06), (0, 0, 0.7), 0.17, 0.27, arm2, "Hips", seg=14, smooth=True)
    B.ring((0, 0, 1.05), 0.17, 0.02, trim, "Hips", sy=0.75)
    if s >= 1:
        for sg in (1, -1):
            box(B, (sg * 0.22, 0, 0.82), (0.03, 0.18, 0.3 + 0.08 * s), arm, "Hips", rot=q(Y, sg * 14))
    if s == 2:
        B.cone((0, 0, 1.04), (0, 0, 0.82), 0.175, 0.24, arm, "Hips", seg=14, smooth=True)
        B.ring((0, 0, 0.71), 0.265, 0.014, trim, "Hips")

def elf_gloves(B, J, M, s):
    arm, trim = M["arm"], M["trim"]
    for side, sg in SIDES:
        f = along(J, "ForeArm." + side, 0.6)
        B.cone(f + Vector((0, 0, 0.07)), f + Vector((0, 0, -0.1)), 0.05, 0.052, arm, "ForeArm." + side, seg=10, smooth=True)
        if s >= 1:
            B.ring(f + Vector((0, 0, -0.1)), 0.052, 0.01, trim, "ForeArm." + side)
            B.ring(f + Vector((0, 0, 0.07)), 0.05, 0.01, trim, "ForeArm." + side)
        if s == 2:
            B.cone(f + Vector((sg * 0.045, 0, -0.06)), f + Vector((sg * 0.09, 0.04, 0.12)), 0.022, 0.0, trim, "ForeArm." + side, seg=5)

def elf_boots(B, J, M, s):
    arm, trim = M["arm"], M["trim"]
    for side, sg in SIDES:
        k = Vector(J["Shin." + side][0])
        B.cone(k + Vector((0, 0, -0.02)), k + Vector((0, 0.01, -0.4)), 0.07, 0.062, arm, "Shin." + side, seg=10, smooth=True)
        f = Vector(J["Foot." + side][0])
        B.sphere(f + Vector((0, -0.06, -0.035)), (0.062, 0.125, 0.053), arm, "Foot." + side, u=12, v=6, smooth=True)
        if s >= 1:
            B.sphere(k + Vector((0, -0.05, 0)), (0.05, 0.035, 0.05), trim, "Shin." + side, u=10, v=6, smooth=True)
        if s == 2:
            B.cone(k + Vector((sg * 0.065, 0.02, -0.08)), k + Vector((sg * 0.14, 0.07, 0.04)), 0.028, 0.0, trim, "Shin." + side, seg=5)

def elf_helm(B, J, M, s):
    arm2, trim, glow = M["arm2"], M["trim"], M["glow"]
    if s == 2:   # winged half-helm over the hair
        B.sphere((0, 0.025, 1.785), (0.142, 0.142, 0.1), arm2, "Head", u=18, v=10, smooth=True)
        B.cone((0, -0.06, 1.87), (0, 0.06, 1.95), 0.03, 0.0, trim, "Head", seg=6)
    B.ring((0, 0, 1.79), 0.138, 0.011, trim, "Head", sy=0.95)
    B.sphere((0, -0.138, 1.8), (0.02, 0.011, 0.024), glow, "Head", u=8, v=6, smooth=True)
    if s >= 1:   # leaf wings at the temples
        n = 2 if s == 1 else 3
        for sg in (1, -1):
            for i in range(n):
                base = Vector((sg * 0.13, -0.02 + i * 0.03, 1.8))
                B.cone(base, base + Vector((sg * (0.06 + 0.02 * s), 0.06 + i * 0.03, 0.1 + 0.05 * s - i * 0.03)), 0.022, 0.0, trim, "Head", seg=5)

def elf_bow(B, J, M, t):
    g = Vector(J["Hand.L"][0]) + Vector((0, -0.03, -0.06))
    size, depth = 0.45 + 0.03 * t, 0.1 + 0.012 * t
    limb_m = (M["wwood"], M["wwood"], M["wwood"], M["wdark"], M["wgold"], M["wred"])[t - 1]
    tip_m = (M["wwood"], M["wgold"], M["wgold"], M["wgreen"], M["wlight"], M["wember"])[t - 1]
    string = M["wember"] if t == 6 else M["wstring"]
    pts = []
    for i in range(11):
        u = (i / 10.0) * 2 - 1
        # recurve: the ends flick forward again from tier 3
        flick = 0.04 * max(0, abs(u) - 0.75) / 0.25 if t >= 3 else 0
        pts.append(g + Vector((0, -depth * (1 - u * u) + flick, size * u)))
    for a, b in zip(pts, pts[1:]):
        B.cone(a, b, 0.016 + 0.002 * t, 0.016 + 0.002 * t, limb_m, "Weapon", seg=6)
    B.sphere(g, (0.026, 0.026, 0.05), M["wgold"] if t >= 2 else M["wwood"], "Weapon", u=8, v=6, smooth=True)
    B.cone(pts[0], pts[-1], 0.004, 0.004, string, "Weapon", seg=4)
    for e in (pts[0], pts[-1]):
        B.sphere(e, (0.02 + 0.004 * t,) * 3, tip_m, "Weapon", u=8, v=6, smooth=True)
    if t >= 4:   # blades / feathers along the limbs
        for i in (2, 3, 7, 8):
            p = pts[i]
            B.cone(p, p + Vector((0, -0.07 - 0.01 * t, 0.03 if i > 5 else -0.03)), 0.02, 0.0, tip_m, "Weapon", seg=5)

def elf(B, J, M):
    B.part = "Body"
    body(B, J, M["skin"], M["under"], M["shoe"], eyes=M["eye"])
    hair, wood = M["hair"], M["wwood"]
    B.sphere((0, 0.035, 1.75), (0.135, 0.13, 0.14), hair, "Head", u=18, v=12, smooth=True)
    B.sphere((0, 0.11, 1.5), (0.11, 0.06, 0.24), hair, "Head", u=14, v=10, smooth=True)
    B.sphere((0, -0.09, 1.79), (0.11, 0.045, 0.04), hair, "Head", u=12, v=6, smooth=True)
    for sg in (1, -1):
        B.cone((sg * 0.11, 0.0, 1.71), (sg * 0.21, 0.04, 1.8), 0.025, 0.0, M["skin"], "Head", seg=6, smooth=True)
    B.cone((0.06, 0.16, 1.05), (-0.08, 0.18, 1.5), 0.05, 0.055, M["shoe"], "Chest", seg=10)   # quiver
    for i in range(3):
        B.cone((-0.07 + i * 0.02, 0.18, 1.48), (-0.1 + i * 0.02, 0.19, 1.62), 0.006, 0.006, wood, "Chest", seg=4)
        B.cone((-0.1 + i * 0.02, 0.19, 1.62), (-0.1 + i * 0.02, 0.19, 1.66), 0.018, 0.0, M["wstring"], "Chest", seg=4)

# ---------------------------------------------------------------- classes
GEAR = {
    "DarkKnight": (dark_knight, {"Armor": dk_armor, "Pants": dk_pants, "Gloves": dk_gloves, "Boots": dk_boots, "Helm": dk_helm}, dk_sword),
    "DarkWizard": (dark_wizard, {"Armor": dw_armor, "Pants": dw_pants, "Gloves": dw_gloves, "Boots": dw_boots, "Helm": dw_helm}, dw_staff),
    "Elf": (elf, {"Armor": elf_armor, "Pants": elf_pants, "Gloves": elf_gloves, "Boots": elf_boots, "Helm": elf_helm}, elf_bow),
}

WEAPON_MATS = {
    "wblade": ((200, 210, 225), 0.18, 0.95), "wwood": ((110, 74, 42), 0.7, 0), "wgold": ((222, 176, 70), 0.3, 0.9),
    "wdark": ((26, 24, 30), 0.4, 0.4), "wstring": ((236, 236, 228), 0.5, 0),
    "wred": ((220, 40, 40), 0.3, 0, (255, 40, 30)), "wblue": ((90, 180, 255), 0.1, 0, (90, 180, 255)),
    "wember": ((255, 140, 40), 0.2, 0, (255, 120, 20)), "wpurple": ((170, 70, 230), 0.1, 0, (170, 70, 255)),
    "wlight": ((255, 240, 180), 0.1, 0, (255, 230, 150)), "wgreen": ((80, 230, 140), 0.1, 0, (60, 230, 120)),
}

CLASSES = {
    "DarkKnight": ("R", {
        "skin": ((232, 190, 160), 0.6, 0), "under": ((48, 44, 52), 0.7, 0), "shoe": ((72, 54, 40), 0.7, 0),
        "eye": ((30, 20, 20), 0.2, 0), "hair": ((40, 32, 30), 0.6, 0), "cape": ((150, 24, 30), 0.7, 0),
        "arm": (None, 0.32, 0.85), "arm2": (None, 0.4, 0.3), "trim": (None, 0.3, 0.9), "glow": (None, 0.1, 0, True)}),
    "DarkWizard": ("R", {
        "skin": ((232, 190, 160), 0.6, 0), "under": ((44, 46, 70), 0.75, 0), "shoe": ((60, 46, 40), 0.7, 0),
        "eye": ((40, 50, 80), 0.2, 0), "hair": ((200, 205, 220), 0.6, 0), "cape": ((88, 36, 120), 0.7, 0),
        "shadow": ((10, 10, 18), 0.9, 0),
        "arm": (None, 0.65, 0), "arm2": (None, 0.7, 0), "trim": (None, 0.3, 0.9), "glow": (None, 0.1, 0, True)}),
    "Elf": ("L", {
        "skin": ((240, 204, 178), 0.6, 0), "under": ((120, 150, 100), 0.7, 0), "shoe": ((110, 78, 48), 0.7, 0),
        "eye": ((40, 120, 70), 0.2, 0), "hair": ((246, 214, 120), 0.5, 0), "cape": ((60, 132, 72), 0.6, 0),
        "arm": (None, 0.6, 0), "arm2": (None, 0.6, 0), "trim": (None, 0.3, 0.9), "glow": (None, 0.1, 0, True)}),
}

GEAR_KEYS = ("arm", "arm2", "trim", "glow")

def set_tier_colors(prefix, cls, tier):
    """Recolour the armour materials like GearVisual does in Unity (for the previews)."""
    for key, rgb in zip(GEAR_KEYS, TIER_COLORS[cls][tier - 1]):
        m = bpy.data.materials.get(prefix + "_" + key)
        if not m:
            continue
        col = (*srgb(*rgb), 1.0)
        m.diffuse_color = col
        bsdf = next((n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
        if bsdf:
            bsdf.inputs["Base Color"].default_value = col
            if key == "glow":
                bsdf.inputs["Emission Color"].default_value = col
                bsdf.inputs["Emission Strength"].default_value = 2.0

def show_outfit(tier, slots=GEAR_SLOTS, weapon=True):
    """Hide everything but the body, the pieces for `slots` at this tier and the tier's weapon."""
    want = {"G_Body"} | {"G_%s%d" % (sl, stage_of(tier)) for sl in slots}
    if weapon:
        want.add("G_Weapon%d" % tier)
    if "Helm" not in slots:
        want.add("G_Hair")
    for o in bpy.data.objects:
        if o.type == "MESH":
            o.hide_render = o.name not in want

def build_class(name, out_dir):
    clear_scene()
    scn = bpy.context.scene
    scn.render.fps = FPS
    weapon_side, palette = CLASSES[name]
    make, pieces, weapon = GEAR[name]
    prefix = name[:2]
    mats, M = [], {}
    for key, spec in list(palette.items()) + list(WEAPON_MATS.items()):
        rgb, rough, metal = spec[0], spec[1], spec[2]
        if rgb is None:   # armour material: start with the tier 3 colours
            rgb = TIER_COLORS[name][2][GEAR_KEYS.index(key)]
        emit = None
        if len(spec) > 3:
            emit = srgb(*rgb) if spec[3] is True else srgb(*spec[3])
        M[key] = len(mats)
        mats.append(make_mat(prefix + "_" + key, srgb(*rgb), rough=rough, metal=metal, emit=emit, strength=2.0))
    J, P = skeleton(weapon_side)
    B = Builder(BONES)
    make(B, J, M)
    for slot in GEAR_SLOTS:
        for s in range(STAGES):
            B.part = "%s%d" % (slot, s)
            pieces[slot](B, J, M, s)
    for t in range(1, TIERS + 1):
        B.part = "Weapon%d" % t
        weapon(B, J, M, t)
    objs = B.to_objects(mats)
    arm_obj = build_armature(name, J, P)
    for obj in objs:
        obj.parent = arm_obj
        obj.modifiers.new("Armature", "ARMATURE").object = arm_obj
    for clip, (length, loop, keys) in actions_for(name).items():
        make_action(arm_obj, clip, length, keys, loop)
    arm_obj.animation_data.action = bpy.data.actions["Idle"]
    scn.frame_set(0)
    path = os.path.join(out_dir, name, name + "_Rigged.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_ARGS)
    print("[ok]", path, len(objs), "objects")
    prev = os.path.normpath(os.path.join(out_dir, "..", "..", "Tools", "Previews"))
    os.makedirs(prev, exist_ok=True)
    show_outfit(1, slots=(), weapon=True)
    preview(os.path.join(prev, name + "_Gear0.png"), (0, 0, 1.0), 2.6, 0.9, "Idle", 0)
    for t in range(1, TIERS + 1):
        set_tier_colors(prefix, name, t)
        show_outfit(t)
        preview(os.path.join(prev, name + "_Gear%d.png" % t), (0, 0, 1.0), 2.6, 0.9, "Idle", 0)
    set_tier_colors(prefix, name, 3)
    show_outfit(3)
    preview(os.path.join(prev, name + "_Preview.png"), (0, 0, 1.0), 2.6, 0.9, "Idle", 0)
    preview(os.path.join(prev, name + "_Attack.png"), (0, 0, 1.0), 2.6, 0.9, "Attack01", 12)
    for o in bpy.data.objects:
        o.hide_render = False

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

if __name__ == "__main__":
    main()
