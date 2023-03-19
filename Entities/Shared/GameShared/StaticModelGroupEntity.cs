using Frosty.Core;
using Frosty.Core.Viewport;
using LevelEditorPlugin.Editors;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using SharpDX;
using LevelEditorPlugin.Render;
using LevelEditorPlugin.Render.Proxies;
using LevelEditorPlugin.Managers;
using System.IO;
using Frosty.Core.Managers;
using FrostySdk.Attributes;
using FrostySdk.Ebx;
using FrostySdk.IO;
using LevelEditorPlugin.Data;
using MeshVariationDatabase = LevelEditorPlugin.Assets.MeshVariationDatabase;
using System.Diagnostics;
using FrostySdk.Managers.Entries;
using MeshSetPlugin;
using Frosty.Core.Windows;
using Bookmarks = Frosty.Core.Bookmarks;
using LevelEditorPlugin.Properties;
using SharpDX.Direct3D11;
using MeshSetPlugin.Resources;
using System.Windows.Media.Media3D;
using TexturePlugin;
using LevelEditorPlugin.Assets;
using LevelEditorPlugin.ExportData;
using System.Text.Json;

namespace LevelEditorPlugin.Entities
{
    public class StaticModelGroupElementMoveEntityUndoUnit : IUndoUnit
    {
        public string Text => "Move Entities";

        private StaticModelGroupElementEntity entity;
        private Matrix originalTransform;

        public StaticModelGroupElementMoveEntityUndoUnit(StaticModelGroupElementEntity inEntity, Matrix inOriginalTransform)
        {
            entity = inEntity;
            originalTransform = inOriginalTransform;
        }

        public void Do()
        {
            (entity.Parent as StaticModelGroupEntity).UpdateData(entity);
        }

        public void Undo()
        {
            (entity.Parent as StaticModelGroupEntity).UndoData(entity);
            (entity as ISpatialEntity).SetTransform(originalTransform, suppressUpdate: true);
            entity.RequiresTransformUpdate = true;
        }
    }

    [EntityBinding(DataType = typeof(StaticModelGroupElementEntityData))]
    public class StaticModelGroupElementEntity : Entity, IEntityData<StaticModelGroupElementEntityData>, ISpatialEntity
    {
        public StaticModelGroupElementEntityData Data => data as StaticModelGroupElementEntityData;
        public override bool RequiresTransformUpdate 
        {
            get => base.RequiresTransformUpdate;
            set
            {
                entity.RequiresTransformUpdate = value;
            }
        }
        public override string DisplayName => Path.GetFileName(blueprint.Name);

        private Assets.ObjectBlueprint blueprint;
        private Entity entity;

        public StaticModelGroupElementEntity(StaticModelGroupElementEntityData inData, Entity inParent)
            : base(inData, inParent)
        {
            blueprint = LoadedAssetManager.Instance.LoadAsset<Assets.ObjectBlueprint>(new FrostySdk.IO.EbxImportReference() { FileGuid = Data.InternalBlueprint.External.FileGuid, ClassGuid = Guid.Empty });
            entity = CreateEntity(blueprint.Data.Object.GetObjectAs<FrostySdk.Ebx.GameObjectData>());

            // For visibility in the property grid, will show the actual blueprint as opposed to the entity data
            Data.Blueprint = new FrostySdk.Ebx.PointerRef(new EbxImportReference() { FileGuid = blueprint.FileGuid, ClassGuid = blueprint.InstanceGuid });
        }

        public virtual Matrix GetTransform()
        {
            Matrix m = Matrix.Identity;
            if (parent != null && parent is ISpatialEntity)
                m = (parent as ISpatialEntity).GetTransform();
            return SharpDXUtils.FromLinearTransform(Data.Transform) * m;
        }

        public Matrix GetLocalTransform()
        {
            return SharpDXUtils.FromLinearTransform(Data.Transform);
        }

        public void SetTransform(Matrix m, bool suppressUpdate)
        {
            if (suppressUpdate)
            {
                if (!UndoManager.Instance.IsUndoing && UndoManager.Instance.PendingUndoUnit == null)
                {
                    UndoManager.Instance.PendingUndoUnit = new StaticModelGroupElementMoveEntityUndoUnit(this, GetLocalTransform());
                }
            }
            else
            {
                UndoManager.Instance.CommitUndo(UndoManager.Instance.PendingUndoUnit);
            }

            Data.Transform = MakeLinearTransform(m);
            NotifyEntityModified("Transform");
        }

        public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
        {
            entity.CreateRenderProxy(proxies, state);
        }

        public override void SetOwner(Entity newOwner)
        {
            base.SetOwner(newOwner);
            entity.SetOwner(newOwner);
        }

        public override void SetVisibility(bool newVisibility)
        {
            if (newVisibility != isVisible)
            {
                isVisible = newVisibility;
                entity.SetVisibility(newVisibility);
            }
        }

        public override void Destroy()
        {
            LoadedAssetManager.Instance.UnloadAsset(blueprint);
            entity.Destroy();
        }
    }    

    [EntityBinding(DataType = typeof(FrostySdk.Ebx.StaticModelGroupEntityData))]
    public class StaticModelGroupEntity : Entity, IEntityData<FrostySdk.Ebx.StaticModelGroupEntityData>, ISpatialReferenceEntity, ILayerEntity
    {
        public FrostySdk.Ebx.StaticModelGroupEntityData Data => data as FrostySdk.Ebx.StaticModelGroupEntityData;

        private Resources.HavokPhysicsData physicsData;
        private List<Entity> entities = new List<Entity>();

        public StaticModelGroupEntity(FrostySdk.Ebx.StaticModelGroupEntityData inData, Entity inParent)
            : base(inData, inParent)
        {
            physicsData = GetPhysicsData(inData);

            // All export happens here

            string export_path = LevelEditorPlugin.Editors.LevelEditor.export_path;

            int instCount = 0;
            int totalInstCount = 0;

            StreamWriter matsw = File.AppendText(export_path + "materials.json");
            StreamWriter inssw = File.AppendText(export_path + "instances.json");

            foreach (StaticModelGroupMemberData member in inData.MemberDatas)
            {
                EbxAssetEntry entryb = App.AssetManager.GetEbxEntry(member.MemberType.External.FileGuid);
                EbxAsset assetb = App.AssetManager.GetEbx(entryb);
                dynamic meshAssetb = (dynamic)assetb.RootObject;                

                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(meshAssetb.Object.Internal.Mesh.External.FileGuid);
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                dynamic meshAsset = (dynamic)asset.RootObject;

                string meshpath = export_path + "Assets/" + entry.Path + "/" + entry.Filename + ".fbx";
                System.IO.Directory.CreateDirectory(export_path + "Assets/" + entry.Path + "/");

                ulong resRid = meshAsset.MeshSetResource;
                ResAssetEntry rEntry = App.AssetManager.GetResEntry(resRid);
                MeshSetPlugin.Resources.MeshSet meshSet = App.AssetManager.GetResAs<MeshSetPlugin.Resources.MeshSet>(rEntry);
                string skeleton = "";
                FBXExporter exporter = new FBXExporter();
                exporter.ExportFBX((dynamic)asset.RootObject, meshpath, "2022", "1", false, true, skeleton, "fbx", meshSet);

                PointerRef pr = new PointerRef();
                MeshMaterialCollection mmc = new MeshMaterialCollection(asset, pr);

                IEnumerator<Frosty.Core.Viewport.MeshMaterial> enumerator = mmc.GetEnumerator();

                MeshExportData mesh_export_data = new MeshExportData(entry.Path, entry.Filename);

                int iii = 0;
                while (enumerator.MoveNext())
                {
                    Frosty.Core.Viewport.MeshMaterial material = enumerator.Current;

                    EbxAssetEntry shader = App.AssetManager.GetEbxEntry(material.Shader.External.FileGuid);
                    if (shader == null)
                    {
                        continue;
                    }

                    if (iii >= meshSet.Lods[0].Sections.Count || meshSet.Lods[0].Sections[iii].Name == "")
                    {
                        continue;
                    }

                    Section section = new Section(meshSet.Lods[0].Sections[iii++].Name, shader.Name);            

                    MaterialExportData material_export_data = new MaterialExportData(shader.Path, shader.Filename);

                    foreach (dynamic textureParameter in material.TextureParameters)
                    {
                        EbxAssetEntry tentry = App.AssetManager.GetEbxEntry(textureParameter.Value.External.FileGuid);
                        if (tentry == null)
                        {
                            break;
                        }
                        EbxAsset tasset = App.AssetManager.GetEbx(tentry);

                        string tpath = export_path + "Assets/" + tentry.Name + ".png";
                        System.IO.Directory.CreateDirectory(export_path + "Assets/" + tentry.Path + "/");

                        ulong tresRid = ((dynamic)tasset.RootObject).Resource;
                        FrostySdk.Resources.Texture texture = App.AssetManager.GetResAs<FrostySdk.Resources.Texture>(App.AssetManager.GetResEntry(tresRid));

                        TextureExporter texporter = new TextureExporter();
                        texporter.Export(texture, tpath, "*.png");

                        material_export_data.texture_parameters.Add(textureParameter.ParameterName);

                        section.texture_parameters.Add(new TextureParameter(textureParameter.ParameterName, tentry.Path, tentry.Filename));
                    }

                    foreach (dynamic vectorParameter in material.VectorParameters)
                    {
                        dynamic val = vectorParameter.Value;
                        section.vector_parameters.Add(new VectorParameter(vectorParameter.ParameterName, val.x, val.y, val.z, val.w));

                        material_export_data.vector_parameters.Add(vectorParameter.ParameterName);
                    }

                    mesh_export_data.sections.Add(section);

                    string matjson = JsonSerializer.Serialize(material_export_data);
                    matsw.Write(matjson);
                    matsw.Write(",");
                }

                for (int i = 0; i < member.InstanceCount; i++)
                {
                    StaticModelGroupElementEntityData entityData = new StaticModelGroupElementEntityData();
                    entityData.InternalBlueprint = member.MemberType;       

                    if (member.InstanceTransforms.Count > 0 || physicsData != null)
                    {
                        if (member.InstanceTransforms.Count > 0)
                        {
                            entityData.Transform = member.InstanceTransforms[i];
                            entityData.Index = totalInstCount;
                            entityData.HavokShapeType = "None";
                        }
                        else if (physicsData != null)
                        {
                            uint index = (uint)(member.PhysicsPartRange.First + i);

                            entityData.Transform = MakeLinearTransform(physicsData.GetTransform(instCount++));                       
                            entityData.Index = instCount - 1;
                            entityData.IsHavok = true;
                            entityData.HavokShapeType = physicsData.GetPhysicsShapeType(instCount - 1);
                        }

                        FrostySdk.Ebx.LinearTransform obj = entityData.Transform;
                        float tx, ty, tz, rx, ry, rz, sx, sy, sz;
                        if (obj.Rotation.x >= float.MaxValue)
                        {
                            // first time convert from raw matrix values

                            Matrix matrix = new Matrix(
                                    obj.right.x, obj.right.y, obj.right.z, 0.0f,
                                    obj.up.x, obj.up.y, obj.up.z, 0.0f,
                                    obj.forward.x, obj.forward.y, obj.forward.z, 0.0f,
                                    obj.trans.x, obj.trans.y, obj.trans.z, 1.0f
                                    );


                            matrix.Decompose(out Vector3 scale, out SharpDX.Quaternion rotation, out Vector3 translation);
                            Vector3 euler = SharpDXUtils.ExtractEulerAngles(matrix);

                            tx = translation.X;
                            ty = translation.Y;
                            tz = translation.Z;

                            sx = scale.X;
                            sy = scale.Y;
                            sz = scale.Z;

                            rx = -euler.Y;
                            ry = euler.X;
                            rz = euler.Z;
                        }
                        else
                        {
                            // grab values directly

                            tx = obj.Translate.x;
                            ty = obj.Translate.y;
                            tz = obj.Translate.z;

                            rx = obj.Rotation.x;
                            ry = obj.Rotation.y;
                            rz = obj.Rotation.z;

                            sx = obj.Scale.x;
                            sy = obj.Scale.y;
                            sz = obj.Scale.z;
                        }

                        mesh_export_data.instances.Add(new Transform(tx, tz, ty, ry, rz, rx, sx, sy, sz));

                        if (i < member.InstanceObjectVariation.Count)
                        {
                            Entity currentLayer = Parent;
                            entityData.ObjectVariationHash = member.InstanceObjectVariation[i];

                            if (entityData.ObjectVariationHash != 0)
                            {
                                while (!(currentLayer is SubWorldReferenceObject))
                                    currentLayer = currentLayer.Parent;

                                MeshVariationDatabase meshVariatationDb = (currentLayer as SubWorldReferenceObject).MeshVariationDatabase;
                                if (meshVariatationDb != null)
                                {
                                    entityData.ObjectVariation = meshVariatationDb.GetVariation(entityData.ObjectVariationHash);
                                }
                            }
                            
                        }
                        if (i < member.InstanceRenderingOverrides.Count) entityData.RenderingOverrides = member.InstanceRenderingOverrides[i];
                        if (i < member.InstanceRadiosityTypeOverride.Count) entityData.RadiosityTypeOverride = member.InstanceRadiosityTypeOverride[i];
                        if (i < member.InstanceTerrainShaderNodesEnable.Count) entityData.TerrainShaderNodesEnable = member.InstanceTerrainShaderNodesEnable[i];

                        totalInstCount++;
                    }

                    entities.Add(CreateEntity(entityData));                    
                }

                string insjson = JsonSerializer.Serialize(mesh_export_data);
                inssw.Write(insjson);
                inssw.Write(",");                
            }

            matsw.Close();
            inssw.Close();
        }

        public virtual Matrix GetTransform()
        {
            Matrix m = Matrix.Identity;
            if (parent != null && parent is ISpatialEntity)
                m = (parent as ISpatialEntity).GetTransform();
            return m;
        }

        public Matrix GetLocalTransform()
        {
            return Matrix.Identity;
        }

        public void SetTransform(Matrix m, bool suppressUpdate)
        {
            // do nothing
        }

        public void UpdateData(StaticModelGroupElementEntity entity)
        {
            if (entity.Data.IsHavok)
            {
                physicsData.UpdateData(entity.Data.Index, entity.GetLocalTransform());
            }
            else
            {
                int instanceIndex = 0;
                bool ebxNeedsUpdating = false;

                foreach (StaticModelGroupMemberData member in Data.MemberDatas)
                {
                    for (int i = 0; i < member.InstanceCount; i++)
                    {
                        if (member.InstanceTransforms.Count > 0)
                        {
                            if (member.InstanceTransforms.Count > 0)
                            {
                                if (instanceIndex == entity.Data.Index)
                                {
                                    member.InstanceTransforms[i] = MakeLinearTransform(entity.GetLocalTransform());
                                    ebxNeedsUpdating = true;
                                }
                            }
                        }

                        instanceIndex++;
                    }
                }

                if (ebxNeedsUpdating)
                {
                    LoadedAssetManager.Instance.UpdateAsset((Owner.Parent as ReferenceObject).Blueprint);
                }
            }
        }

        public void UndoData(StaticModelGroupElementEntity entity)
        {
            if (entity.Data.IsHavok)
            {
                physicsData.UndoData(entity.Data.Index, entity.GetLocalTransform());
            }
            else
            {
                int instanceIndex = 0;
                bool ebxNeedsUpdating = false;

                foreach (StaticModelGroupMemberData member in Data.MemberDatas)
                {
                    for (int i = 0; i < member.InstanceCount; i++)
                    {
                        if (member.InstanceTransforms.Count > 0)
                        {
                            if (member.InstanceTransforms.Count > 0)
                            {
                                if (instanceIndex == entity.Data.Index)
                                {
                                    member.InstanceTransforms[i] = MakeLinearTransform(entity.GetLocalTransform());
                                    ebxNeedsUpdating = true;
                                }
                            }
                        }

                        instanceIndex++;
                    }
                }

                if (ebxNeedsUpdating)
                {
                    LoadedAssetManager.Instance.UndoUpdate((Owner.Parent as ReferenceObject).Blueprint);
                }
            }
        }

        public Layers.SceneLayer GetLayer()
        {
            Layers.SceneLayer layer = new Layers.SceneLayer(this, "static_instances", new SharpDX.Color4(0.0f, 0.0f, 0.5f, 1.0f));
            foreach (Entity entity in entities)
            {
                layer.AddEntity(entity);
                entity.SetOwner(entity);
            }

            return layer;
        }

        public override void SetOwner(Entity newOwner)
        {
            base.SetOwner(newOwner);
            foreach (Entity entity in entities)
                entity.SetOwner(newOwner);
        }

        public override void SetVisibility(bool newVisibility)
        {
            if (newVisibility != isVisible)
            {
                isVisible = newVisibility;
                foreach (Entity entity in entities)
                    entity.SetVisibility(newVisibility);
            }
        }

        public override void CreateRenderProxy(List<RenderProxy> proxies, RenderCreateState state)
        {
            foreach (Entity entity in entities)
                entity.CreateRenderProxy(proxies, state);
        }

        public override void Destroy()
        {
            foreach (Entity entity in entities)
                entity.Destroy();
        }

        private Resources.HavokPhysicsData GetPhysicsData(FrostySdk.Ebx.StaticModelGroupEntityData inData)
        {
            foreach (PointerRef component in inData.Components)
            {
                FrostySdk.Ebx.GameObjectData gameObjectData = component.GetObjectAs<FrostySdk.Ebx.GameObjectData>();
                if (gameObjectData is FrostySdk.Ebx.StaticModelGroupPhysicsComponentData)
                {
                    FrostySdk.Ebx.StaticModelGroupPhysicsComponentData physicsComponentData = (FrostySdk.Ebx.StaticModelGroupPhysicsComponentData)gameObjectData;
                    foreach (PointerRef body in physicsComponentData.PhysicsBodies)
                    {
                        FrostySdk.Ebx.GroupRigidBodyData bodyData = body.GetObjectAs<FrostySdk.Ebx.GroupRigidBodyData>();
                        FrostySdk.Ebx.GroupHavokAsset havokAsset = bodyData.Asset.GetObjectAs<FrostySdk.Ebx.GroupHavokAsset>();

                        if (havokAsset != null)
                        {
                            return App.AssetManager.GetResAs<Resources.HavokPhysicsData>(App.AssetManager.GetResEntry(havokAsset.Resource));
                        }
                    }
                }
            }

            return null;
        }
    }
}
