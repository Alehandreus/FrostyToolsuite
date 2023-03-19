import os
import unreal
import re
from math import fabs
import json

export_path = "C:"
ins_path = ins_path + "/Maps/instances.json"
mat_path = ins_path + "/Maps/materials.json"

asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
mel = unreal.MaterialEditingLibrary
eal = unreal.EditorAssetLibrary


# see https://www.youtube.com/@AlexQuevillonEn for this function
def buildImportTask(filename, destination_path):
    task = unreal.AssetImportTask()
    task.set_editor_property("automated", True)
    task.set_editor_property("destination_name", "")
    task.set_editor_property("destination_path", destination_path)
    task.set_editor_property("filename", filename)
    task.set_editor_property("replace_existing", True)
    task.set_editor_property("save", True)
    return task


def executeImportTasks(tasks):
    asset_tools.import_asset_tasks(tasks)


def import_meshes():
    ins_file = open(ins_path, "rt", encoding="utf-8")
    ins_data = json.load(ins_file)

    tasks = []
    for mesh in ins_data:
        task = buildImportTask(f"{export_path}/Maps/Assets/{mesh['path']}/{mesh['filename']}.fbx", f"/Game/Maps/{mesh['path']}/{mesh['filename']}")
        tasks.append(task)

    executeImportTasks(tasks)


def import_textures():
    ins_file = open(ins_path, "rt", encoding="utf-8")
    ins_data = json.load(ins_file)

    tasks = []
    for mesh in ins_data:
        for section in mesh["sections"]:
            for texture in section["texture_parameters"]:
                task = buildImportTask(f"{export_path}/Maps/Assets/{texture['path']}/{texture['filename']}.png", f"/Game/Maps/{texture['path']}")
                tasks.append(task)
    executeImportTasks(tasks)


def place_meshes():
    ins_file = open(ins_path, "rt", encoding="utf-8")
    ins_data = json.load(ins_file)

    total_frames = len(ins_data)
    text_label = "Placing meshes"
    with unreal.ScopedSlowTask(total_frames, text_label) as task:
        task.make_dialog(True)
        for mesh in ins_data:
            if task.should_cancel():
                break
            task.enter_progress_frame(1)

            for instance in mesh["instances"]:
                if instance["rx"] == 180.0 and instance["ry"] == 180.0:
                    instance["rz"] = -instance["rz"]
                if len(mesh["sections"]) == 1:
                    obj = unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}")
                    if obj is None:
                        continue
                    actor_location = unreal.Vector(instance["tx"] * 70, instance["ty"] * 70, instance["tz"] * 70)
                    actor_rotation = unreal.Rotator(instance["rx"], instance["ry"], instance["rz"])
                    actor_scale = unreal.Vector(instance["sx"], instance["sy"], instance["sz"])
                    actor = unreal.EditorLevelLibrary.spawn_actor_from_object(obj, actor_location, actor_rotation)
                    actor.set_actor_scale3d(actor_scale)
                    actor.set_folder_path(f"Maps/{mesh['filename']}")
                else:
                    for section in mesh["sections"]:
                        obj = unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}_{section['name']}")
                        if obj is None:
                            continue
                        actor_location = unreal.Vector(instance["tx"] * 70, instance["ty"] * 70, instance["tz"] * 70)
                        actor_rotation = unreal.Rotator(instance["rx"], instance["ry"], instance["rz"])
                        actor_scale = unreal.Vector(instance["sx"], instance["sy"], instance["sz"])
                        actor = unreal.EditorLevelLibrary.spawn_actor_from_object(obj, actor_location, actor_rotation)
                        actor.set_actor_scale3d(actor_scale)
                        actor.set_folder_path(f"Maps/{mesh['filename']}")


def create_materials():
    mat_file = open(mat_path, "rt", encoding="utf-8")
    mat_data = json.load(mat_file)

    total_frames = len(mat_data)
    text_label = "Creating materials"
    with unreal.ScopedSlowTask(total_frames, text_label) as task:
        task.make_dialog(True)
        for material in mat_data:
            if task.should_cancel():
                break
            task.enter_progress_frame(1)

            if (material_asset := unreal.load_asset(f"/Game/Maps/Materials/{material['filename']}")) is None:
                material_asset = asset_tools.create_asset(material["filename"], f"/Game/Maps/Materials/", unreal.Material, unreal.MaterialFactoryNew())

            mel.delete_all_material_expressions(material_asset)
            mel.create_material_expression(material_asset, unreal.MaterialExpressionConstant3Vector, -600, -300)

            for j in range(len(material["texture_parameters"])):
                expr = mel.create_material_expression(material_asset, unreal.MaterialExpressionTextureSampleParameter2D, -600, j * 300)
                expr.set_editor_property("parameter_name", material["texture_parameters"][j])

            for j in range(len(material["vector_parameters"])):
                expr = mel.create_material_expression(material_asset, unreal.MaterialExpressionVectorParameter, -1000, j * 300)
                expr.set_editor_property("parameter_name", material["vector_parameters"][j])

            eal.save_asset(f"/Game/Maps/Materials/{material['filename']}", only_if_is_dirty=False)


def assign_materials():
    ins_file = open(ins_path, "rt", encoding="utf-8")
    ins_data = json.load(ins_file)

    total_frames = len(ins_data)
    text_label = "Assigning materials"
    with unreal.ScopedSlowTask(total_frames, text_label) as task:
        task.make_dialog(True)
        for mesh in ins_data:
            if task.should_cancel():
                break
            task.enter_progress_frame(1)

            if len(mesh["sections"]) == 0:
                continue

            elif len(mesh["sections"]) == 1:
                mesh_asset = unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}")
                if type(mesh_asset) != unreal.StaticMesh:
                    continue

                section = mesh['sections'][0]
                section["name"] = section["name"].replace(" - ", "")

                if (material_instance_asset := unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{section['name']}")) is None:
                    material_instance_asset = asset_tools.create_asset(section['name'], f"/Game/Maps/{mesh['path']}/{mesh['filename']}/", unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())

                material_asset = unreal.load_asset(f"/Game/Maps/Materials/{os.path.basename(section['material_fullname'])}")
                mel.set_material_instance_parent(material_instance_asset, material_asset)

                for texture_param in section["texture_parameters"]:
                    texture_asset = unreal.load_asset(f"/Game/Maps/{texture_param['path']}/{texture_param['filename']}")
                    mel.set_material_instance_texture_parameter_value(material_instance_asset, texture_param["name"], texture_asset)

                for vector_param in section["vector_parameters"]:
                    u_vector = unreal.LinearColor(vector_param["x"], vector_param["y"], vector_param["z"], vector_param["w"])
                    mel.set_material_instance_vector_parameter_value(material_instance_asset, vector_param["name"], u_vector)

                mesh_asset.set_material(0, material_instance_asset)
                eal.save_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{section['name']}", only_if_is_dirty=False)
                eal.save_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}", only_if_is_dirty=False)

            else:
                for section in mesh["sections"]:
                    mesh_asset = unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}_{section['name']}")
                    if type(mesh_asset) != unreal.StaticMesh:
                        continue

                    if (material_instance_asset := unreal.load_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{section['name']}")) is None:
                        material_instance_asset = asset_tools.create_asset(section['name'], f"/Game/Maps/{mesh['path']}/{mesh['filename']}/", unreal.MaterialInstanceConstant, unreal.MaterialInstanceConstantFactoryNew())

                    material_asset = unreal.load_asset(f"/Game/Maps/Materials/{os.path.basename(section['material_fullname'])}")
                    mel.set_material_instance_parent(material_instance_asset, material_asset)

                    for texture_param in section["texture_parameters"]:
                        texture_asset = unreal.load_asset(f"/Game/Maps/{texture_param['path']}/{texture_param['filename']}")
                        mel.set_material_instance_texture_parameter_value(material_instance_asset, texture_param["name"], texture_asset)

                    for vector_param in section["vector_parameters"]:
                        u_vector = unreal.LinearColor(vector_param["x"], vector_param["y"], vector_param["z"], vector_param["w"])
                        mel.set_material_instance_vector_parameter_value(material_instance_asset, vector_param["name"], u_vector)

                    mesh_asset.set_material(0, material_instance_asset)
                    eal.save_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{section['name']}", only_if_is_dirty=False)
                    eal.save_asset(f"/Game/Maps/{mesh['path']}/{mesh['filename']}/{mesh['filename']}_{section['name']}", only_if_is_dirty=False)


def clean_mat_file():
    mat_file = open(mat_path, "rt", encoding="utf-8")
    mat_data = json.load(mat_file)

    new_mat_data = dict()

    for material in mat_data:
        key = (material['path'], material['filename'])
        v_params = set(material["vector_parameters"])
        t_params = set(material["texture_parameters"])
        if key in new_mat_data.keys():
            new_mat_data[key]["vector_parameters"] |= v_params
            new_mat_data[key]["texture_parameters"] |= t_params
        else:
            new_mat_data[key] = dict()
            new_mat_data[key]["vector_parameters"] = v_params
            new_mat_data[key]["texture_parameters"] = t_params

    new_mat_data = [
        {"path": key[0],
         "filename": key[1],
         "vector_parameters": list(value["vector_parameters"]),
         "texture_parameters": list(value["texture_parameters"]),
         } for (key, value) in new_mat_data.items()]

    mat_file = open(mat_path, "wt", encoding="utf-8")
    json.dump(new_mat_data, mat_file)