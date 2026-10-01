"""Rig the supplied Human Blender model; run with Blender --background --python."""
import bpy, bmesh, math, os, json
from mathutils import Vector, Matrix

ROOT = r'C:\Wyvernland'
SOURCE = r'C:\Users\qze11\OneDrive\桌面\Human.blend'
bpy.ops.wm.open_mainfile(filepath=SOURCE)
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
if mesh.mode != 'OBJECT':
    bpy.context.view_layer.objects.active = mesh
    bpy.ops.object.mode_set(mode='OBJECT')
# Bake the source's symmetry modifier before assigning bones and weights.
if mesh.modifiers:
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(depsgraph)
    baked_mesh = bpy.data.meshes.new_from_object(evaluated,
        preserve_all_data_layers=True, depsgraph=depsgraph)
    old_mesh = mesh.data
    mesh.modifiers.clear()
    mesh.data = baked_mesh
coords = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
bottom = min(v.z for v in coords)
scale = 1.8 / (max(v.z for v in coords) - bottom)
def point(x, y, z):
    return Vector((y * scale, -x * scale, (z - bottom) * scale))
for v, co in zip(mesh.data.vertices, coords):
    v.co = point(*co)
mesh.matrix_world = Matrix.Identity(4)
mesh.name = 'HumanBody'

# Move the supplied T-pose arms into a relaxed near-vertical bind pose. Preserve
# the original coordinates for continuous anatomical skin weighting afterward.
waist_height = point(0,0,0.58).z
source_positions = {v.index: v.co.copy() for v in mesh.data.vertices}
arm_influences = {}
for vertex in mesh.data.vertices:
    lateral = abs(vertex.co.x)
    influence = max(0.0, min(1.0, (lateral - 0.19) / 0.075))
    if influence <= 0.0 or vertex.co.z < waist_height:
        continue
    side = 1 if vertex.co.x > 0 else -1
    pivot = point(0, side * 0.83, 1.82)
    rotation = Matrix.Rotation(side * math.radians(82.0), 4, 'Y')
    lowered = pivot + rotation @ (vertex.co - pivot)
    vertex.co = vertex.co.lerp(lowered, influence)
    arm_influences[vertex.index] = (side, influence, lateral)
# Blender displays both sides by default, while Unity's Lit shader correctly
# culls back faces. Rebuild consistent outward normals before skinning/export.
normal_mesh = bmesh.new()
normal_mesh.from_mesh(mesh.data)
bmesh.ops.recalc_face_normals(normal_mesh, faces=normal_mesh.faces)
normal_mesh.to_mesh(mesh.data)
normal_mesh.free()
mesh.data.update()

arm = bpy.data.armatures.new('HumanSkeleton')
rig = bpy.data.objects.new('HumanRig', arm)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
rig.select_set(True)
mesh.select_set(False)
bpy.ops.object.mode_set(mode='EDIT')
def bone(name, head, tail, parent=None):
    b = arm.edit_bones.new(name)
    b.head, b.tail = point(*head), point(*tail)
    if parent:
        b.parent = arm.edit_bones[parent]
    return b
# Pelvis anchors the continuous lower torso while Waist bends the upper torso.
# Their overlapping weights eliminate the old visible cut at the belt line.
bone('Pelvis', (0,0,-0.32), (0,0,0.58))
bone('Waist', (0,0,0.58), (0,0,1.32), 'Pelvis')
for side, label in [(1, 'Left'), (-1, 'Right')]:
    shoulder = arm.edit_bones.new(label + 'Shoulder')
    shoulder.head = point(0, side * 0.83, 1.82)
    original_tail = point(-0.04, side * 1.69, 1.74)
    rotation = Matrix.Rotation(side * math.radians(82.0), 4, 'Y')
    shoulder.tail = shoulder.head + rotation @ (original_tail - shoulder.head)
    shoulder.parent = arm.edit_bones['Waist']
    elbow = arm.edit_bones.new(label + 'Elbow')
    elbow.head = shoulder.tail
    original_elbow_tail = point(-0.04, side * 2.68, 1.69)
    elbow.tail = shoulder.head + rotation @ (original_elbow_tail - shoulder.head)
    elbow.parent = shoulder
    hip = bone(label + 'Hip', (0, side * 0.38, -0.32),
        (0.04, side * 0.38, -1.52), 'Pelvis')
    knee = bone(label + 'Knee', (0.04, side * 0.38, -1.52),
        (0.02, side * 0.38, -3.18), label + 'Hip')
    ankle = arm.edit_bones.new(label + 'Ankle')
    ankle.head = point(0.02, side * 0.38, -3.18)
    ankle.tail = point(0.61, side * 0.38, -3.39)
    ankle.parent = knee
bpy.ops.object.mode_set(mode='OBJECT')

# One continuous skinned mesh uses smooth transitions at every joint. This keeps
# the torso, pelvis, arms, legs, and feet physically connected while posing.
mesh.parent = rig
modifier = mesh.modifiers.new('Human Armature', 'ARMATURE')
modifier.object = rig
groups = {name: mesh.vertex_groups.new(name=name) for name in
    ('Pelvis', 'Waist', 'LeftShoulder', 'RightShoulder', 'LeftElbow',
     'RightElbow', 'LeftHip', 'RightHip', 'LeftKnee', 'RightKnee',
     'LeftAnkle', 'RightAnkle')}
def smooth(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)
def assign(vertex, weights):
    total = sum(max(0.0, value) for value in weights.values())
    assert total > 0.0001
    for name, value in weights.items():
        if value > 0.0001:
            groups[name].add([vertex.index], value / total, 'REPLACE')

waist_low = point(0,0,0.30).z
waist_high = point(0,0,0.75).z
hip_top = point(0,0,-0.20).z
hip_bottom = point(0,0,-0.65).z
knee_top = point(0,0,-1.28).z
knee_bottom = point(0,0,-1.80).z
ankle_top = point(0,0,-2.85).z
ankle_bottom = point(0,0,-3.18).z
for vertex in mesh.data.vertices:
    original = source_positions[vertex.index]
    if vertex.index in arm_influences:
        side, influence, lateral = arm_influences[vertex.index]
        label = 'Left' if side > 0 else 'Right'
        elbow = smooth((lateral - 0.42) / 0.10)
        assign(vertex, {
            'Waist': 1.0 - influence,
            label + 'Shoulder': influence * (1.0 - elbow),
            label + 'Elbow': influence * elbow
        })
        continue

    height = original.z
    side = 'Left' if original.x >= 0 else 'Right'
    if height >= waist_high:
        assign(vertex, {'Waist': 1.0})
    elif height >= waist_low:
        blend = smooth((height - waist_low) / (waist_high - waist_low))
        assign(vertex, {'Pelvis': 1.0 - blend, 'Waist': blend})
    elif height >= hip_top:
        assign(vertex, {'Pelvis': 1.0})
    elif height >= hip_bottom:
        blend = smooth((hip_top - height) / (hip_top - hip_bottom))
        assign(vertex, {'Pelvis': 1.0 - blend, side + 'Hip': blend})
    elif height >= knee_top:
        assign(vertex, {side + 'Hip': 1.0})
    elif height >= knee_bottom:
        blend = smooth((knee_top - height) / (knee_top - knee_bottom))
        assign(vertex, {side + 'Hip': 1.0 - blend, side + 'Knee': blend})
    elif height >= ankle_top:
        assign(vertex, {side + 'Knee': 1.0})
    elif height >= ankle_bottom:
        blend = smooth((ankle_top - height) / (ankle_top - ankle_bottom))
        assign(vertex, {side + 'Knee': 1.0 - blend, side + 'Ankle': blend})
    else:
        assign(vertex, {side + 'Ankle': 1.0})
# Fail instead of silently exporting an unweighted mesh.
unweighted = [v.index for v in mesh.data.vertices if sum(g.weight for g in v.groups) < 0.001]
assert not unweighted, f'Unweighted vertices: {unweighted}'

def separate_vertices(source, name, predicate):
    existing = set(o for o in bpy.context.scene.objects if o.type == 'MESH')
    bpy.ops.object.select_all(action='DESELECT')
    source.select_set(True)
    bpy.context.view_layer.objects.active = source
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='DESELECT')
    bpy.ops.object.mode_set(mode='OBJECT')
    for vertex in source.data.vertices:
        vertex.select = predicate(vertex)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.separate(type='SELECTED')
    bpy.ops.object.mode_set(mode='OBJECT')
    separated = next(o for o in bpy.context.scene.objects if o.type == 'MESH' and o not in existing)
    separated.name = name
    return separated

# Separate the head at an existing ring so it can cast shadows without blocking
# the first-person camera. The head and remaining upper body retain Waist skinning.
head = separate_vertices(mesh, 'HumanHead', lambda vertex: vertex.co.z > point(0,0,2.36).z)
# The first-person camera follows this mesh through Waist, so fail the export if
# any head vertex has an influence other than Waist.
for vertex in head.data.vertices:
    for membership in vertex.groups:
        if membership.weight > 0.001:
            assert head.vertex_groups[membership.group].name == 'Waist', \
                'HumanHead must be weighted only to Waist'

mat = bpy.data.materials.new('Human Matte Skin')
mat.diffuse_color = (0.72,0.50,0.38,1)
mat.use_nodes = True
bsdf = mat.node_tree.nodes.get('Principled BSDF')
bsdf.inputs['Base Color'].default_value = mat.diffuse_color
bsdf.inputs['Roughness'].default_value = 0.9
for obj in (mesh, head):
    obj.data.materials.clear()
    obj.data.materials.append(mat)
    for p in obj.data.polygons:
        p.use_smooth = False

os.makedirs(ROOT+'/Assets/Resources/Player', exist_ok=True)
os.makedirs(ROOT+'/Artifacts/Player', exist_ok=True)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=ROOT+'/Assets/Resources/Player/HumanRigged.fbx',
    use_selection=True, object_types={'MESH','ARMATURE'}, add_leaf_bones=False,
    bake_anim=False, axis_forward='-Z', axis_up='Y', use_mesh_modifiers=True)
bpy.ops.wm.save_as_mainfile(filepath=ROOT+'/Artifacts/Player/HumanRigged.blend')
print('RIG_VALIDATED', len(arm.bones), 'bones; continuous body with elbows and knees; height 1.8m')

# Render the neutral bind pose for a quick rig check.
scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE'
scene.render.resolution_x = 800
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.world = bpy.data.worlds.new('Preview World')
scene.world.use_nodes = True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(0.16,0.18,0.22,1)
scene.world.node_tree.nodes['Background'].inputs[1].default_value=0.6
bpy.ops.object.camera_add(location=(2.4,-4,2.1))
cam = bpy.context.object
cam.rotation_euler = (Vector((0,0,0.92))-cam.location).to_track_quat('-Z','Y').to_euler()
cam.data.type='ORTHO'
cam.data.ortho_scale=2.35
scene.camera=cam
bpy.ops.object.light_add(type='AREA', location=(1,-3,4))
bpy.context.object.data.energy=350
bpy.context.object.data.shape='DISK'
bpy.context.object.data.size=3
scene.render.filepath=ROOT+'/Artifacts/Player/rig-neutral.png'
bpy.ops.render.render(write_still=True)
