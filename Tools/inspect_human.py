import bpy, json
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=r'C:\Users\qze11\OneDrive\桌面\Human.fbx')
for obj in bpy.context.scene.objects:
    print('MODEL', obj.name, obj.type, list(obj.dimensions), list(obj.location))
    if obj.type == 'MESH':
        coords = [obj.matrix_world @ v.co for v in obj.data.vertices]
        print('VERTICES', len(coords), 'BOUNDS', [[min(v[i] for v in coords), max(v[i] for v in coords)] for i in range(3)])
        print('POINTS', json.dumps([list(v) for v in coords]))
bpy.ops.wm.save_as_mainfile(filepath=r'C:\Wyvernland\Artifacts\human-inspect.blend')
