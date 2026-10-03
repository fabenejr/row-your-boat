"""
Builds the Viking Oarsmen rowing oar in Blender (low-poly, Valheim style) and exports it.

Run headless from the repo root:
    blender -b --python art/oar/build_oar.py

Outputs (next to this script): oar.blend, oar.fbx, oar_d.png (diffuse texture).

Conventions (see VikingOarsmen-Documents/Pipeline-Asset-e-Animacao.md):
- 1 Blender unit = 1 m.
- Origin = upper hand grip (`grip_top`), like the vanilla weapons' `attach` child.
- The oar lies along Blender -Y (blade toward -Y), blade flat in the XY plane. With the FBX settings
  used below (Forward -Z, Up Y, Apply Transform) Blender -Y becomes Unity +Z, which is the axis the
  vanilla weapons extend along from the grip, and the blade faces Unity Y.
- Empties `grip_top`, `grip_bottom`, `fulcrum`, `blade_tip` are children of the mesh for the code to read.
"""

import math
import os

import bmesh
import bpy
import numpy as np
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
SIDES = 8

# Distances along the shaft from the upper grip, as designed (+ toward the blade). The whole oar is then
# stretched along the shaft to LENGTH (tip to tip) without changing its thickness; see to_blender.
LENGTH = 2.98
CAP = 0.006  # how far the end caps' center points stick out past the last sections
GRIP_BOTTOM = 0.45   # RowerPose.HandSpacing
FULCRUM = 0.85       # where the shaft rests on the gunwale
LASH_START, LASH_END = 1.50, 1.62
BLADE_TIP = 2.52

# Cross-section profile: (s, half width, half thickness, material). Width is across the blade (X),
# thickness is through it (Z). The shaft is round (width == thickness); the knob is a flattened pear.
PROFILE = [
    # pear-shaped top knob
    (-0.150, 0.012, 0.010, "wood"),
    (-0.140, 0.034, 0.024, "wood"),
    (-0.115, 0.054, 0.036, "wood"),
    (-0.080, 0.048, 0.034, "wood"),
    (-0.045, 0.036, 0.032, "wood"),
    # shaft, swelling a little toward the loom (where it takes the gunwale load)
    (0.000, 0.033, 0.033, "wood"),
    (0.450, 0.036, 0.036, "wood"),
    (0.850, 0.041, 0.041, "wood"),
    (1.250, 0.038, 0.038, "wood"),
    # rope lashing at the blade neck (two bulging turns)
    (LASH_START, 0.038, 0.038, "rope"),
    (1.525, 0.047, 0.047, "rope"),
    (1.560, 0.042, 0.042, "rope"),
    (1.595, 0.047, 0.047, "rope"),
    (LASH_END, 0.038, 0.038, "wood"),
    # leaf blade: neck flares out, widest past the middle, rounded tip
    (1.700, 0.050, 0.028, "wood"),
    (1.800, 0.072, 0.020, "wood"),
    (1.950, 0.098, 0.017, "wood"),
    (2.100, 0.113, 0.015, "wood"),
    (2.250, 0.116, 0.014, "wood"),
    (2.360, 0.104, 0.013, "wood"),
    (2.440, 0.078, 0.012, "wood"),
    (2.490, 0.046, 0.011, "wood"),
    (BLADE_TIP, 0.010, 0.008, "wood"),
]

# Texture layout: U = around the section (0..1), V = along the oar, mapped linearly from S_MIN..S_MAX.
S_MIN, S_MAX = PROFILE[0][0], PROFILE[-1][0]
STRETCH = LENGTH / (S_MAX - S_MIN + 2 * CAP)
TEX_W, TEX_H = 64, 512


def v_of(s):
    return (s - S_MIN) / (S_MAX - S_MIN)


def to_blender(s, x, z):
    """Oar frame (s along the shaft, x across, z through) -> Blender coords (shaft along -Y)."""
    return Vector((x, -s * STRETCH, z))


def section(half_w, half_t, s):
    # Blade sections get a mid ridge (lens shape); round ones stay a plain octagon.
    flat = half_w > half_t * 1.4
    pts = []
    for k in range(SIDES):
        a = 2 * math.pi * k / SIDES
        x, z = half_w * math.cos(a), half_t * math.sin(a)
        if flat and k % 4 != 0:
            # keep the edge at full width, thin toward it, full thickness on the spine
            x *= 0.85
            z *= 0.75 if k % 2 else 1.0
        pts.append(to_blender(s, x, z))
    return pts


def build_mesh():
    mesh = bpy.data.meshes.new("Oar")
    obj = bpy.data.objects.new("Oar", mesh)
    bpy.context.collection.objects.link(obj)

    wood = make_material("oar_wood")
    obj.data.materials.append(wood)
    mat_index = {"wood": 0, "rope": 0}  # one material, rope is painted in the texture

    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    rings = [[bm.verts.new(p) for p in section(w, t, s)] for s, w, t, _ in PROFILE]

    for i in range(len(rings) - 1):
        a, b = rings[i], rings[i + 1]
        va, vb = v_of(PROFILE[i][0]), v_of(PROFILE[i + 1][0])
        for k in range(SIDES):
            k2 = (k + 1) % SIDES
            f = bm.faces.new((a[k], a[k2], b[k2], b[k]))
            f.material_index = mat_index[PROFILE[i][3]]
            u0, u1 = k / SIDES, (k + 1) / SIDES
            for loop, coord in zip(f.loops, ((u0, va), (u1, va), (u1, vb), (u0, vb))):
                loop[uv].uv = coord

    # Cap both ends with a fan to a center point.
    for ring, (s, _, _, _), outward in ((rings[0], PROFILE[0], -1), (rings[-1], PROFILE[-1], 1)):
        center = bm.verts.new(to_blender(s + outward * CAP, 0, 0))
        vc = v_of(s)
        for k in range(SIDES):
            k2 = (k + 1) % SIDES
            verts = (ring[k], ring[k2], center) if outward > 0 else (ring[k2], ring[k], center)
            f = bm.faces.new(verts)
            for loop in f.loops:
                loop[uv].uv = (0.5, vc)

    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(mesh)
    bm.free()

    for p in mesh.polygons:
        p.use_smooth = True
    mesh.use_auto_smooth = True
    mesh.auto_smooth_angle = math.radians(50)
    return obj


def paint_texture():
    """Small hand-painted-looking wood texture: dark knob band, grain on the shaft,
    laminated stripes down the blade, rope turns at the neck."""
    rng = np.random.default_rng(7)
    u = (np.arange(TEX_W) + 0.5) / TEX_W
    v = (np.arange(TEX_H) + 0.5) / TEX_H
    U, V = np.meshgrid(u, v)
    S = S_MIN + V * (S_MAX - S_MIN)

    light = np.array([0.62, 0.42, 0.24])
    dark = np.array([0.33, 0.20, 0.11])
    img = np.ones((TEX_H, TEX_W, 3)) * light

    # long grain: low-frequency streaks along the length, varying around the section
    grain = 0.5 + 0.5 * np.sin(U * 2 * math.pi * 3 + np.sin(S * 7.0) * 0.8 + rng.normal(0, 0.15, U.shape))
    img *= (0.88 + 0.12 * grain)[..., None]

    # blade: laminated stripes along the spine of each face (dark core, pale strips, dark lines)
    blade = S > LASH_END
    pale = np.array([0.80, 0.60, 0.36])
    for c in (0.25, 0.75):
        d = np.abs(U - c)
        img[blade & (d < 0.020)] = dark
        img[blade & (d >= 0.020) & (d < 0.040)] = pale
        img[blade & (d >= 0.040) & (d < 0.050)] = dark * 1.2

    # knob: dark wood cap like the canoe-paddle grips
    knob = S < -0.03
    img[knob] = dark * (0.9 + 0.1 * grain[knob])[..., None]

    # rope: diagonal turns, pale hemp
    rope = (S >= LASH_START) & (S <= LASH_END)
    twist = np.mod((S - LASH_START) * 90 + U * 3, 1.0)
    hemp = np.array([0.71, 0.62, 0.45])
    img[rope] = (hemp * (0.72 + 0.28 * np.sin(twist * math.pi))[..., None])[rope]

    # wear: hands darken the grips a little
    for g in (0.0, GRIP_BOTTOM):
        w = np.exp(-((S - g) / 0.08) ** 2)
        img *= (1 - 0.18 * w)[..., None]

    # paint noise, kept soft so it reads like Valheim's blurry hand-painted textures
    img *= (1 + rng.normal(0, 0.03, (TEX_H, TEX_W)))[..., None]
    img = np.clip(img, 0, 1)

    image = bpy.data.images.new("oar_d", TEX_W, TEX_H, alpha=False)
    rgba = np.concatenate([img, np.ones((TEX_H, TEX_W, 1))], axis=2)
    image.pixels.foreach_set(rgba.astype(np.float32).ravel())
    image.filepath_raw = os.path.join(HERE, "oar_d.png")
    image.file_format = "PNG"
    image.save()
    return image


def make_material(name):
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    bsdf = nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = 0.85
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = paint_texture()
    tex.interpolation = "Linear"
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def add_empty(parent, name, s, size=0.08):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "ARROWS"
    e.empty_display_size = size
    e.location = to_blender(s, 0, 0)
    e.parent = parent
    bpy.context.collection.objects.link(e)
    return e


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0

    oar = build_mesh()
    add_empty(oar, "grip_top", 0.0)
    add_empty(oar, "grip_bottom", GRIP_BOTTOM)
    add_empty(oar, "fulcrum", FULCRUM)
    add_empty(oar, "blade_tip", BLADE_TIP)

    tris = sum(len(p.vertices) - 2 for p in oar.data.polygons)
    dims = oar.dimensions
    print(f"[oar] tris={tris} length={dims.y:.3f} m width={dims.x:.3f} m thickness={dims.z:.3f} m")

    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(HERE, "oar.blend"))
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(HERE, "oar.fbx"),
        object_types={"MESH", "EMPTY"},
        apply_scale_options="FBX_SCALE_UNITS",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,  # "Apply Transform"
        mesh_smooth_type="FACE",
        path_mode="AUTO",
        embed_textures=False,
    )


main()
