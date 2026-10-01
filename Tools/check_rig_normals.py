import bpy

bpy.ops.wm.open_mainfile(filepath=r'C:\Wyvernland\Artifacts\Player\HumanRigged.blend')
for obj in (item for item in bpy.context.scene.objects if item.type == 'MESH'):
    mesh = obj.data
    mesh.calc_loop_triangles()
    signed_volume = 0.0
    for triangle in mesh.loop_triangles:
        a, b, c = (mesh.vertices[index].co for index in triangle.vertices)
        signed_volume += a.dot(b.cross(c)) / 6.0
    print('NORMAL_CHECK', obj.name, signed_volume)
    if signed_volume <= 0:
        raise RuntimeError(obj.name + ' has inward-facing normals')
print('NORMAL_CHECK_PASS')
