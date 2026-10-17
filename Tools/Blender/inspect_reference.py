"""Inspect the RPG Monster Duo TurtleShell for reference: render the mesh and list every animation's length."""
import bpy, os, glob, math
from mathutils import Vector

PACK = r"D:\MMORPG\My project\Assets\RPG Monster DUO PBR Polyart"
OUT = r"D:\MMORPG\My project\Assets\Tools\Previews"
os.makedirs(OUT, exist_ok=True)

for o in list(bpy.data.objects):
    bpy.data.objects.remove(o, do_unlink=True)

report = []
bpy.ops.import_scene.fbx(filepath=os.path.join(PACK, "Meshes", "TurtleShellPolyartMesh.fbx"))
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
arms = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
lo = Vector((1e9,) * 3); hi = Vector((-1e9,) * 3)
for m in meshes:
    for c in m.bound_box:
        w = m.matrix_world @ Vector(c)
        lo = Vector(map(min, lo, w)); hi = Vector(map(max, hi, w))
report.append(f"meshes={[m.name for m in meshes]} tris={sum(len(m.data.polygons) for m in meshes)}")
report.append(f"bounds min={tuple(round(v,3) for v in lo)} max={tuple(round(v,3) for v in hi)}")
if arms:
    report.append("bones: " + ", ".join(b.name for b in arms[0].data.bones))
for m in meshes:
    report.append(f"{m.name} materials: {[s.material.name for s in m.material_slots if s.material]}")

for f in sorted(glob.glob(os.path.join(PACK, "Animations", "TurtleShell", "*.fbx"))):
    before = set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=f)
    for a in set(bpy.data.actions) - before:
        fr = a.frame_range
        report.append(f"{os.path.basename(f)} | {a.name} | frames {fr[0]:.0f}-{fr[1]:.0f} ({(fr[1]-fr[0])/30:.2f}s @30fps)")
    for o in list(bpy.context.selected_objects):
        if o not in meshes and o not in arms:
            bpy.data.objects.remove(o, do_unlink=True)

open(os.path.join(OUT, "TurtleShell_reference.txt"), "w", encoding="utf-8").write("\n".join(report))
print("\n".join(report))

# render 3 views of the original mesh
scn = bpy.context.scene
scn.render.engine = "BLENDER_WORKBENCH"
scn.display.shading.light = "STUDIO"
scn.display.shading.color_type = "TEXTURE"
scn.display.shading.show_cavity = True
scn.render.resolution_x, scn.render.resolution_y = 480, 480
center = (lo + hi) / 2
size = max(hi - lo)
cam = bpy.data.objects.new("RefCam", bpy.data.cameras.new("RefCam"))
scn.collection.objects.link(cam)
scn.camera = cam
for name, d in (("front", Vector((0, -1, 0.35))), ("threequarter", Vector((0.8, -0.8, 0.45))), ("side", Vector((1, 0, 0.25)))):
    cam.location = center + d.normalized() * size * 2.2
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()
    scn.render.filepath = os.path.join(OUT, f"TurtleShell_ref_{name}.png")
    bpy.ops.render.render(write_still=True)
print("[ref] done")
