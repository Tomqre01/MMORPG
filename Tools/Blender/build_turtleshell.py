"""
Our own spiky TurtleShell monster, modeled, rigged and animated in Blender.

Modeled after the idea of the RPG Monster Duo TurtleShell (a spiked shell that opens like a clam,
with the creature peeking out between the lid and the base) and animated with the same clip
names and lengths (30 fps):
    IdleNormal, IdleBattle, Walk, WalkBack, WalkLeft, WalkRight, Run, Attack01, Attack02,
    Defend, DefendHit, GetHit, Dizzy, Die, Taunt, Victory, SenseSomethingStart, SenseSomethingRT

Blender 4.2+ (tested on 5.2). Run in Blender's Python console, or:
    blender -b -P build_turtleshell.py -- "C:/path/to/YourUnityProject/Assets/Resources/Models"
Writes Models/Turtle/Turtle_Rigged.fbx and a preview in Assets/Tools/Previews.
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector, Matrix, Quaternion

MODELS_DIR = r""   # e.g. r"D:\MMORPG\My project\Assets\Resources\Models"
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
def build(out_dir):
    clear_scene()
    scn = bpy.context.scene
    scn.render.fps = FPS
    bones = ["Root", "Body", "Shell", "Head", "Leg.FL", "Leg.FR", "Leg.BL", "Leg.BR", "Tail"]
    HINGE = (0, 0.6, 0.44)  # the lid hinges at the back like a clam
    joints = {"Root": ((0, 0, 0), (0, 0, 0.12)),
              "Body": ((0, 0, 0.0), (0, 0, 0.3)),
              "Shell": (HINGE, (0, 0.6, 0.9)),
              "Head": ((0, -0.4, 0.28), (0, -0.85, 0.32)),
              "Tail": ((0, 0.62, 0.18), (0, 0.95, 0.12))}
    for side, sx in (("L", 1), ("R", -1)):
        joints["Leg.F" + side] = ((sx * 0.42, -0.3, 0.16), (sx * 0.6, -0.44, 0.02))
        joints["Leg.B" + side] = ((sx * 0.42, 0.32, 0.16), (sx * 0.6, 0.44, 0.02))
    parents = {"Root": None, "Body": "Root", "Shell": "Body", "Head": "Body", "Tail": "Body",
               "Leg.FL": "Body", "Leg.FR": "Body", "Leg.BL": "Body", "Leg.BR": "Body"}
    mats = [make_mat("TS_Shell", srgb(38, 104, 118), rough=0.4),
            make_mat("TS_Plate", srgb(62, 150, 152), rough=0.35),
            make_mat("TS_Lip", srgb(150, 86, 46), rough=0.5),
            make_mat("TS_Base", srgb(214, 186, 128), rough=0.6),
            make_mat("TS_Horn", srgb(238, 226, 196), rough=0.35),
            make_mat("TS_HornTip", srgb(196, 60, 40), rough=0.4),
            make_mat("TS_Skin", srgb(128, 170, 84), rough=0.55),
            make_mat("TS_EyeWhite", srgb(250, 246, 230), rough=0.2),
            make_mat("TS_Pupil", srgb(20, 16, 14), rough=0.1),
            make_mat("TS_Mouth", srgb(46, 60, 30), rough=0.5),
            make_mat("TS_Glint", srgb(255, 255, 255), rough=0.2, emit=srgb(255, 255, 255)),
            make_mat("TS_SkinDark", srgb(88, 122, 56), rough=0.55)]
    SHELL, PLATE, LIP, BASE, HORN, TIP, SKIN, WHITE, PUPIL, MOUTH, GLINT, SKIN2 = range(len(mats))
    B = Builder(bones)

    # base: a shallow bowl the creature sits in
    def bowl(co):
        z = co.z * (0.18 if co.z > 0 else 0.34)
        return Vector((co.x, co.y, z + 0.2))
    B.sphere((0, 0, 0), (0.72, 0.74, 1.0), BASE, "Body", u=24, v=10, smooth=True, shape=bowl)
    # lid: spiked dome with alternating plates
    C = Vector((0, 0.03, 0.46))
    RX, RY, RZ = 0.68, 0.7, 0.56
    def dome(co):
        return Vector((co.x, co.y, max(co.z, 0.0) + 0.46))
    _, faces = B.sphere((0, 0.03, 0), (RX, RY, RZ), SHELL, "Shell", u=14, v=10, shape=dome)
    for f in faces:
        c = f.calc_center_median()
        if c.z > 0.54:
            ring = int((c.z - 0.54) / 0.1)
            ang = int((math.atan2(c.y, c.x) + math.pi) / (2 * math.pi) * 14)
            if (ring + ang) % 2 == 0:
                f.material_index = PLATE
    B.ring((0, 0.03, 0.47), 0.69, 0.065, LIP, "Shell", sy=0.7 / 0.69 * 1.0, rz=0.9)
    # horns: 3 along the crest, 4 per side
    def on_dome(az, el, out=0.0):
        a, e = math.radians(az), math.radians(el)
        d = Vector((math.cos(e) * math.sin(a), -math.cos(e) * math.cos(a), math.sin(e)))
        p = C + Vector((d.x * RX, d.y * RY, d.z * RZ))
        n = Vector((d.x / RX, d.y / RY, d.z / RZ)).normalized()
        return p - n * 0.03, n
    horns = [(0, 90, 0.34), (0, 48, 0.26), (180, 48, 0.26)]
    for sx in (-1, 1):
        horns += [(sx * a, 22, 0.24) for a in (38, 80, 122, 160)]
        horns += [(sx * 70, 60, 0.18)]
    for az, el, length in horns:
        p, n = on_dome(az, el)
        tip = p + n * length
        mid = p + n * length * 0.72
        B.cone(p, mid, 0.075, 0.03, HORN, "Shell", seg=8)
        B.cone(mid, tip, 0.032, 0.0, TIP, "Shell", seg=8)
    # the creature peeking out at the front
    # turtle head on a neck, poking out under the lid
    B.sphere((0, -0.55, 0.27), (0.12, 0.2, 0.1), SKIN, "Head", u=12, v=8, smooth=True)          # neck
    B.sphere((0, -0.76, 0.3), (0.16, 0.2, 0.13), SKIN, "Head", u=16, v=10, smooth=True)         # head
    for sx in (-1, 1):
        B.sphere((sx * 0.095, -0.875, 0.37), (0.048, 0.028, 0.048), WHITE, "Head", u=12, v=8, smooth=True,
                 rot=q(Z, -sx * 20))                                                            # eyes
        B.sphere((sx * 0.09, -0.898, 0.365), (0.024, 0.012, 0.028), PUPIL, "Head", u=10, v=6, smooth=True)
        B.sphere((sx * 0.082, -0.905, 0.378), (0.009, 0.005, 0.009), GLINT, "Head", u=6, v=4, smooth=True)
        B.sphere((sx * 0.1, -0.868, 0.415), (0.06, 0.018, 0.014), SKIN2, "Head", u=8, v=4,
                 rot=q(Y, -sx * 15))                                                            # angry brow
        B.sphere((sx * 0.03, -0.95, 0.33), (0.012, 0.008, 0.01), PUPIL, "Head", u=6, v=4)        # nostrils
    B.sphere((0, -0.948, 0.275), (0.075, 0.012, 0.01), MOUTH, "Head", u=10, v=4, smooth=True)    # mouth line
    for x, y, r in ((0.06, -0.7, 0.04), (-0.07, -0.66, 0.035), (0.0, -0.78, 0.03)):
        B.sphere((x, y, 0.415), (r, r, 0.012), SKIN2, "Head", u=8, v=4)                          # head spots
    # stubby legs with little claws, and a tail
    for name, (h, t) in joints.items():
        if name.startswith("Leg"):
            h, t = Vector(h), Vector(t)
            B.sphere((h + t) / 2, (0.13, 0.15, 0.09), SKIN, name, u=10, v=6, smooth=True,
                     rot=Vector((0, 1, 0)).rotation_difference((t - h).normalized()))
            for k in (-1, 0, 1):
                c = t + Vector((k * 0.045, -0.06 if t.y < 0 else 0.06, 0.0))
                B.sphere(c, (0.022, 0.022, 0.018), HORN, name, u=6, v=4)
    B.cone((0, 0.66, 0.18), (0, 0.95, 0.1), 0.07, 0.0, SKIN, "Tail", seg=8, smooth=True)
    obj = B.to_object("TurtleShell", mats)
    arm = build_armature("TurtleShell", joints, parents)
    obj.parent = arm
    obj.modifiers.new("Armature", "ARMATURE").object = arm

    # ------------------------------------------------------------ animations (30 fps, same lengths as the pack)
    H = 1.0
    def lid(deg):          # negative opens the lid (front goes up), positive clamps it shut
        return {"r": q(X, deg)}
    def head(fwd=0.0, up=0.0, yaw=0.0, pitch=0.0):
        return {"t": (0, -fwd, up), "r": q(Z, yaw) @ q(X, pitch)}
    def root(lift=0.0, back=0.0, side=0.0, pitch=0.0, roll=0.0, yaw=0.0, s=None):
        e = {"t": (side, back, lift), "r": q(Z, yaw) @ q(X, pitch) @ q(Y, roll)}
        if s:
            e["s"] = s
        return e
    tuck = {n: {"t": ((-0.12 if n.endswith("L") else 0.12), (0.08 if ".F" in n else -0.08), 0.04)}
            for n in ("Leg.FL", "Leg.FR", "Leg.BL", "Leg.BR")}
    closed = dict(tuck, Shell=lid(2), Head=head(-0.3, -0.04), Root=root(s=(1.04, 0.94, 1.04)))

    def walk_cycle(n, back=0.0, side=0.0, roll_amp=7, lift=0.05, lean=6):
        k = {}
        for i, f in enumerate((0, n // 4, n // 2, 3 * n // 4)):
            s = (1, 0, -1, 0)[i]
            k[f] = {"Root": root(lift=lift * H * (1 - abs(s)), roll=roll_amp * s, pitch=lean, side=side * s),
                    "Shell": lid(-6 - 3 * (1 - abs(s))), "Head": head(0.02, 0, yaw=4 * s),
                    "Leg.FL": {"r": q(X, -30 * s)}, "Leg.BR": {"r": q(X, -30 * s)},
                    "Leg.FR": {"r": q(X, 30 * s)}, "Leg.BL": {"r": q(X, 30 * s)},
                    "Tail": {"r": q(Z, -18 * s)}}
        return k

    actions = {
        "IdleNormal": (50, True, {0: {"Shell": lid(-5), "Head": head(0, 0)},
                                  25: {"Shell": lid(-9), "Head": head(0.03, 0.01, pitch=-5),
                                       "Root": root(s=(1.02, 0.98, 1.02))}}),
        "IdleBattle": (24, True, {0: {"Shell": lid(-10), "Head": head(0.05), "Root": root(pitch=6)},
                                  12: {"Shell": lid(-15), "Head": head(0.07, 0.01), "Root": root(lift=0.03, pitch=8)}}),
        "Walk": (30, True, walk_cycle(30)),
        "WalkBack": (30, True, walk_cycle(30, lean=-6)),
        "WalkLeft": (30, True, walk_cycle(30, roll_amp=12, lean=2)),
        "WalkRight": (30, True, walk_cycle(30, roll_amp=12, lean=2)),
        "Run": (16, True, walk_cycle(16, roll_amp=9, lift=0.12, lean=14)),
        "Attack01": (25, False, {0: {"Shell": lid(-10)},
                                 8: {"Shell": lid(-34), "Head": head(-0.04, 0.02, pitch=-15), "Root": root(back=0.06, pitch=-8)},
                                 12: {"Shell": lid(-28), "Head": head(0.13, 0.02, pitch=8), "Root": root(back=-0.12, pitch=10)},
                                 14: {"Shell": lid(2), "Head": head(0.08), "Root": root(back=-0.14, pitch=8, s=(1.08, 0.9, 1.08))},
                                 25: {"Shell": lid(-10)}}),
        "Attack02": (25, False, {0: {"Shell": lid(-10)},
                                 5: dict(closed, Root=root(lift=0.1, yaw=0, s=(1.04, 0.94, 1.04))),
                                 10: dict(closed, Root=root(lift=0.16, yaw=120)),
                                 15: dict(closed, Root=root(lift=0.16, yaw=240)),
                                 19: dict(closed, Root=root(lift=0.0, yaw=360, s=(1.12, 0.86, 1.12))),
                                 25: {"Shell": lid(-10)}}),
        "Defend": (17, False, {0: {"Shell": lid(-6)}, 6: dict(closed), 17: dict(closed)}),
        "DefendHit": (7, False, {0: dict(closed), 2: dict(closed, Root=root(back=0.1, pitch=-8, s=(1.08, 0.9, 1.08))),
                                 7: dict(closed)}),
        "GetHit": (25, False, {0: {"Shell": lid(-8)},
                               4: {"Shell": lid(-28), "Head": head(-0.08, 0.04, pitch=-20), "Root": root(back=0.1, pitch=-12)},
                               10: {"Shell": lid(-4), "Head": head(-0.02), "Root": root(back=0.03, pitch=3)},
                               25: {"Shell": lid(-8)}}),
        "Dizzy": (40, True, {0: {"Shell": lid(-18), "Head": head(0.05, -0.02, yaw=18, pitch=12), "Root": root(roll=8)},
                             10: {"Shell": lid(-14), "Head": head(0.05, -0.03, yaw=0, pitch=16), "Root": root(pitch=8)},
                             20: {"Shell": lid(-18), "Head": head(0.05, -0.02, yaw=-18, pitch=12), "Root": root(roll=-8)},
                             30: {"Shell": lid(-14), "Head": head(0.05, -0.03, yaw=0, pitch=16), "Root": root(pitch=-6)}}),
        "Die": (60, False, {0: {"Shell": lid(-8)},
                            8: {"Shell": lid(-45), "Head": head(0.1, 0.05, pitch=-25), "Root": root(back=0.05, pitch=-10)},
                            22: {"Shell": lid(-30), "Head": head(0.05, 0.0, pitch=-10), "Root": root(lift=0.98, roll=90)},
                            36: {"Shell": lid(-10), "Head": head(-0.05, -0.03, pitch=30),
                                 "Root": root(lift=1.34, roll=180)},
                            44: {"Shell": lid(-12), "Head": head(0.02, -0.04, pitch=40), "Root": root(lift=1.4, roll=176),
                                 "Leg.FL": {"r": q(X, 35)}, "Leg.BR": {"r": q(X, -35)}, "Leg.FR": {"r": q(X, -30)}, "Leg.BL": {"r": q(X, 30)}},
                            52: {"Shell": lid(-10), "Head": head(0.02, -0.05, pitch=42), "Root": root(lift=1.37, roll=179),
                                 "Leg.FL": {"r": q(X, -30)}, "Leg.BR": {"r": q(X, 30)}, "Leg.FR": {"r": q(X, 35)}, "Leg.BL": {"r": q(X, -35)}},
                            60: {"Shell": lid(-8), "Head": head(0.02, -0.05, pitch=45), "Root": root(lift=1.36, roll=180)}}),
        "Taunt": (50, False, {0: {"Shell": lid(-8)},
                              6: {"Shell": lid(-35), "Root": root(lift=0.08)}, 10: {"Shell": lid(2), "Root": root(s=(1.06, 0.92, 1.06))},
                              16: {"Shell": lid(-35), "Root": root(lift=0.08)}, 20: {"Shell": lid(2), "Root": root(s=(1.06, 0.92, 1.06))},
                              26: {"Shell": lid(-38), "Head": head(0.12, 0.03, pitch=-15), "Root": root(lift=0.1, pitch=-6)},
                              40: {"Shell": lid(-38), "Head": head(0.12, 0.03, yaw=12, pitch=-15)},
                              50: {"Shell": lid(-8)}}),
        "Victory": (80, False, {0: {"Shell": lid(-8)},
                                8: {"Shell": lid(2), "Root": root(s=(1.1, 0.88, 1.1))},
                                16: {"Shell": lid(-30), "Root": root(lift=0.35, yaw=120)},
                                24: {"Shell": lid(-30), "Root": root(lift=0.35, yaw=240)},
                                32: {"Shell": lid(2), "Root": root(yaw=360, s=(1.1, 0.88, 1.1))},
                                40: {"Shell": lid(-8)},
                                48: {"Shell": lid(2), "Root": root(s=(1.1, 0.88, 1.1))},
                                56: {"Shell": lid(-40), "Head": head(0.1, 0.04, pitch=-20), "Root": root(lift=0.4)},
                                66: {"Shell": lid(-20), "Root": root(s=(1.06, 0.92, 1.06))},
                                80: {"Shell": lid(-8)}}),
        "SenseSomethingStart": (60, False, {0: {"Shell": lid(-6)},
                                            12: {"Shell": lid(-20), "Head": head(0.1, 0.03, pitch=-8)},
                                            24: {"Shell": lid(-20), "Head": head(0.1, 0.03, yaw=30)},
                                            40: {"Shell": lid(-20), "Head": head(0.1, 0.03, yaw=-30)},
                                            60: {"Shell": lid(-20), "Head": head(0.1, 0.03, pitch=-8)}}),
        "SenseSomethingRT": (70, True, {0: {"Shell": lid(-20), "Head": head(0.1, 0.03, pitch=-8)},
                                        18: {"Shell": lid(-24), "Head": head(0.12, 0.03, yaw=35, pitch=-5), "Root": root(yaw=10)},
                                        35: {"Shell": lid(-20), "Head": head(0.1, 0.03, pitch=-8)},
                                        52: {"Shell": lid(-24), "Head": head(0.12, 0.03, yaw=-35, pitch=-5), "Root": root(yaw=-10)}}),
    }
    for name, (length, loop, keys) in actions.items():
        make_action(arm, name, length, keys, loop)

    arm.animation_data.action = bpy.data.actions["IdleNormal"]
    scn.frame_set(0)
    path = os.path.join(out_dir, "Turtle", "Turtle_Rigged.fbx")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.export_scene.fbx(filepath=path, **FBX_ARGS)
    print("[ok]", path)

    prev = os.path.normpath(os.path.join(out_dir, "..", "..", "Tools", "Previews"))
    os.makedirs(prev, exist_ok=True)
    preview(os.path.join(prev, "TurtleShell_Preview.png"), (0, 0, 0.55), 2.2, 0.9, "IdleNormal", 0)
    preview(os.path.join(prev, "TurtleShell_Attack.png"), (0, -0.1, 0.55), 2.2, 0.9, "Attack01", 12)
    preview(os.path.join(prev, "TurtleShell_Die.png"), (0, 0, 0.7), 2.4, 1.0, "Die", 60)
    print("[preview]", prev)

def main():
    folder = MODELS_DIR
    if "--" in sys.argv and sys.argv.index("--") + 1 < len(sys.argv):
        folder = sys.argv[sys.argv.index("--") + 1]
    if not folder or not os.path.isdir(folder):
        raise SystemExit("Set MODELS_DIR (or pass it after --) to your Unity project's Assets/Resources/Models folder")
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    build(folder)
    print("Done. Switch back to Unity and press Play.")

main()
