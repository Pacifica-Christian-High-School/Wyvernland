import bpy
import os

root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
obj_path = os.path.join(root, "Artifacts", "Wyvernland-Map.obj")
blend_path = os.path.join(root, "Artifacts", "Wyvernland-Map.blend")

bpy.ops.object.select_all(action="SELECT")
bpy.ops.object.delete(use_global=False)
bpy.ops.wm.obj_import(filepath=obj_path, forward_axis="Y", up_axis="Z")

for obj in bpy.context.selected_objects:
    obj.name = obj.name.replace("Floating_Island_", "Floating Island ")
    if obj.type == "MESH":
        obj.data.name = obj.name + " Mesh"

bpy.context.scene.unit_settings.system = "METRIC"
bpy.context.scene.unit_settings.length_unit = "METERS"
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.context.scene["source"] = "Wyvernland procedural Unity map"
bpy.context.scene["units"] = "1 Blender unit = 1 metre"
bpy.ops.wm.save_as_mainfile(filepath=blend_path, check_existing=False)
print(f"WYVERNLAND_BLEND_EXPORTED {blend_path}")
