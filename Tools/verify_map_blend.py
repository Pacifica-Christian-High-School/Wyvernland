import bpy
import os

blend_path = r"C:\Wyvernland\Artifacts\Wyvernland-Map.blend"
bpy.ops.wm.open_mainfile(filepath=blend_path)

mesh_objects = [obj for obj in bpy.data.objects if obj.type == "MESH"]
assert len(mesh_objects) == 3, f"Expected 3 island meshes, found {len(mesh_objects)}"

vertices = sum(len(obj.data.vertices) for obj in mesh_objects)
faces = sum(len(obj.data.polygons) for obj in mesh_objects)
assert vertices > 0 and faces > 0, "Map meshes are empty"

print(f"WYVERNLAND_BLEND_VALID objects={len(mesh_objects)} vertices={vertices} faces={faces} bytes={os.path.getsize(blend_path)}")
