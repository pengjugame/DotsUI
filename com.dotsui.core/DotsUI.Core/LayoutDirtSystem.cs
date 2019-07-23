﻿using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using DotsUI.Profiling;
using TMPro;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TextCore;

namespace DotsUI.Core
{
    [UpdateInGroup(typeof(BeforeRectTransformUpdateGroup))]
    [UpdateAfter(typeof(ParentSystem))]
    public class LayoutDirtSystem : JobComponentSystem
    {
        [UpdateInGroup(typeof(BeforeRectTransformUpdateGroup))][UpdateAfter(typeof(LayoutDirtSystem))]
        private class SystemBarrier : EntityCommandBufferSystem
        {
        }

        private EntityCommandBufferSystem m_Barrier;
        //[BurstCompile]
        private struct MarkDirtyCanvases : IJobChunk
        {
            [ReadOnly] public ArchetypeChunkEntityType EntityType;
            [ReadOnly] public ArchetypeChunkComponentType<DirtyElementFlag> DirtyElementType;
            [ReadOnly] public ArchetypeChunkComponentType<UpdateElementColor> UpdateColorType;
            [ReadOnly] public ComponentDataFromEntity<UIParent> ParentFromEntity;
            [ReadOnly] public ComponentType DirtyElementComponent;
            [ReadOnly] public ComponentType UpdateColorComponent;

            public EntityCommandBuffer.Concurrent CommandBuff;

            public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
            {
                var entities = chunk.GetNativeArray(EntityType);
                if (chunk.Has(DirtyElementType))
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var entity = entities[i];
                        var root = GetRootRecursive(entity);
                        if(root != Entity.Null)
                        {
                            CommandBuff.AddComponent(chunkIndex, root, new RebuildCanvasHierarchyFlag());
                            CommandBuff.RemoveComponent(chunkIndex, entity, DirtyElementComponent);
                        }
                    }
                }
                else if (chunk.Has(UpdateColorType))    // If element is dirty, there is no need to set updatecolor, because color will be updated anyway
                {
                    for (int i = 0; i < chunk.Count; i++)
                    {
                        var entity = entities[i];
                        var root = GetRootRecursive(entity);
                        if (root != Entity.Null)
                            CommandBuff.AddComponent(chunkIndex, root, new UpdateCanvasVerticesFlag());
                    }
                }

            }

            public Entity GetRootRecursive(Entity entity)
            {
                if (ParentFromEntity.Exists(entity))
                    return GetRootRecursive(ParentFromEntity[entity].Value);
                return entity;
            }
        }
        private EntityQuery m_DirtyElements;

        protected override void OnDestroyManager()
        {
        }

        protected override void OnCreateManager()
        {
            m_Barrier = World.GetOrCreateSystem<SystemBarrier>();
            m_DirtyElements = GetEntityQuery(new EntityQueryDesc
            {
                Any = new ComponentType[]
                {
                    ComponentType.ReadOnly<DirtyElementFlag>(),
                    ComponentType.ReadOnly<UpdateElementColor>(), 
                }
            });
        }

        protected override JobHandle OnUpdate(JobHandle inputDeps)
        {
            var entityCommandBuffer = m_Barrier.CreateCommandBuffer().ToConcurrent();
            var dirtType = ComponentType.ReadOnly<DirtyElementFlag>();
            var parentFromEntity = GetComponentDataFromEntity<UIParent>(true);
            var entityType = GetArchetypeChunkEntityType();
            var job = new MarkDirtyCanvases()
            {
                CommandBuff = entityCommandBuffer,
                DirtyElementType = GetArchetypeChunkComponentType<DirtyElementFlag>(true),
                DirtyElementComponent = dirtType,
                UpdateColorType = GetArchetypeChunkComponentType<UpdateElementColor>(true),
                UpdateColorComponent = ComponentType.ReadOnly<UpdateElementColor>(),
                ParentFromEntity = parentFromEntity,
                EntityType = entityType
            };
            inputDeps = job.Schedule(m_DirtyElements, inputDeps);
            inputDeps.Complete();
            return inputDeps;
        }
    }
}