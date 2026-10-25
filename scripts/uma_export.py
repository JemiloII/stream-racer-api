# uma_export.py: reads Umamusume Pretty Derby's own asset files on this PC (decrypting them the way the game does) and
# writes the horse girls for Stream Racer's Uma race: one folder per uma with model.json (skeleton, meshes, which
# texture each part uses), model.bin (vertex data) and the PNG textures, straight into the game's plugin folder
# (BepInEx/plugins/StreamRacerApi.uma). Nothing of Cygames' is ever committed or released.
#   python scripts/uma_export.py            export every playable uma whose files the game has downloaded
#   python scripts/uma_export.py 1001 1007  export these chara ids
#   python scripts/uma_export.py gym [ids]  the same umas in the gym uniform (Trackwear) -> folders <id>_gym
#   python scripts/uma_export.py theme <vgmstream-cli.exe>   the music -> theme.wav + race_*.wav (needs vgmstream)
#   python scripts/uma_export.py motions    the game's race motions -> motions/type0N_<name>.json
#   python scripts/uma_export.py probe <asset name>
import json
import os
import zlib
import sqlite3
import shutil
import struct
import subprocess
import tempfile
import sys

import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

PERSISTENT = os.environ.get(
    "UMA_PERSISTENT",
    r"C:/Program Files (x86)/Steam/steamapps/common/UmamusumePrettyDerby/UmamusumePrettyDerby_Data/Persistent",
)
OUTPUT = os.environ.get(
    "UMA_OUTPUT",
    r"C:/Program Files (x86)/Steam/steamapps/common/Stream Racer/BepInEx/plugins/StreamRacerApi.uma",
)
# Umamusume's _diff textures are the fully lit colour (skin nearly white even on a tanned uma); her real tone comes
# from the toon shader mixing in the _shad_c shadow colour. Stream Racer has no toon shader, so the mix is baked in.
TONE = float(os.environ.get("UMA_TONE", "0.45"))   # how much of the shadow colour goes into the baked texture

MUD = (92, 70, 48)                                # mud brown splashed on in the rain
MUD_STRENGTH = 0.6                                # how much of the dirt mask turns to mud

BASE_KEY = bytes.fromhex("532B4631E4A7B9473E7CFB")   # the game's asset-bundle base key (as UmaViewer's Config.json has it)

# ---- reading the game's files ----

def database():
    meta = next(name for name in os.listdir(PERSISTENT) if name.startswith("meta_decrypted"))
    return sqlite3.connect(os.path.join(PERSISTENT, meta))


def master():
    return sqlite3.connect(os.path.join(PERSISTENT, "master", "master.mdb"))


# Every playable character (card_data) -> what the race needs about her, from the game's own master database: name
# (text_data category 6), height in cm (chara_data.scale), running form (race_running_type 1-3), tail model, and her
# strategy (card_data.running_style: 1 front runner, 2 pace chaser, 3 late surger, 4 end closer; her first card's).
def roster():
    db = master()
    names = dict(db.execute('select "index", text from text_data where category = 6'))
    styles = {}
    for chara, style in db.execute("select chara_id, running_style from card_data order by id"):
        styles.setdefault(chara, style)
    everyone = {}
    for chara, height, running_type, tail, size, shape, bust, skin, socks in db.execute(
            "select id, scale, race_running_type, tail_model_id, height, shape, bust, skin, socks from chara_data"):
        if chara in styles:
            everyone[chara] = {
                "name": names.get(chara, str(chara)),
                "heightCm": height,
                "runningType": running_type,
                "style": styles[chara],
                "tail": tail if tail and tail > 0 else 1,
                "body": (size, shape, bust, skin, socks),   # which shared body + texture a generic outfit uses
            }
    return dict(sorted(everyone.items()))


def downloaded(db, name):
    row = db.execute("select h from a where n = ?", (name,)).fetchone()
    return row is not None and os.path.exists(os.path.join(PERSISTENT, "dat", row[0][:2], row[0]))


def entry(db, name):
    row = db.execute("select n, h, e, d from a where n = ?", (name,)).fetchone()
    if row is None:
        raise KeyError(name)
    return row


def bundle_bytes(hash_name, key):
    path = os.path.join(PERSISTENT, "dat", hash_name[:2], hash_name)
    data = bytearray(open(path, "rb").read())
    if key == 0 or len(data) <= 256:
        return bytes(data)
    key_bytes = (key & 0xFFFFFFFFFFFFFFFF).to_bytes(8, "little")
    keys = bytes(base ^ key_byte for base in BASE_KEY for key_byte in key_bytes)
    for index in range(256, len(data)):
        data[index] ^= keys[index % len(keys)]
    return bytes(data)


# The bundle plus everything it depends on (column d, ';'-separated).
def load_with_dependencies(db, name, seen):
    if name in seen:
        return
    try:
        _, hash_name, key, dependencies = entry(db, name)
    except KeyError:
        return
    seen[name] = bundle_bytes(hash_name, key)
    for dependency in (dependencies or "").split(";"):
        if dependency:
            load_with_dependencies(db, dependency, seen)


def environment(db, names):
    files = {}
    for name in names:
        load_with_dependencies(db, name, files)
    env = UnityPy.Environment()
    for bundle_name, data in files.items():
        env.load_file(data, name=bundle_name)
    env.sr_files = files
    return env


# The real Uma look: Stream Racer's Unity loads the game's own bundles (shaders included), so every bundle a uma needs
# is written decrypted to the shared bundles/ folder (each once) and listed in her model.json.
def bundle_file(name):
    return "".join(character if character.isalnum() or character == "_" else "_" for character in name) + ".bundle"


def write_bundles(env, extra=None):
    folder = os.path.join(OUTPUT, "bundles")
    os.makedirs(folder, exist_ok=True)
    files = dict(env.sr_files)
    files.update(extra or {})
    written = []
    for name, data in files.items():
        target = os.path.join(folder, bundle_file(name))
        if not os.path.exists(target) or os.path.getsize(target) != len(data):
            open(target, "wb").write(data)
        written.append(bundle_file(name))
    return sorted(written)


# ---- building one uma ----

class Model:
    def __init__(self):
        self.bones = []            # {name, parent, p, r, s}
        self.by_name = {}
        self.meshes = []
        self.blob = bytearray()
        self.textures = {}         # texture name -> Texture2D

    def floats(self, values):
        offset = len(self.blob)
        self.blob += struct.pack("<%df" % len(values), *values)
        return offset

    def ints(self, values):
        offset = len(self.blob)
        self.blob += struct.pack("<%di" % len(values), *values)
        return offset

    # One prefab's transforms into the shared skeleton: a bone whose name the skeleton already has is that bone
    # (the head and tail hang off the body's own Neck / Hip), every other bone keeps its parent.
    def add_skeleton(self, root):
        mapping = {}
        stack: list = [(root, None)]
        while stack:
            transform, parent = stack.pop()
            name = transform.m_GameObject.read().m_Name
            if name in self.by_name or (parent is None and self.bones):
                index = self.by_name.get(name, 0)
            else:
                index = len(self.bones)
                self.bones.append({
                    "name": name,
                    "parent": parent if parent is not None else -1,
                    "p": list(vector(transform.m_LocalPosition)),
                    "r": list(quaternion(transform.m_LocalRotation)),
                    "s": list(vector(transform.m_LocalScale)),
                })
                self.by_name[name] = index
            mapping[path_of(transform)] = index
            for child in transform.m_Children:
                stack.append((child.read(), index))
        return mapping

    def add_mesh(self, name, mesh, bones, bindposes, materials, attach):
        handler = MeshHandler(mesh)
        handler.process()
        vertices = handler.m_Vertices or []
        count = len(vertices)
        entry_ = {
            "name": name,
            "blend": "multiply" if name.startswith("M_Cheek") else None,   # the blush multiplies onto the face
            "vertexCount": count,
            "attach": attach,
            "positions": self.floats([value for vertex in vertices for value in vertex]),
            "normals": self.floats([value for normal in handler.m_Normals for value in normal])
            if handler.m_Normals else -1,
            "uvs": self.floats([value for uv in handler.m_UV0 for value in uv[:2]]) if handler.m_UV0 else -1,
            "bones": bones,
            "bindposes": [matrix(pose) for pose in bindposes],
            "submeshes": [],
        }
        if bones and handler.m_BoneWeights:
            entry_["boneIndices"] = self.ints([index for four in handler.m_BoneIndices or [] for index in four])
            entry_["boneWeights"] = self.floats([weight for four in handler.m_BoneWeights for weight in four])
        indices = handler.m_IndexBuffer or []
        size = 2 if handler.m_Use16BitIndices else 4
        # Unity only draws as many submeshes as the renderer has materials; the rest (whole-body copies the game keeps
        # for its own extra passes) would draw a second skin over the clothes, so they are left out
        drawn = mesh.m_SubMeshes[:len(materials)] if materials else mesh.m_SubMeshes
        for number, submesh in enumerate(drawn):
            first = submesh.firstByte // size
            part = list(indices[first:first + submesh.indexCount])
            material = materials[number] if number < len(materials) else (materials[-1] if materials else None)
            entry_["submeshes"].append({
                "indices": self.ints(part),
                "indexCount": len(part),
                "texture": self.main_texture(material),
                "material": material.m_Name if material is not None else None,
            })
        self.meshes.append(entry_)

    def main_texture(self, material):
        if material is None:
            return None
        for key, environment_ in material.m_SavedProperties.m_TexEnvs:
            if key == "_MainTex" and environment_.m_Texture and environment_.m_Texture.path_id:
                texture = environment_.m_Texture.read()
                self.textures[texture.m_Name] = texture
                return texture.m_Name
        return None

    # Every renderer under one prefab: skinned meshes with their bones, plain meshes stuck to their transform.
    def add_part(self, env, prefab_name):
        root = prefab_root(env, prefab_name)
        if root is None:
            print("  missing", prefab_name)
            return
        mapping = self.add_skeleton(root)
        for item in env.objects:
            kind = item.type.name
            if kind == "SkinnedMeshRenderer":
                renderer = item.read()
                if not owned_by(renderer, mapping) or not renderer.m_Mesh.path_id:
                    continue
                mesh = renderer.m_Mesh.read()
                if mesh.m_Name.startswith("M_Tear"):
                    continue
                bones = [mapping.get(bone.path_id, 0) for bone in renderer.m_Bones]
                materials = [reference.read() for reference in renderer.m_Materials if reference.path_id]
                self.add_mesh(mesh.m_Name, mesh, bones, mesh.m_BindPose, materials, -1)
            elif kind == "MeshRenderer":
                renderer = item.read()
                if not owned_by(renderer, mapping):
                    continue
                game_object = renderer.m_GameObject.read()
                mesh = mesh_of(game_object)
                if mesh is None or mesh.m_Name.startswith("M_Tear"):   # tears only show when she cries
                    continue
                materials = [reference.read() for reference in renderer.m_Materials if reference.path_id]
                attach = mapping[game_object.m_Transform.path_id] if hasattr(game_object, "m_Transform") \
                    else mapping.get(transform_of(game_object).path_id, 0)
                self.add_mesh(mesh.m_Name, mesh, [], [], materials, attach)


def prefab_root(env, prefab_name):
    for item in env.objects:
        if item.type.name == "GameObject":
            game_object = item.read()
            if game_object.m_Name == prefab_name:
                transform = transform_of(game_object)
                if transform is not None:
                    return transform
    return None


def transform_of(game_object):
    for component in game_object.m_Components:
        reference = component.component if hasattr(component, "component") else component
        target = reference.read()
        if target.object_reader.type.name == "Transform":
            return target
    return None


def mesh_of(game_object):
    for component in game_object.m_Components:
        reference = component.component if hasattr(component, "component") else component
        target = reference.read()
        if target.object_reader.type.name == "MeshFilter" and target.m_Mesh.path_id:
            return target.m_Mesh.read()
    return None


def owned_by(renderer, mapping):
    transform = transform_of(renderer.m_GameObject.read())
    return transform is not None and path_of(transform) in mapping


# Her colour as the game shows it: the lit colour (_diff) blended toward her own shadow colour (_shad_c), alpha kept.
def toned(env, diff):
    image = diff.image.convert("RGBA")
    shadow_name = diff.m_Name[:-len("_diff")] + "_shad_c" if diff.m_Name.endswith("_diff") else None
    shadow = next((item.read() for item in env.objects if shadow_name and item.type.name == "Texture2D"
                   and item.read().m_Name == shadow_name), None)
    if shadow is None:
        return image
    from PIL import Image
    shade = shadow.image.convert("RGBA").resize(image.size)
    blended = Image.blend(image, shade, TONE)
    blended.putalpha(image.getchannel("A"))
    return blended


# Where a texture lives in the game's files (by its name), or None.
def texture_asset(db, name):
    row = db.execute("select n from a where n like ?", ("%/" + name,)).fetchone()
    return row[0] if row and downloaded(db, row[0]) else None


def texture_named(env, name):
    return next((item.read() for item in env.objects
                 if item.type.name == "Texture2D" and item.read().m_Name == name), None)


# Her rain look, like the game's wet + dirty race: her wet colours (_diff_wet, toned like the dry ones) with mud
# brown where her dirt mask (_dirt: splotches in red and green) says, written as <texture>_rain.png next to it.
def rain_versions(db, folder, texture_names):
    for name in texture_names:
        if not name.endswith("_diff"):
            continue
        stem = name[:-len("_diff")]
        suffixes = ("_diff_wet", "_shad_c_wet", "_dirt", "_diff", "_shad_c")
        extra = [texture_asset(db, stem + suffix) for suffix in suffixes]
        extra = [asset for asset in extra if asset]
        if not extra:
            continue
        env = environment(db, extra)
        base = texture_named(env, stem + "_diff_wet") or texture_named(env, name)
        if base is None:
            continue
        image = toned(env, base)
        dirt = texture_named(env, stem + "_dirt")
        if dirt is not None:
            image = muddy(image, dirt.image.convert("RGB").resize(image.size))
        image.save(os.path.join(folder, name + "_rain.png"))


def muddy(image, dirt):
    from PIL import Image, ImageChops
    red, green, _ = dirt.split()
    splotches = ImageChops.lighter(red, green)
    mask = Image.eval(splotches, lambda value: min(255, int(value * 2 * MUD_STRENGTH)))
    mud = Image.new("RGBA", image.size, MUD + (255,))
    muddied = Image.composite(mud, image, mask)
    muddied.putalpha(image.getchannel("A"))
    return muddied


def path_of(unity_object):
    return unity_object.object_reader.path_id if unity_object is not None else None


def vector(value):
    return (value.x, value.y, value.z)


def quaternion(value):
    return (value.x, value.y, value.z, value.w)


def matrix(pose):
    return [getattr(pose, "e%d%d" % (row, column)) for row in range(4) for column in range(4)]


# The gym uniform (dress 1, Trackwear) is a shared body: the prefab for her height / shape / bust bucket
# (pfb_bdy0001_00_<setting>_<height>_<shape>_<bust>; setting 02 = "Race Bloomers", the buruma; 01 = race shorts)
# recoloured with the texture for her skin, bust and socks (tex_bdy0001_00_00_<skin>_<bust>_0<socks>_diff), as
# UmaViewer builds it.
def gym_body(about):
    size, shape, bust, skin, socks = about["body"]
    body = "3d/chara/body/bdy0001_00/pfb_bdy0001_00_02_%d_%d_%d" % (size, shape, bust)
    texture = "3d/chara/body/bdy0001_00/textures/offline/tex_bdy0001_00_00_%d_%d_0%d_diff" % (skin, bust, socks)
    return body, texture


# Her tail: an exclusive one (tail<id>_00) when she has it, else the shared model (chara_data.tail_model_id)
# coloured with her own texture (tex_tail<model>_00_<id>_diff), as UmaViewer loads it.
def tail_of(db, chara_id, tail_model):
    exclusive = "tail%d_00" % chara_id
    if downloaded(db, "3d/chara/tail/%s/pfb_%s" % (exclusive, exclusive)):
        return "3d/chara/tail/%s/pfb_%s" % (exclusive, exclusive), "pfb_" + exclusive, "tex_%s_diff" % exclusive
    shared = "tail%04d_00" % tail_model
    return "3d/chara/tail/%s/pfb_%s" % (shared, shared), "pfb_" + shared, "tex_%s_%d_diff" % (shared, chara_id)


def export(db, chara_id, about, outfit="racewear"):
    name = about["name"]
    body = "3d/chara/body/bdy%d_00/pfb_bdy%d_00" % (chara_id, chara_id)
    skin_texture = None
    if outfit == "gym":
        body, skin_texture = gym_body(about)
    head = "3d/chara/head/chr%d_00/pfb_chr%d_00" % (chara_id, chara_id)
    needed = [body, head] + ([skin_texture] if skin_texture else [])
    shadows = [skin_texture[:-len("_diff")] + "_shad_c"] if skin_texture else []
    if not all(downloaded(db, asset) for asset in needed):
        print("%d %s (%s): not downloaded by the game yet, skipped" % (chara_id, name, outfit))
        return
    tail, tail_prefab, tail_texture = tail_of(db, chara_id, about["tail"])
    tail_stem = tail_texture[:-len("_diff")]
    # a shared tail's masks are the model's _0000 set; an exclusive tail (tex_tail<id>_00) has its own
    shared_masks = tail_stem[:tail_stem.index("_00_") + len("_00_")] + "0000" if "_00_" in tail_stem else tail_stem
    tail_assets = [tail] + [texture_asset(db, name) for name in (
        tail_texture, tail_stem + "_shad_c", shared_masks + "_base", shared_masks + "_ctrl")]
    env = environment(db, needed + [asset for asset in tail_assets if asset]
                      + [shadow for shadow in shadows if downloaded(db, shadow)])
    model = Model()
    model.add_part(env, body.rsplit("/", 1)[1])
    body_meshes = list(model.meshes)
    if skin_texture:
        recolour(body_meshes, model, env, skin_texture.rsplit("/", 1)[1])
    model.add_part(env, "pfb_chr%d_00" % chara_id)
    before_tail = len(model.meshes)
    model.add_part(env, tail_prefab)
    texture = texture_named(env, tail_texture)
    if texture is not None:   # the shared tail mesh carries no texture of its own: hers, matching her hair
        model.textures[tail_texture] = texture
        for mesh in model.meshes[before_tail:]:
            for submesh in mesh["submeshes"]:
                submesh["texture"] = tail_texture
    folder = os.path.join(OUTPUT, str(chara_id) + ("_gym" if outfit == "gym" else ""))
    os.makedirs(folder, exist_ok=True)
    for texture_name, texture in model.textures.items():
        toned(env, texture).save(os.path.join(folder, texture_name + ".png"))
    rain_versions(db, folder, list(model.textures))
    save_icon(db, chara_id, folder)
    open(os.path.join(folder, "model.bin"), "wb").write(model.blob)
    info = {
        "id": chara_id,
        "name": name,
        "bundles": write_bundles(env),
        "heightCm": about["heightCm"],
        "runningType": about["runningType"],
        "style": about["style"],
        "bones": model.bones,
        "meshes": model.meshes,
    }
    json.dump(info, open(os.path.join(folder, "model.json"), "w"), separators=(",", ":"))
    print("%d %s (%s): %d bones, %d meshes, %d textures" % (
        chara_id, name, outfit, len(model.bones), len(model.meshes), len(model.textures)))


# The gym body has three materials, as the game paints them (UmaViewer, UmaContainerCharacter): 0 = the clothes,
# coloured by gate frame (tex_bdy0001_00_00_waku<0-7>_diff), 1 = her skin (skin / bust / socks), 2 = the number bib
# (tex_bdy0001_00_zekken<0-4>_<bust>_diff). Clothes and bib are written as "gym:waku" / "gym:zekken_<bust>": the race
# picks random colours from the shared gym/ folder (export_gym_colours), like the game does.
def recolour(body_meshes, model, env, texture_name):
    texture = next((item.read() for item in env.objects
                    if item.type.name == "Texture2D" and item.read().m_Name == texture_name), None)
    if texture is None:
        print("  no texture", texture_name)
        return
    bust = texture_name.split("_")[5]
    model.textures[texture_name] = texture
    # the shared body's materials carry no texture of their own (the game fills them in), so go by material slot
    for mesh in body_meshes:
        for number, submesh in enumerate(mesh["submeshes"]):
            submesh["texture"] = {0: "gym:waku", 2: "gym:zekken_" + bust}.get(number, texture_name)
    for unused in [key for key in model.textures if key.startswith("tex_bdy0001") and key != texture_name]:
        del model.textures[unused]


# Her little face (chara/chr<id>/chr_icon_<id>) for the skill pop-up.
def save_icon(db, chara_id, folder):
    asset = "chara/chr%d/chr_icon_%d" % (chara_id, chara_id)
    if not downloaded(db, asset):
        return
    icon = texture_named(environment(db, [asset]), "chr_icon_%d" % chara_id)
    if icon is not None:
        icon.image.save(os.path.join(folder, "icon.png"))


# The in-race skill effect indicator the game floats over a girl (atlas/race/race sprite utx_race_skill_icon_0_000:
# "Velocity Up"), shown when her car boosts.
def export_cutin(db):
    env = environment(db, ["atlas/race/race", "atlas/race/race_tex"])
    for item in env.objects:
        if item.type.name == "Sprite" and item.read().m_Name == "utx_race_skill_icon_0_000":
            item.read().image.save(os.path.join(OUTPUT, "velocity_up.png"))
            print("velocity_up.png written")
            return


def export_gym_colours(db):
    folder = os.path.join(OUTPUT, "gym")
    os.makedirs(folder, exist_ok=True)
    names = ["tex_bdy0001_00_00_waku%d_diff" % colour for colour in range(8)]
    names += ["tex_bdy0001_00_zekken%d_%d_diff" % (colour, bust) for colour in range(5) for bust in range(5)]
    wanted = names + [name[:-len("_diff")] + "_shad_c" for name in names]
    assets = ["3d/chara/body/bdy0001_00/textures/%s/%s" % ("zekken" if "zekken" in name else "offline", name)
              for name in wanted]
    real = ["tex_bdy0001_00_00_waku0_base", "tex_bdy0001_00_00_waku0_ctrl"]
    real += ["tex_bdy0001_00_00_0_%d_00_%s" % (bust, kind) for bust in range(5) for kind in ("base", "ctrl")]
    real += ["tex_bdy0001_00_zekken0_%d_%s" % (bust, kind) for bust in range(5) for kind in ("base", "ctrl")]
    assets += [texture_asset(db, name) for name in real]
    env = environment(db, [asset for asset in assets if asset and downloaded(db, asset)])
    write_bundles(env)
    json.dump({"bundles": write_bundles(env)}, open(os.path.join(folder, "bundles.json"), "w"))
    for item in env.objects:
        if item.type.name == "Texture2D":
            texture = item.read()
            if texture.m_Name in names:
                toned(env, texture).save(os.path.join(folder, texture.m_Name + ".png"))
    rain_versions(db, folder, [name for name in names if "waku" in name])
    print("gym colours written")


def probe(name):
    env = environment(database(), [name])
    counts = {}
    for item in env.objects:
        counts[item.type.name] = counts.get(item.type.name, 0) + 1
    print(name, counts)


if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "probe":
        probe(sys.argv[2])
    elif len(sys.argv) > 2 and sys.argv[1] == "theme":
        theme(sys.argv[2])
    elif len(sys.argv) > 1 and sys.argv[1] == "motions":
        motions()
    else:
        connection = database()
        everyone = roster()
        arguments = sys.argv[1:]
        outfit = "gym" if arguments and arguments[0] == "gym" else "racewear"
        arguments = arguments[1:] if outfit == "gym" else arguments
        if outfit == "gym":
            export_gym_colours(connection)
        export_cutin(connection)
        for chara in [int(arg) for arg in arguments] or list(everyone):
            try:
                export(connection, chara, everyone[chara], outfit)
            except Exception as error:   # one broken uma never stops the rest
                print("%d: failed (%s)" % (chara, error))
