"""
Our own monster models for the MU-style maps, modeled, rigged and animated in Blender by script.
Original designs in generic fantasy archetypes (not copies of any game's art).

How the bodies are made: each creature is a graph of joints with a thickness at every joint. Blender's
Skin modifier wraps the graph in one continuous hull, Subdivision Surface smooths it, and every vertex
is weighted to its two nearest bones, so limbs bend smoothly instead of being stacked boxes.
Horns, eyes, teeth, weapons and armour are separate pieces fixed to one bone.

Models (all face -Y, feet on the ground, 30 fps clips Idle, Run, Attack01, GetHit, Die):
  Spider      giant spider: round abdomen with markings, eight jointed legs, fangs, red eyes
  BabyDragon  chubby young dragon: horns, little wings, spade tail, cream belly
  Hound       demon hound: lean body, spiked back, pointed ears, glowing eyes
  BullDemon   bull-headed brute: horns, hooves, war axe
  DarkMage    hooded sorcerer: long robe, glowing eyes, staff with an orb
  Giant       big-bellied giant: small head, tusks, loincloth, club
  Skeleton    skeleton soldier: ribcage, skull, rusty sword, round shield

Run inside Blender 5.x (Text Editor > Run Script), or drop it into Tools/BlenderJobs/job.py while
Tools/blender_watch.py is running. Writes Resources/Models/<Name>/<Name>_Rigged.fbx and
Tools/Previews/<Name>.png.
"""
import bpy, bmesh, math, os
from mathutils import Vector, Matrix, Quaternion

MODELS_DIR = r"D:\MMORPG\My project\Assets\Resources\Models"
PREVIEW_DIR = r"D:\MMORPG\My project\Assets\Tools\Previews"
FPS = 30
ONLY = None   # e.g. ["spider"] (builder function names) to rebuild some models

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)

# ------------------------------------------------------------------ Blender plumbing

def ctx():
    wm = bpy.context.window_manager
    win = bpy.context.window or wm.windows[0]
    area = next((a for a in win.screen.areas if a.type == "VIEW_3D"), win.screen.areas[0])
    region = next((r for r in area.regions if r.type == "WINDOW"), area.regions[0])
    return dict(window=win, screen=win.screen, area=area, region=region, scene=bpy.context.scene,
                view_layer=bpy.context.view_layer)

def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for d in list(coll):
            if d.users == 0 or coll is not bpy.data.materials:
                try:
                    coll.remove(d)
                except Exception:
                    pass

def srgb(r, g, b):
    f = lambda c: (c / 255.0) ** 2.2
    return (f(r), f(g), f(b))

def make_mat(name, rgb, rough=0.7, metal=0.0, emit=None, strength=2.0):
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

def q(axis, deg):
    return Quaternion(Vector(axis).normalized(), math.radians(deg))

def Q(*rots):
    """Compose rotations given as (axis, degrees) pairs, applied left to right."""
    out = Quaternion()
    for axis, deg in rots:
        out = q(axis, deg) @ out
    return out

# ------------------------------------------------------------------ the creature builder

class Creature:
    def __init__(self, name):
        self.name = name
        self.nodes = {}       # name -> dict(pos, r, parent, skin_link, skin, deform)
        self.order = []
        self.mats = []
        self.mat_index = {}
        self.parts = []       # callables(bm, deform_layer, bones) adding rigid pieces
        self.face_mat = lambda center, normal, bone: 0
        self.res = 0.035      # metaball resolution (smaller = finer)
        self.budget = 6000    # hull faces after decimation
        self.limb = 0.85      # capsule thickness relative to the thinner joint
        self.tube = 0.065     # joints thinner than this are built as tubes instead of blobs
        self.paw_mat = None   # material key for quadruped paw pads

    def mat(self, key, rgb, **kw):
        self.mat_index[key] = len(self.mats)
        self.mats.append(make_mat(self.name + "_" + key, rgb, **kw))
        return self.mat_index[key]

    def node(self, name, pos, r, parent=None, link=True, skin=True, deform=True, rz=None):
        """r: radius, or (rx = width, ry = depth); rz: height (default the larger of the two).
        link: melted to the parent's blob with a capsule (False starts a separate piece)."""
        if not isinstance(r, tuple):
            r = (r, r)
        if len(r) == 3:
            r, rz = (r[0], r[1]), r[2]
        self.nodes[name] = dict(pos=Vector(pos), r=r, rz=rz, parent=parent, link=link and parent is not None, skin=skin, deform=deform)
        self.order.append(name)

    def mirror(self, name, pos, r, parent, **kw):
        """Adds <name>L at +X and <name>R at -X; parent may contain {s}."""
        for s, sx in (("L", 1), ("R", -1)):
            p = (pos[0] * sx, pos[1], pos[2])
            self.node(name + s, p, r, parent.format(s=s) if parent else None, **kw)

    def bone_of(self, name):
        n = self.nodes[name]
        if n["parent"] is None:
            return n["pos"], n["pos"] + Vector((0, 0, 0.15))
        return self.nodes[n["parent"]]["pos"], n["pos"]

    # rigid pieces --------------------------------------------------
    def sphere(self, center, radii, mat, bone, u=16, v=10, smooth=True, rot=None):
        def add(bm, dl, bones):
            M = Matrix.Translation(Vector(center)) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*radii, 1))
            ret = bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0, matrix=M)
            return ret["verts"], self.mat_index[mat], bone, smooth
        self.parts.append(add)

    def cone(self, base, tip, r1, r2, mat, bone, seg=10, smooth=True):
        def add(bm, dl, bones):
            b, t = Vector(base), Vector(tip)
            d = t - b
            rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
            M = Matrix.Translation((b + t) / 2) @ rot.to_matrix().to_4x4()
            ret = bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=seg, radius1=r1, radius2=r2, depth=d.length, matrix=M)
            return ret["verts"], self.mat_index[mat], bone, smooth
        self.parts.append(add)

    def horn(self, base, direction, length, r, curl, mat, bone, seg=6):
        """A curved horn made of tapering cone segments bending by `curl` degrees around Z per segment."""
        p = Vector(base)
        d = Vector(direction).normalized()
        for i in range(seg):
            r1 = r * (1 - i / seg)
            r2 = r * (1 - (i + 1) / seg) + 0.002
            nxt = p + d * (length / seg)
            self.cone(tuple(p), tuple(nxt), r1, r2, mat, bone, seg=8)
            p = nxt
            d = (Matrix.Rotation(math.radians(curl[0]), 3, "X") @ Matrix.Rotation(math.radians(curl[1]), 3, "Z") @ d).normalized()

    def membrane(self, outline, mat, bone, thick=0.008):
        """A flat polygon (fan from its first point) with a front and a back face."""
        def add(bm, dl, bones):
            pts = [Vector(p) for p in outline]
            n = (pts[1] - pts[0]).cross(pts[2] - pts[0]).normalized() * thick
            front = [bm.verts.new(p + n) for p in pts]
            back = [bm.verts.new(p - n) for p in pts]
            for i in range(1, len(pts) - 1):
                bm.faces.new((front[0], front[i], front[i + 1]))
                bm.faces.new((back[0], back[i + 1], back[i]))
            self._flat.extend(front + back)
            return front + back, self.mat_index[mat], bone, False
        self.parts.append(add)

    def box(self, center, size, mat, bone, rot=None, smooth=False):
        def add(bm, dl, bones):
            M = Matrix.Translation(Vector(center)) @ (rot.to_matrix().to_4x4() if rot else Matrix()) @ Matrix.Diagonal((*size, 1))
            ret = bmesh.ops.create_cube(bm, size=1.0, matrix=M)
            return ret["verts"], self.mat_index[mat], bone, smooth
        self.parts.append(add)

    def ring(self, center, R, r, mat, bone, seg=24, side=6, sy=1.0, arc=360, rot=None):
        def add(bm, dl, bones):
            c = Vector(center)
            Rm = rot.to_matrix() if rot else Matrix.Identity(3)
            grid = []
            closed = arc >= 360
            n = seg if closed else seg + 1
            for i in range(n):
                a = math.radians(arc) * i / seg - (0 if closed else math.radians(arc) / 2) + (0 if closed else -math.pi / 2)
                row = []
                for j in range(side):
                    b = 2 * math.pi * j / side
                    rr = R + r * math.cos(b)
                    local = Vector((rr * math.cos(a), rr * math.sin(a) * sy, r * math.sin(b)))
                    row.append(bm.verts.new(c + Rm @ local))
                grid.append(row)
            for i in range(seg if closed else seg):
                for j in range(side):
                    i2 = (i + 1) % n
                    bm.faces.new((grid[i][j], grid[i2][j], grid[i2][(j + 1) % side], grid[i][(j + 1) % side]))
            return [v for row in grid for v in row], self.mat_index[mat], bone, True
        self.parts.append(add)

    # build ----------------------------------------------------------
    def build(self):
        self._flat = []
        bones = list(self.order)
        segs = {b: self.bone_of(b) for b in bones}

        # 1. hull. Thick parts: a metaball blob per joint and a capsule along every linked bone, melted
        #    together (one blob per separate piece), turned into a mesh and decimated to a game-friendly
        #    size. Thin parts (legs, fingers, tails below `tube` radius): tapered tubes with round joints.
        K = 1.5            # metaball radius -> visible surface radius (stiffness 3.5, threshold 0.6)
        island_of, balls, tubes, joints = {}, {}, [], []
        for name in self.order:
            n = self.nodes[name]
            if not n["skin"]:
                continue
            par = n["parent"]
            linked = n["link"] and par in self.nodes and self.nodes[par]["skin"]
            island_of[name] = island_of.get(par, name) if linked else name
            rx, ry = n["r"]
            big = max(rx, ry)
            if big >= self.tube:
                balls.setdefault(island_of[name], []).append(("ball", n["pos"], (rx, ry, n.get("rz") or big)))
            else:
                joints.append((name, n["pos"], big))
            if linked:
                a0, a1 = self.nodes[par]["pos"], n["pos"]
                r0, r1 = min(self.nodes[par]["r"]), min(n["r"])
                if min(r0, r1) >= self.tube:
                    balls.setdefault(island_of[name], []).append(("cap", a0, a1, min(r0, r1) * self.limb))
                else:
                    tubes.append((a0, a1, min(r0, self.tube * 0.9) if r0 >= self.tube else r0, r1))
        bm = bmesh.new()
        total = 0
        pieces = []
        for k, (isl, elems) in enumerate(balls.items()):
            mb = bpy.data.metaballs.new("%s_blob%d" % (self.name, k))
            mb.resolution = mb.render_resolution = self.res
            mb.threshold = 0.6
            mobj = bpy.data.objects.new("%s_blob%d" % (self.name, k), mb)
            bpy.context.scene.collection.objects.link(mobj)
            for el in elems:
                if el[0] == "ball":
                    _, pos, (rx, ry, rz) = el
                    big = max(rx, ry, rz)
                    e = mb.elements.new(type="ELLIPSOID")
                    e.co = pos
                    e.radius = big * K
                    e.size_x, e.size_y, e.size_z = rx / big, ry / big, rz / big
                else:
                    _, a0, a1, rr = el
                    d = a1 - a0
                    if d.length < 0.01:
                        continue
                    e = mb.elements.new(type="CAPSULE")
                    e.co = (a0 + a1) / 2
                    e.radius = rr * K
                    e.size_x = d.length / 2
                    e.rotation = Vector((1, 0, 0)).rotation_difference(d.normalized())
                e.stiffness = 3.5
            bpy.context.view_layer.update()
            deps = bpy.context.evaluated_depsgraph_get()
            blob = bpy.data.meshes.new_from_object(mobj.evaluated_get(deps), depsgraph=deps)
            bpy.data.objects.remove(mobj, do_unlink=True)
            bpy.data.metaballs.remove(mb)
            pieces.append(blob)
            total += len(blob.polygons)
        for blob in pieces:
            tmp = bpy.data.objects.new(self.name + "_tmp", blob)
            bpy.context.scene.collection.objects.link(tmp)
            if total > self.budget:
                dec = tmp.modifiers.new("Decimate", "DECIMATE")
                dec.ratio = self.budget / total
            bpy.context.view_layer.update()
            deps = bpy.context.evaluated_depsgraph_get()
            hull = bpy.data.meshes.new_from_object(tmp.evaluated_get(deps), depsgraph=deps)
            bpy.data.objects.remove(tmp, do_unlink=True)
            bpy.data.meshes.remove(blob)
            bm.from_mesh(hull)
            bpy.data.meshes.remove(hull)
        for a0, a1, r0, r1 in tubes:
            d = a1 - a0
            rot = Vector((0, 0, 1)).rotation_difference(d.normalized())
            M = Matrix.Translation((a0 + a1) / 2) @ rot.to_matrix().to_4x4()
            bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=10, radius1=r0, radius2=r1, depth=d.length, matrix=M)
        for name, pos, r in joints:
            near = [min(self.nodes[o]["r"]) for o in self.order if self.nodes[o]["parent"] == name and self.nodes[o]["skin"]]
            near.append(min(self.nodes[name]["r"]))
            r = min(r, max(near) * 1.05)
            bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=r, matrix=Matrix.Translation(pos))
        dl = bm.verts.layers.deform.verify()
        bm.verts.ensure_lookup_table()

        # 2. weights: each hull piece binds to the bones of its own island, two nearest bones blended
        island_bones = {}
        for b in bones:
            if self.nodes[b]["deform"] and self.nodes[b]["skin"]:
                island_bones.setdefault(island_of.get(b, b), []).append(b)
        all_deform = [b for b in bones if self.nodes[b]["deform"]]

        def seg_dist(p, a, b):
            ab = b - a
            L = ab.length_squared
            t = 0.0 if L < 1e-9 else max(0.0, min(1.0, (p - a).dot(ab) / L))
            return (a + ab * t - p).length

        seen = set()
        for start in bm.verts:
            if start.index in seen:
                continue
            comp, stack = [], [start]
            seen.add(start.index)
            while stack:
                v = stack.pop()
                comp.append(v)
                for e in v.link_edges:
                    o = e.other_vert(v)
                    if o.index not in seen:
                        seen.add(o.index)
                        stack.append(o)
            cands = [b for b in all_deform if self.nodes[b]["skin"]]
            for v in comp:
                ds = sorted((seg_dist(v.co, *segs[b]), b) for b in cands)
                d1, b1 = ds[0]
                if len(ds) > 1 and ds[1][0] < d1 * 1.8 + 0.02:
                    d2, b2 = ds[1]
                    w1, w2 = 1 / (d1 + 0.01) ** 3, 1 / (d2 + 0.01) ** 3
                    v[dl][bones.index(b1)] = w1 / (w1 + w2)
                    v[dl][bones.index(b2)] = w2 / (w1 + w2)
                else:
                    v[dl][bones.index(b1)] = 1.0
        for f in bm.faces:
            f.smooth = True
            wsum = {}
            for v in f.verts:
                for gi, w in v[dl].items():
                    wsum[gi] = wsum.get(gi, 0) + w
            bone = bones[max(wsum, key=wsum.get)] if wsum else bones[0]
            f.material_index = self.face_mat(f.calc_center_median(), f.normal, bone)

        # 3. rigid pieces
        for add in self.parts:
            verts, mi, bone, smooth = add(bm, dl, bones)
            vs = set(verts)
            for v in vs:
                v[dl][bones.index(bone)] = 1.0
            for f in {f for v in vs for f in v.link_faces}:
                f.material_index = mi
                f.smooth = smooth
        flat = {f for v in self._flat for f in v.link_faces}
        bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if f not in flat])
        me = bpy.data.meshes.new(self.name)
        bm.to_mesh(me)
        bm.free()
        for mt in self.mats:
            me.materials.append(mt)
        obj = bpy.data.objects.new(self.name, me)
        bpy.context.scene.collection.objects.link(obj)
        for b in bones:
            obj.vertex_groups.new(name=b)

        # 4. armature
        data = bpy.data.armatures.new(self.name + "_Armature")
        arm = bpy.data.objects.new("Armature", data)
        bpy.context.scene.collection.objects.link(arm)
        bpy.context.view_layer.objects.active = arm
        for o in bpy.context.view_layer.objects:
            o.select_set(o == arm)
        with bpy.context.temp_override(**ctx(), active_object=arm, object=arm):
            bpy.ops.object.mode_set(mode="EDIT")
            for b in bones:
                h, t = segs[b]
                if (t - h).length < 0.02:
                    t = h + Vector((0, 0, 0.05))
                eb = data.edit_bones.new(b)
                eb.head, eb.tail, eb.roll = h, t, 0.0
            for b in bones:
                p = self.nodes[b]["parent"]
                if p:
                    data.edit_bones[b].parent = data.edit_bones[p]
            origin = data.edit_bones.new("Origin")
            origin.head, origin.tail = Vector((0, 0, 0)), Vector((0, 0, 0.05))
            for eb in data.edit_bones:
                if eb is not origin and eb.parent is None:
                    eb.parent = origin
            bpy.ops.object.mode_set(mode="OBJECT")
        obj.parent = arm
        mod = obj.modifiers.new("Armature", "ARMATURE")
        mod.object = arm
        return arm, obj

# ------------------------------------------------------------------ animation

def make_action(arm, name, length, keys, loop):
    """keys: {frame: {bone: dict(r=Quaternion in armature axes, t=offset in armature axes)}}"""
    act = bpy.data.actions.new(name)
    act.use_fake_user = True
    arm.animation_data_create()
    arm.animation_data.action = act
    frames = sorted(set(keys) | {0, length})
    for f in frames:
        if f in keys:
            k = keys[f]
        elif loop and f == length:
            k = keys[0]
        else:
            k = keys[max(x for x in keys if x <= f)]
        for pb in arm.pose.bones:
            pb.rotation_mode = "QUATERNION"
            rest = pb.bone.matrix_local.to_quaternion()
            e = k.get(pb.name, {})
            pb.rotation_quaternion = (rest.inverted() @ e["r"] @ rest) if "r" in e else Quaternion()
            pb.location = (rest.inverted() @ Vector(e["t"])) if "t" in e else Vector()
            pb.scale = (1, 1, 1)
            for path in ("rotation_quaternion", "location", "scale"):
                pb.keyframe_insert(path, frame=f, group=pb.name)
    act.frame_range = (0, length)
    return act

def merge(*poses):
    out = {}
    for p in poses:
        for b, e in p.items():
            cur = out.setdefault(b, {})
            if "r" in e:
                cur["r"] = e["r"] @ cur.get("r", Quaternion())
            if "t" in e:
                cur["t"] = tuple(Vector(cur.get("t", (0, 0, 0))) + Vector(e["t"]))
    return out

def R(**bones):
    """R(ArmL=Q((X, -30)), ...) -> pose dict."""
    return {b: {"r": r} for b, r in bones.items()}

# humanoid ------------------------------------------------------------

def humanoid_actions(hip_h, weapon_hand="R", heavy=False, caster=False):
    s = 0.8 if heavy else 1.0
    def run(phase):
        c, sn = math.cos(phase), math.sin(phase)
        lean = 12 if not caster else 8
        return merge(
            {"Hips": {"t": (0, 0, 0.04 * abs(sn) * hip_h)}},
            R(Spine=Q((X, lean)), Head=Q((X, -lean * 0.6)),
              ThighL=Q((X, -38 * c * s)), ThighR=Q((X, 38 * c * s)),
              ShinL=Q((X, 50 * max(0, sn) * s + 8)), ShinR=Q((X, 50 * max(0, -sn) * s + 8)),
              ArmL=Q((X, 32 * c * s), (Y, -8)), ArmR=Q((X, -32 * c * s), (Y, 8)),
              ForearmL=Q((X, -35)), ForearmR=Q((X, -35))))
    acts = {}
    acts["Idle"] = (60, {0: R(Spine=Q((X, 2)), ArmL=Q((Y, -6)), ArmR=Q((Y, 6)), ForearmL=Q((X, -12)), ForearmR=Q((X, -12))),
                         30: merge(R(Spine=Q((X, -1)), Head=Q((X, 4)), ArmL=Q((Y, -9)), ArmR=Q((Y, 9)), ForearmL=Q((X, -16)), ForearmR=Q((X, -16))),
                                   {"Hips": {"t": (0, 0, -0.012 * hip_h)}})}, True)
    acts["Run"] = (20, {f: run(2 * math.pi * f / 20) for f in (0, 5, 10, 15)}, True)
    wa, wf = "Arm" + weapon_hand, "Forearm" + weapon_hand
    if caster:
        acts["Attack01"] = (26, {0: R(),
                                 8: R(Spine=Q((X, -8)), **{wa: Q((X, -120)), wf: Q((X, -20))}, Head=Q((X, -6))),
                                 15: R(Spine=Q((X, 14)), **{wa: Q((X, -75)), wf: Q((X, -5))}, ArmL=Q((X, -60))),
                                 26: R()}, False)
    else:
        acts["Attack01"] = (24, {0: R(),
                                 8: R(Spine=Q((X, -10), (Z, 25)), **{wa: Q((X, -160), (Y, 10 if weapon_hand == "R" else -10)), wf: Q((X, -30))}),
                                 13: R(Spine=Q((X, 18), (Z, -15)), **{wa: Q((X, -40)), wf: Q((X, -10))}, ThighL=Q((X, -20)), ShinL=Q((X, 15))),
                                 24: R()}, False)
    acts["GetHit"] = (12, {0: R(), 3: merge(R(Spine=Q((X, -16)), Head=Q((X, -12)), ArmL=Q((X, 15)), ArmR=Q((X, 15))),
                                          {"Hips": {"t": (0, 0.05 * hip_h, 0)}}), 12: R()}, False)
    lie = {"Hips": {"r": Q((X, -86)), "t": (0, 0.1 * hip_h, -hip_h + 0.18 * hip_h)}}
    acts["Die"] = (40, {0: R(),
                        8: merge(R(Spine=Q((X, -20)), Head=Q((X, -20)), ShinL=Q((X, 40)), ShinR=Q((X, 30))), {"Hips": {"t": (0, 0.05, -0.15 * hip_h)}}),
                        22: merge(lie, R(ArmL=Q((Y, -50)), ArmR=Q((Y, 50)), ThighL=Q((X, -10)), ThighR=Q((X, 10)))),
                        40: merge(lie, R(ArmL=Q((Y, -55)), ArmR=Q((Y, 55)), ThighL=Q((X, -10)), ThighR=Q((X, 10))))}, False)
    return acts

def humanoid_skeleton(c, hip, chest, neck, head_top, shoulder_x, arm_len, leg_x, radii, belly=None):
    """Joint graph for a biped. radii: dict of thicknesses (w, d) or (w, d, h); head_top is the head centre."""
    r = radii
    c.node("Hips", (0, 0, hip), r["hips"])
    c.node("Spine", (0, -0.02, hip + (chest - hip) * 0.55), belly or r["spine"], "Hips")
    c.node("Chest", (0, 0, chest), r["chest"], "Spine")
    c.node("Neck", (0, -0.02, neck), r["neck"], "Chest")
    c.node("Head", (0, -0.03, head_top), r["head"], "Neck")
    sh = chest - 0.02
    c.mirror("Clav", (shoulder_x, 0, sh), r["shoulder"], "Chest")
    c.mirror("Arm", (shoulder_x + 0.06, 0.0, sh - arm_len * 0.5), r["arm"], "Clav{s}")
    c.mirror("Forearm", (shoulder_x + 0.09, -0.04, sh - arm_len), r["forearm"], "Arm{s}")
    c.mirror("Hand", (shoulder_x + 0.1, -0.06, sh - arm_len - r["hand"][0] * 1.6), r["hand"], "Forearm{s}")
    c.mirror("Hip", (leg_x, 0, hip - 0.04), r["thigh"], "Hips")
    c.mirror("Thigh", (leg_x + 0.01, -0.01, hip * 0.52), r["knee"], "Hip{s}")
    c.mirror("Shin", (leg_x + 0.01, 0.02, 0.09), r["ankle"], "Thigh{s}")
    c.mirror("Foot", (leg_x + 0.01, -0.13, 0.05), r["foot"], "Shin{s}")

# quadruped -------------------------------------------------------------

def quad_actions(body_h, tail=("Tail1", "Tail2", "Tail3"), wings=False):
    def walk(phase):
        c, sn = math.cos(phase), math.sin(phase)
        pose = R(FLUpperL=Q((X, -30 * c)), BLUpperR=Q((X, -30 * c)), FLUpperR=Q((X, 30 * c)), BLUpperL=Q((X, 30 * c)),
                 FLLowerL=Q((X, -30 * max(0, sn))), BLLowerR=Q((X, 35 * max(0, sn))),
                 FLLowerR=Q((X, -30 * max(0, -sn))), BLLowerL=Q((X, 35 * max(0, -sn))),
                 Neck=Q((X, 4 * math.sin(phase * 2))), Head=Q((X, -4 * math.sin(phase * 2))),
                 **{t: Q((Z, 12 * math.sin(phase + i * 0.8))) for i, t in enumerate(tail)})
        if wings:
            pose.update(R(WingL=Q((Y, -25 * math.sin(phase * 2))), WingR=Q((Y, 25 * math.sin(phase * 2)))))
        return merge(pose, {"Hips": {"t": (0, 0, 0.05 * body_h * abs(math.sin(phase)))}})
    acts = {}
    idle_tail = lambda a: {t: Q((Z, a * (i + 1))) for i, t in enumerate(tail)}
    i0 = R(Neck=Q((X, 2)), **idle_tail(4))
    i1 = R(Neck=Q((X, -3)), Head=Q((X, 4)), **idle_tail(-4))
    if wings:
        i0.update(R(WingL=Q((Y, -10)), WingR=Q((Y, 10))))
        i1.update(R(WingL=Q((Y, 15)), WingR=Q((Y, -15))))
    acts["Idle"] = (50, {0: i0, 25: merge(i1, {"Chest": {"t": (0, 0, 0.015 * body_h)}})}, True)
    acts["Run"] = (18, {f: walk(2 * math.pi * f / 18) for f in (0, 4, 9, 13)}, True)
    acts["Attack01"] = (22, {0: R(),
                             7: merge(R(Chest=Q((X, -14)), Neck=Q((X, -25)), Head=Q((X, -20)), FLUpperL=Q((X, -25)), FLUpperR=Q((X, -25))),
                                      {"Hips": {"t": (0, 0.08 * body_h, 0)}}),
                             12: merge(R(Chest=Q((X, 10)), Neck=Q((X, 30)), Head=Q((X, 12)), Jaw=Q((X, 35))),
                                       {"Hips": {"t": (0, -0.25 * body_h, 0)}}),
                             22: R()}, False)
    acts["GetHit"] = (12, {0: R(), 3: merge(R(Neck=Q((X, -20)), Head=Q((X, -10))), {"Hips": {"t": (0, 0.1 * body_h, 0)}}), 12: R()}, False)
    side = merge(R(Hips=Q((Y, 82)), FLUpperL=Q((X, -20)), BLUpperL=Q((X, 20)), Neck=Q((X, 25)), **idle_tail(8)),
                 {"Hips": {"t": (0, 0, -body_h * 0.45)}})
    acts["Die"] = (36, {0: R(), 8: merge(R(Neck=Q((X, -25))), {"Hips": {"t": (0, 0, -0.1 * body_h)}}), 20: side, 36: side}, False)
    return acts

def quad_skeleton(c, hip, chest, length, neck_up, head_fwd, leg_x, radii, tail_len):
    r = radii
    y0 = length / 2           # hips at +Y (back), chest at -Y (front)
    c.node("Hips", (0, y0, hip), r["hips"])
    c.node("Spine", (0, 0, (hip + chest) / 2 + 0.03), r["spine"], "Hips")
    c.node("Chest", (0, -y0, chest), r["chest"], "Spine")
    c.node("Neck", (0, -y0 - 0.18, chest + neck_up), r["neck"], "Chest")
    c.node("Head", (0, -y0 - 0.18 - head_fwd * 0.45, chest + neck_up + 0.05), r["head"], "Neck")
    c.node("Snout", (0, -y0 - 0.18 - head_fwd, chest + neck_up - 0.02), r["snout"], "Head")
    c.node("Jaw", (0, -y0 - 0.18 - head_fwd * 0.9, chest + neck_up - 0.09), r["jaw"], "Head")
    c.node("Tail1", (0, y0 + tail_len * 0.35, hip + 0.03), r["tail1"], "Hips")
    c.node("Tail2", (0, y0 + tail_len * 0.7, hip - 0.05), r["tail2"], "Tail1")
    c.node("Tail3", (0, y0 + tail_len, hip - 0.15), r["tail3"], "Tail2")
    for leg, yy, top in (("FL", -y0 + 0.02, chest), ("BL", y0 - 0.02, hip)):
        c.mirror(leg + "Upper", (leg_x, yy - 0.02, top * 0.5), r["upper"], "Chest" if leg == "FL" else "Hips")
        c.mirror(leg + "Lower", (leg_x, yy + (0.04 if leg == "BL" else -0.01), 0.06), r["lower"], leg + "Upper{s}")
        c.mirror(leg + "Paw", (leg_x, yy - 0.09, 0.04), r["paw"], leg + "Lower{s}")
        for s_, sx in (("L", 1), ("R", -1)):
            c.sphere((leg_x * sx, yy - 0.07, 0.035), (r["paw"][0] * 1.1, r["paw"][0] * 1.6, 0.035), c.paw_mat, leg + "Paw" + s_)

# ------------------------------------------------------------------ the monsters

def spider():
    c = Creature("Spider")
    body = c.mat("Body", srgb(58, 40, 52), rough=0.55)
    mark = c.mat("Mark", srgb(196, 72, 38), rough=0.5)
    leg = c.mat("Leg", srgb(40, 28, 36), rough=0.5)
    eye = c.mat("Eye", srgb(255, 40, 30), emit=srgb(255, 40, 30), strength=4)
    fang = c.mat("Fang", srgb(230, 220, 200), rough=0.4)
    c.node("Body", (0, 0, 0.5), (0.3, 0.34))
    c.node("Head", (0, -0.36, 0.48), (0.2, 0.2), "Body")
    c.node("Abdomen", (0, 0.5, 0.64), (0.42, 0.46), "Body", link=False, rz=0.4)
    c.node("Tip", (0, 0.88, 0.6), (0.2, 0.22), "Abdomen")
    angles = (-55, -20, 15, 50)   # leg directions, front to back (degrees from +X toward +Y)
    for i, a in enumerate(angles, 1):
        for s, sx in (("L", 1), ("R", -1)):
            d = Vector((math.cos(math.radians(a)) * sx, math.sin(math.radians(a)), 0))
            base = Vector((0, 0.02, 0.5)) + d * 0.26
            knee = Vector((0, 0.02, 0)) + d * 0.72 + Vector((0, 0, 0.78))
            foot = Vector((0, 0.02, 0)) + d * 1.18 + Vector((0, 0, 0.02))
            c.node("Leg%d%sRoot" % (i, s), tuple(base), 0.07, "Body", link=False, deform=False)
            c.node("Leg%d%sFemur" % (i, s), tuple(knee), 0.055, "Leg%d%sRoot" % (i, s))
            c.node("Leg%d%sTibia" % (i, s), tuple(foot), 0.02, "Leg%d%sFemur" % (i, s))
    def spider_mat(ctr, n, bone):
        if "Leg" in bone:
            return leg
        if bone in ("Abdomen", "Tip") and n.z > 0.15 and (int((ctr.y - 0.18) / 0.13) % 2 == 0 or abs(ctr.x) < 0.07):
            return mark
        return body
    c.face_mat = spider_mat
    for ex, ez, rr in ((0.075, 0.6, 0.04), (-0.075, 0.6, 0.04), (0.15, 0.56, 0.028), (-0.15, 0.56, 0.028)):
        c.sphere((ex, -0.585, ez), (rr, rr, rr), "Eye", "Head")
    for s in (1, -1):
        c.horn((0.07 * s, -0.6, 0.42), (0.05 * s, -0.3, -1), 0.22, 0.04, (-12, 0), "Fang", "Head", seg=4)

    def walk(phase):
        pose = {}
        for i in range(1, 5):
            for s, sgn in (("L", 1), ("R", -1)):
                grp = (i + (0 if s == "L" else 1)) % 2
                ph = phase + (math.pi if grp else 0)
                lift = max(0, math.sin(ph)) * 22
                pose["Leg%d%sFemur" % (i, s)] = {"r": Q((Z, 18 * math.cos(ph) * sgn), (Y, lift * sgn))}
        return merge(pose, {"Body": {"t": (0, 0, 0.03 * abs(math.sin(phase)))}})
    acts = {}
    acts["Idle"] = (40, {0: R(Abdomen=Q((X, 3))), 20: merge(R(Abdomen=Q((X, -4)), Head=Q((X, 3))), {"Body": {"t": (0, 0, -0.025)}})}, True)
    acts["Run"] = (16, {f: walk(2 * math.pi * f / 16) for f in (0, 4, 8, 12)}, True)
    rear = merge(R(Body=Q((X, -22)), Leg1LFemur=Q((Y, 40), (Z, 20)), Leg1RFemur=Q((Y, -40), (Z, -20)),
                   Leg2LFemur=Q((Y, 25)), Leg2RFemur=Q((Y, -25))), {"Body": {"t": (0, 0.1, 0.12)}})
    strike = merge(R(Body=Q((X, 12)), Head=Q((X, 18)), Leg1LFemur=Q((Y, -10), (Z, -15)), Leg1RFemur=Q((Y, 10), (Z, 15))), {"Body": {"t": (0, -0.28, -0.04)}})
    acts["Attack01"] = (20, {0: R(), 7: rear, 11: strike, 20: R()}, False)
    acts["GetHit"] = (12, {0: R(), 3: merge(R(Body=Q((X, -10))), {"Body": {"t": (0, 0.12, 0.04)}}), 12: R()}, False)
    curl = {}
    for i in range(1, 5):
        for s, sgn in (("L", 1), ("R", -1)):
            curl["Leg%d%sFemur" % (i, s)] = {"r": Q((Y, 70 * sgn))}
            curl["Leg%d%sTibia" % (i, s)] = {"r": Q((Y, 60 * sgn))}
    dead = merge(curl, R(Body=Q((Y, 170))), {"Body": {"t": (0, 0, 0.05)}})
    acts["Die"] = (34, {0: R(), 8: merge(R(Body=Q((X, -15))), {"Body": {"t": (0, 0, 0.25)}}), 20: dead, 34: dead}, False)
    return c, acts, (0, 0.1, 0.45), 3.2

def baby_dragon():
    c = Creature("BabyDragon")
    skin = c.mat("Skin", srgb(64, 150, 96), rough=0.55)
    belly = c.mat("Belly", srgb(236, 214, 150), rough=0.6)
    horn = c.mat("Horn", srgb(245, 236, 210), rough=0.4)
    wing = c.mat("Wing", srgb(196, 92, 60), rough=0.6)
    eye = c.mat("Eye", srgb(255, 210, 60), emit=srgb(255, 200, 40), strength=3)
    claw = c.mat("Claw", srgb(40, 40, 40), rough=0.4)
    c.paw_mat = "Skin"
    quad_skeleton(c, hip=0.5, chest=0.55, length=0.6, neck_up=0.28, head_fwd=0.42, leg_x=0.2,
                  radii=dict(hips=(0.27, 0.27), spine=(0.3, 0.31), chest=(0.27, 0.27), neck=(0.13, 0.13), head=(0.2, 0.2),
                             snout=(0.11, 0.1), jaw=(0.08, 0.06), tail1=(0.15, 0.15), tail2=(0.09, 0.09), tail3=(0.04, 0.04),
                             upper=(0.1, 0.1), lower=(0.07, 0.07), paw=(0.08, 0.06)), tail_len=0.8)
    c.node("WingL", (0.62, 0.05, 1.05), 0.03, "Chest", skin=False)
    c.node("WingR", (-0.62, 0.05, 1.05), 0.03, "Chest", skin=False)
    c.face_mat = lambda ctr, n, bone: belly if (bone in ("Hips", "Spine", "Chest", "Neck") and n.z < -0.25) or (bone == "Jaw" and n.z < 0) else skin
    head = c.nodes["Head"]["pos"]
    for s in (1, -1):
        c.horn((0.09 * s, head.y + 0.08, head.z + 0.13), (0.3 * s, 0.6, 0.7), 0.26, 0.045, (12, 0), "Horn", "Head", seg=5)
        c.sphere((0.1 * s, head.y - 0.13, head.z + 0.04), (0.05, 0.035, 0.055), "Eye", "Head")
        # wing: a bony leading edge and a scalloped membrane, both sides
        wb = "WingL" if s > 0 else "WingR"
        root = Vector((0.16 * s, -0.25, 0.78))
        tip = Vector((0.66 * s, 0.05, 1.08))
        elbow = root.lerp(tip, 0.45) + Vector((0, -0.06, 0.08))
        c.cone(tuple(root), tuple(elbow), 0.035, 0.025, "Horn", wb, seg=6)
        c.cone(tuple(elbow), tuple(tip), 0.025, 0.01, "Horn", wb, seg=6)
        rim = [Vector((0.58 * s, 0.32, 0.86)), Vector((0.45 * s, 0.34, 0.8)), Vector((0.34 * s, 0.26, 0.74)), Vector((0.2 * s, 0.12, 0.7))]
        c.membrane([root, elbow, tip] + rim, "Wing", wb)
    for p in ("Hips", "Spine", "Chest"):   # back ridge
        pos = c.nodes[p]["pos"]
        c.cone((0, pos.y, pos.z + 0.24), (0, pos.y + 0.05, pos.z + 0.4), 0.06, 0.005, "Horn", p, seg=6)
    t3 = c.nodes["Tail3"]["pos"]
    c.cone((0, t3.y - 0.02, t3.z), (0, t3.y + 0.2, t3.z - 0.05), 0.1, 0.005, "Wing", "Tail3", seg=4)
    for s in (1, -1):
        for leg in ("FLPaw", "BLPaw"):
            n = leg + ("L" if s > 0 else "R")
            p = c.nodes[n]["pos"]
            for k in (-1, 0, 1):
                c.cone((p.x + k * 0.04, p.y - 0.03, 0.03), (p.x + k * 0.05, p.y - 0.12, 0.01), 0.02, 0.003, "Claw", n, seg=5)
    return c, quad_actions(0.55, wings=True), (0, 0.1, 0.55), 3.0

def hound():
    c = Creature("Hound")
    fur = c.mat("Fur", srgb(52, 48, 56), rough=0.8)
    belly = c.mat("Belly", srgb(120, 96, 88), rough=0.8)
    spike = c.mat("Spike", srgb(200, 60, 40), rough=0.4)
    eye = c.mat("Eye", srgb(255, 120, 30), emit=srgb(255, 110, 20), strength=4)
    tooth = c.mat("Tooth", srgb(235, 228, 210), rough=0.4)
    c.paw_mat = "Fur"
    c.limb = 1.0
    quad_skeleton(c, hip=0.62, chest=0.7, length=0.85, neck_up=0.18, head_fwd=0.46, leg_x=0.15,
                  radii=dict(hips=(0.17, 0.2), spine=(0.17, 0.2), chest=(0.22, 0.26), neck=(0.13, 0.15), head=(0.15, 0.15),
                             snout=(0.07, 0.07), jaw=(0.06, 0.04), tail1=(0.07, 0.07), tail2=(0.05, 0.05), tail3=(0.02, 0.02),
                             upper=(0.08, 0.09), lower=(0.045, 0.05), paw=(0.055, 0.045)), tail_len=0.6)
    c.face_mat = lambda ctr, n, bone: belly if bone in ("Hips", "Spine", "Chest", "Neck") and n.z < -0.4 else fur
    head = c.nodes["Head"]["pos"]
    snout = c.nodes["Snout"]["pos"]
    for s in (1, -1):
        c.cone((0.08 * s, head.y + 0.04, head.z + 0.08), (0.13 * s, head.y + 0.1, head.z + 0.3), 0.06, 0.005, "Fur", "Head", seg=6)
        c.sphere((0.08 * s, head.y - 0.1, head.z + 0.04), (0.035, 0.025, 0.03), "Eye", "Head")
        c.cone((0.035 * s, snout.y + 0.02, snout.z - 0.04), (0.035 * s, snout.y + 0.01, snout.z - 0.12), 0.018, 0.002, "Tooth", "Head", seg=5)
    for k, p in enumerate(("Hips", "Spine", "Spine", "Chest", "Chest")):
        pos = c.nodes["Hips"]["pos"].lerp(c.nodes["Chest"]["pos"], k / 4)
        c.cone((0, pos.y, pos.z + 0.17), (0, pos.y + 0.08, pos.z + 0.38 - abs(k - 3) * 0.04), 0.05, 0.004, "Spike", p, seg=6)
    return c, quad_actions(0.7), (0, 0.1, 0.6), 3.0

def bull_demon():
    c = Creature("BullDemon")
    fur = c.mat("Fur", srgb(122, 74, 46), rough=0.85)
    dark = c.mat("Dark", srgb(58, 36, 28), rough=0.8)
    horn = c.mat("Horn", srgb(236, 226, 200), rough=0.4)
    metal = c.mat("Metal", srgb(160, 162, 170), rough=0.3, metal=0.8)
    wood = c.mat("Wood", srgb(90, 58, 34), rough=0.7)
    eye = c.mat("Eye", srgb(255, 60, 30), emit=srgb(255, 50, 20), strength=4)
    belt = c.mat("Belt", srgb(70, 40, 30), rough=0.6)
    humanoid_skeleton(c, hip=0.95, chest=1.5, neck=1.7, head_top=1.88, shoulder_x=0.36, arm_len=0.72, leg_x=0.15,
                      radii=dict(hips=(0.24, 0.19, 0.18), spine=(0.27, 0.22, 0.22), chest=(0.34, 0.25, 0.22), neck=(0.13, 0.13), head=(0.15, 0.16, 0.14),
                                 shoulder=(0.14, 0.14), arm=(0.11, 0.11), forearm=(0.1, 0.1), hand=(0.08, 0.07),
                                 thigh=(0.13, 0.13), knee=(0.1, 0.1), ankle=(0.075, 0.075), foot=(0.085, 0.06)))
    c.node("Muzzle", (0, -0.2, 1.82), (0.1, 0.1, 0.085), "Head")
    c.face_mat = lambda ctr, n, bone: dark if bone.startswith(("Shin", "Foot", "Hand")) or (bone in ("Head", "Muzzle") and ctr.y < -0.2) else fur
    h = c.nodes["Head"]["pos"]
    for s in (1, -1):
        c.horn((0.12 * s, h.y + 0.02, h.z + 0.06), (1 * s, 0.05, 0.3), 0.42, 0.055, (0, 24 * s), "Horn", "Head", seg=6)
        c.sphere((0.08 * s, h.y - 0.13, h.z + 0.03), (0.028, 0.02, 0.022), "Eye", "Head")
        c.sphere((0.17 * s, h.y + 0.02, h.z - 0.02), (0.05, 0.03, 0.035), "Fur", "Head")   # ears
        f = c.nodes["Foot" + ("L" if s > 0 else "R")]["pos"]
        c.sphere((f.x, f.y + 0.01, 0.05), (0.09, 0.12, 0.05), "Dark", "Foot" + ("L" if s > 0 else "R"))
    c.ring((0, -0.29, 1.79), 0.035, 0.01, "Metal", "Head", seg=16, side=6, rot=q(X, 90))   # nose ring
    c.ring((0, 0, 0.98), 0.25, 0.04, "Belt", "Hips", seg=24, sy=0.85)
    c.cone((0, -0.02, 0.97), (0, -0.04, 0.6), 0.2, 0.25, "Belt", "Hips", seg=12)     # loincloth
    hand = c.nodes["HandR"]["pos"]
    c.cone((hand.x, hand.y + 0.02, hand.z - 0.3), (hand.x, hand.y - 0.02, hand.z + 0.8), 0.03, 0.03, "Wood", "HandR", seg=8)
    for s in (1, -1):
        c.box((hand.x + 0.16 * s, hand.y, hand.z + 0.62), (0.26, 0.035, 0.3), "Metal", "HandR", rot=q(Y, 14 * s))
    return c, humanoid_actions(0.95, heavy=True), (0, 0, 1.0), 4.4

def dark_mage():
    c = Creature("DarkMage")
    robe = c.mat("Robe", srgb(52, 30, 82), rough=0.8)
    trim = c.mat("Trim", srgb(212, 170, 72), rough=0.35, metal=0.6)
    skin = c.mat("Skin", srgb(110, 120, 140), rough=0.6)
    shadow = c.mat("Shadow", srgb(14, 10, 20), rough=1.0)
    eye = c.mat("Eye", srgb(140, 255, 220), emit=srgb(100, 255, 210), strength=5)
    wood = c.mat("Wood", srgb(60, 40, 30), rough=0.7)
    orb = c.mat("Orb", srgb(170, 90, 255), emit=srgb(170, 80, 255), strength=6)
    humanoid_skeleton(c, hip=0.95, chest=1.42, neck=1.55, head_top=1.74, shoulder_x=0.22, arm_len=0.62, leg_x=0.1,
                      radii=dict(hips=(0.2, 0.17), spine=(0.2, 0.16), chest=(0.24, 0.18), neck=(0.07, 0.07), head=(0.12, 0.13),
                                 shoulder=(0.1, 0.1), arm=(0.085, 0.085), forearm=(0.09, 0.09), hand=(0.05, 0.04),
                                 thigh=(0.09, 0.09), knee=(0.07, 0.07), ankle=(0.05, 0.05), foot=(0.06, 0.04)))
    c.face_mat = lambda ctr, n, bone: skin if bone.startswith("Hand") else robe
    # long robe skirt from the waist down, a hood, a mantle with gold trim
    c.cone((0, 0.0, 0.98), (0, 0.02, 0.04), 0.2, 0.42, "Robe", "Hips", seg=18)
    c.ring((0, 0.02, 0.06), 0.42, 0.025, "Trim", "Hips", seg=28)
    c.ring((0, 0, 1.38), 0.26, 0.06, "Trim", "Chest", seg=24, sy=0.8)
    h = c.nodes["Head"]["pos"]
    c.sphere((0, h.y + 0.02, h.z - 0.02), (0.17, 0.18, 0.2), "Robe", "Head")
    c.cone((0, h.y + 0.1, h.z + 0.12), (0, h.y + 0.32, h.z + 0.3), 0.12, 0.01, "Robe", "Head", seg=10)
    c.sphere((0, h.y - 0.1, h.z - 0.04), (0.11, 0.07, 0.12), "Shadow", "Head")
    for s in (1, -1):
        c.sphere((0.045 * s, h.y - 0.16, h.z - 0.02), (0.022, 0.012, 0.016), "Eye", "Head")
        a = c.nodes["Arm" + ("L" if s > 0 else "R")]["pos"]
        f = c.nodes["Forearm" + ("L" if s > 0 else "R")]["pos"]
        c.cone(tuple(a), (f.x + 0.02 * s, f.y, f.z - 0.05), 0.09, 0.15, "Robe", "Forearm" + ("L" if s > 0 else "R"), seg=10)
    hand = c.nodes["HandR"]["pos"]
    c.cone((hand.x, hand.y, hand.z - 0.7), (hand.x, hand.y - 0.02, hand.z + 0.7), 0.025, 0.03, "Wood", "HandR", seg=8)
    c.ring((hand.x, hand.y - 0.02, hand.z + 0.78), 0.09, 0.015, "Trim", "HandR", seg=16, rot=q(X, 90))
    c.sphere((hand.x, hand.y - 0.02, hand.z + 0.78), (0.08, 0.08, 0.08), "Orb", "HandR")
    return c, humanoid_actions(0.95, caster=True), (0, 0, 0.95), 4.0

def giant():
    c = Creature("Giant")
    skin = c.mat("Skin", srgb(156, 146, 100), rough=0.75)
    cloth = c.mat("Cloth", srgb(110, 70, 40), rough=0.9)
    tusk = c.mat("Tusk", srgb(240, 232, 210), rough=0.4)
    wood = c.mat("Wood", srgb(96, 64, 40), rough=0.8)
    stud = c.mat("Stud", srgb(120, 120, 128), rough=0.3, metal=0.8)
    eye = c.mat("Eye", srgb(255, 230, 120), emit=srgb(255, 220, 90), strength=3)
    hair = c.mat("Hair", srgb(60, 40, 30), rough=0.9)
    humanoid_skeleton(c, hip=0.86, chest=1.55, neck=1.74, head_top=1.9, shoulder_x=0.42, arm_len=0.86, leg_x=0.19,
                      radii=dict(hips=(0.3, 0.24, 0.2), spine=(0.38, 0.36, 0.3), chest=(0.36, 0.27, 0.23), neck=(0.13, 0.13), head=(0.14, 0.15, 0.14),
                                 shoulder=(0.17, 0.17), arm=(0.13, 0.13), forearm=(0.12, 0.12), hand=(0.1, 0.08),
                                 thigh=(0.16, 0.16), knee=(0.12, 0.12), ankle=(0.09, 0.09), foot=(0.1, 0.07)),
                      belly=(0.4, 0.4, 0.32))
    c.node("Brow", (0, -0.11, 1.95), (0.11, 0.05, 0.04), "Head")
    c.face_mat = lambda ctr, n, bone: skin
    c.cone((0, 0.0, 0.92), (0, -0.02, 0.5), 0.33, 0.36, "Cloth", "Hips", seg=16)
    c.ring((0, 0, 0.93), 0.33, 0.04, "Cloth", "Hips", seg=24, sy=0.85)
    h = c.nodes["Head"]["pos"]
    c.cone((0, h.y + 0.02, h.z + 0.12), (0, h.y + 0.1, h.z + 0.26), 0.07, 0.01, "Hair", "Head", seg=8)   # top knot
    for s in (1, -1):
        c.horn((0.06 * s, h.y - 0.16, h.z - 0.08), (0.2 * s, -0.5, 1), 0.14, 0.028, (-20, 0), "Tusk", "Head", seg=3)
        c.sphere((0.055 * s, h.y - 0.17, h.z + 0.03), (0.026, 0.016, 0.02), "Eye", "Head")
        c.sphere((0.15 * s, h.y, h.z), (0.04, 0.05, 0.065), "Skin", "Head")
        f = c.nodes["Foot" + ("L" if s > 0 else "R")]["pos"]
        c.sphere((f.x, f.y + 0.01, 0.05), (0.1, 0.14, 0.05), "Skin", "Foot" + ("L" if s > 0 else "R"))
    hand = c.nodes["HandR"]["pos"]
    c.cone((hand.x, hand.y + 0.05, hand.z - 0.2), (hand.x, hand.y - 0.15, hand.z + 1.0), 0.05, 0.14, "Wood", "HandR", seg=10)
    top = Vector((hand.x, hand.y - 0.13, hand.z + 0.85))
    for k in range(5):
        dirv = Matrix.Rotation(math.radians(k * 72), 3, "Z") @ Vector((1, 0, 0))
        p = top + dirv * 0.12
        c.cone(tuple(p), tuple(p + dirv * 0.1), 0.035, 0.003, "Stud", "HandR", seg=5)
    return c, humanoid_actions(0.86, heavy=True), (0, 0, 1.0), 4.6

def skeleton_soldier():
    c = Creature("Skeleton")
    bone_m = c.mat("Bone", srgb(226, 216, 190), rough=0.6)
    rust = c.mat("Rust", srgb(120, 80, 56), rough=0.5, metal=0.6)
    wood = c.mat("Wood", srgb(96, 62, 40), rough=0.8)
    shadow = c.mat("Shadow", srgb(20, 16, 18), rough=1.0)
    eye = c.mat("Eye", srgb(90, 200, 255), emit=srgb(80, 190, 255), strength=5)
    humanoid_skeleton(c, hip=0.92, chest=1.42, neck=1.56, head_top=1.76, shoulder_x=0.24, arm_len=0.66, leg_x=0.12,
                      radii=dict(hips=(0.14, 0.09), spine=(0.045, 0.045), chest=(0.06, 0.06), neck=(0.035, 0.035), head=(0.06, 0.06),
                                 shoulder=(0.06, 0.06), arm=(0.038, 0.038), forearm=(0.04, 0.04), hand=(0.04, 0.025),
                                 thigh=(0.05, 0.05), knee=(0.045, 0.045), ankle=(0.035, 0.035), foot=(0.05, 0.035)))
    c.face_mat = lambda ctr, n, bone: bone_m
    # ribcage: arcs around the spine; skull and jaw
    for k in range(5):
        z = 1.2 + k * 0.055
        c.ring((0, -0.02, z), 0.16 - abs(k - 2) * 0.012, 0.016, "Bone", "Chest" if k > 1 else "Spine", seg=18, side=5, sy=0.75, arc=290)
    h = c.nodes["Head"]["pos"]
    c.sphere((0, h.y + 0.01, h.z - 0.02), (0.12, 0.13, 0.13), "Bone", "Head")
    c.box((0, h.y - 0.07, h.z - 0.13), (0.15, 0.1, 0.05), "Bone", "Head", smooth=True)
    for s in (1, -1):
        c.sphere((0.045 * s, h.y - 0.1, h.z), (0.035, 0.02, 0.035), "Shadow", "Head")
        c.sphere((0.045 * s, h.y - 0.115, h.z), (0.014, 0.01, 0.014), "Eye", "Head")
    c.ring((0, 0, 0.92), 0.13, 0.025, "Rust", "Hips", seg=20, sy=0.7)
    hand = c.nodes["HandR"]["pos"]
    c.box((hand.x, hand.y - 0.02, hand.z + 0.45), (0.06, 0.015, 0.75), "Rust", "HandR")
    c.box((hand.x, hand.y - 0.02, hand.z + 0.07), (0.2, 0.04, 0.03), "Rust", "HandR")
    hl = c.nodes["HandL"]["pos"]
    c.cone((hl.x + 0.06, hl.y - 0.02, hl.z + 0.12), (hl.x + 0.1, hl.y - 0.02, hl.z + 0.12), 0.24, 0.24, "Wood", "HandL", seg=20, smooth=False)
    c.sphere((hl.x + 0.11, hl.y - 0.02, hl.z + 0.12), (0.03, 0.06, 0.06), "Rust", "HandL")
    c.ring((hl.x + 0.1, hl.y - 0.02, hl.z + 0.12), 0.23, 0.02, "Rust", "HandL", seg=24, rot=q(Y, 90))
    return c, humanoid_actions(0.92), (0, 0, 0.95), 4.0

MONSTERS = [spider, baby_dragon, hound, bull_demon, dark_mage, giant, skeleton_soldier]

# ------------------------------------------------------------------ export and preview

FBX_ARGS = dict(
    use_selection=False, object_types={"ARMATURE", "MESH"},
    apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
    add_leaf_bones=False, use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
    path_mode="AUTO", mesh_smooth_type="FACE")

def preview(path, arm, action, frame, target, dist, obj=None):
    scn = bpy.context.scene
    if obj is not None:
        pts = [obj.matrix_world @ v.co for v in obj.data.vertices]
        lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
        hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
        target = (lo + hi) / 2
        dist = max((hi - lo).length * 1.25, 1.0)
    arm.animation_data.action = bpy.data.actions[action]
    scn.frame_set(frame)
    cam = bpy.data.objects.new("PreviewCam", bpy.data.cameras.new("PreviewCam"))
    scn.collection.objects.link(cam)
    target = Vector(target)
    cam.location = target + Vector((dist * 0.62, -dist * 0.85, dist * 0.32))
    cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
    cam.data.lens = 50
    scn.camera = cam
    scn.render.engine = "BLENDER_WORKBENCH"
    scn.display.shading.light = "STUDIO"
    scn.display.shading.color_type = "MATERIAL"
    scn.display.shading.show_shadows = True
    scn.display.shading.show_cavity = True
    scn.display.shading.show_object_outline = True
    scn.render.resolution_x = scn.render.resolution_y = 480
    scn.render.film_transparent = False
    scn.render.filepath = path
    with bpy.context.temp_override(**ctx()):
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)

def build(fn):
    clear_scene()
    c, acts, target, dist = fn()
    arm, obj = c.build()
    for name, (length, keys, loop) in acts.items():
        make_action(arm, name, length, keys, loop)
    bpy.context.scene.render.fps = FPS
    out_dir = os.path.join(MODELS_DIR, c.name)
    os.makedirs(out_dir, exist_ok=True)
    with bpy.context.temp_override(**ctx()):
        bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, c.name + "_Rigged.fbx"), **FBX_ARGS)
    os.makedirs(PREVIEW_DIR, exist_ok=True)
    preview(os.path.join(PREVIEW_DIR, c.name + ".png"), arm, "Idle", 0, target, dist, obj)
    attack = acts["Attack01"][0]
    preview(os.path.join(PREVIEW_DIR, c.name + "_Attack.png"), arm, "Attack01", int(attack * 0.5), target, dist, obj)
    preview(os.path.join(PREVIEW_DIR, c.name + "_Run.png"), arm, "Run", 4, target, dist, obj)
    print(c.name, "verts", len(obj.data.vertices), "bones", len(arm.data.bones), "actions", list(acts))

def main():
    for fn in MONSTERS:
        if ONLY and fn.__name__ not in ONLY:
            continue
        build(fn)

if __name__ in ("__main__", "__job__"):
    main()
