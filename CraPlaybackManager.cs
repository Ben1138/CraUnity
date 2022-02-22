using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;


public partial class CraMain
{
    public class CraPlaybackManager
    {
        // Player data memory

        // These two have the same length!
        CraBuffer<CraBoneData> BoneData;
        TransformAccessArray Bones;

        // Clip data memory
        CraBuffer<CraClipData> ClipData;
        CraBuffer<CraTransform> BakedClipTransforms;

        // int is an index pointing into ClipData
        Dictionary<CraSourceClip, CraHandle> KnownClipHandles = new Dictionary<CraSourceClip, CraHandle>();
        Dictionary<CraHandle, CraSourceClip> KnownClips = new Dictionary<CraHandle, CraSourceClip>();

        // Map from a given Unity Transform into BoneData & Bones
        Dictionary<Transform, int> KnownBoneIndices = new Dictionary<Transform, int>();

        // for each bone in our memory buffer, provides a map to retrieve the bone's local
        // bone index within a clip, i.e.     ClipHandle -> LocalBoneIndex
        List<Dictionary<CraHandle, int>> BonePlayerClipIndices = new List<Dictionary<CraHandle, int>>();

        // For each player, provide a list of bone indices into BoneData & Bones
        List<List<int>> PlayerAssignedBones = new List<List<int>>();


        internal CraPlaybackManager()
        {
            ClipData = new CraBuffer<CraClipData>(Instance.Settings.Clips);
            BakedClipTransforms = new CraBuffer<CraTransform>(Instance.Settings.ClipTransforms);
            BoneData = new CraBuffer<CraBoneData>(Instance.Settings.Bones);
            Bones = new TransformAccessArray(Instance.Settings.MaxBones);

            Instance.PlayerJob = new CraPlayJob();
            Instance.BoneJob = new CraBoneEvalJob();
        }

        public CraHandle Clip_New(CraSourceClip clip)
        {
            Debug.Assert(clip != null);

            if (KnownClipHandles.TryGetValue(clip, out CraHandle clipHandle))
            {
                return clipHandle;
            }

            clipHandle = new CraHandle(ClipData.Alloc());
            if (!clipHandle.IsValid())
            {
                Debug.LogError($"Allocation of new Clip failed!");
                return CraHandle.Invalid;
            }

            CraClipData data = ClipData.Get(clipHandle.Index);
            data.FPS = clip.Fps;
            data.FrameCount = clip.FrameCount;
            data.FrameOffset = BakedClipTransforms.GetNumAllocated();
            ClipData.Set(clipHandle.Index, in data);

            KnownClipHandles.Add(clip, clipHandle);
            KnownClips.Add(clipHandle, clip);

            for (int i = 0; i < clip.Bones.Length; ++i)
            {
                if (clip.Bones[i].Curve.BakedFrames == null)
                {
                    Debug.LogError($"Given clip '{clip.Name}' is not baked!");
                    return CraHandle.Invalid;
                }

                if (!BakedClipTransforms.AllocFrom(clip.Bones[i].Curve.BakedFrames))
                {
                    return CraHandle.Invalid;
                }
            }

            return clipHandle;
        }

        public float Clip_GetDuration(CraHandle clip)
        {
            CraClipData data = ClipData.Get(clip.Index);
            return data.FrameCount / data.FPS;
        }

        public int Clip_GetFrameCount(CraHandle clip)
        {
            CraClipData data = ClipData.Get(clip.Index);
            return data.FrameCount;
        }

        public float Clip_GetFPS(CraHandle clip)
        {
            CraClipData data = ClipData.Get(clip.Index);
            return data.FPS;
        }

        public CraHandle Player_New()
        {
            int newIdx = Instance.PlayerData.Alloc();
            if (newIdx < 0)
            {
                Debug.LogError($"Allocation of new Player failed!");
                return CraHandle.Invalid;
            }

            CraPlayerData data = Instance.PlayerData.Get(newIdx);
            data.ClipIndex = -1;
            data.PlaybackSpeed = 1f;
            data.TransitionProgress = 1f;
            Instance.PlayerData.Set(newIdx, in data);

            PlayerAssignedBones.Add(new List<int>());
            Debug.Assert(PlayerAssignedBones.Count == Instance.PlayerData.GetNumAllocated());

            return new CraHandle(newIdx);
        }

        public void Player_SetClip(CraHandle player, CraHandle clip)
        {
            Debug.Assert(player.IsValid());
            Debug.Assert(clip.IsValid());
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.ClipIndex = clip.Index;
            Instance.PlayerData.Set(player.Index, in data);

            var clipData = ClipData.Get(data.ClipIndex);
            Player_SetPlayRange(player, new CraPlayRange { MinTime = 0f, MaxTime = clipData.FrameCount / clipData.FPS });
        }

        public CraHandle Player_GetClip(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return new CraHandle(data.ClipIndex);
        }

        public void Player_CaptureBones(CraHandle player)
        {
            List<int> boneIndices = PlayerAssignedBones[player.Index];
            for (int i = 0; i < boneIndices.Count; ++i)
            {
                int boneIdx = boneIndices[i];

                CraPlayerData playerData = Instance.PlayerData.Get(player.Index);
                CraHandle clip = new CraHandle(playerData.ClipIndex);

                // Let the bone point to our Player and Clip
                CraBoneData boneData = BoneData.Get(boneIdx);
                boneData.PlayerIndex = player.Index;
                boneData.ClipBoneIndex = BonePlayerClipIndices[boneIdx][clip];
                BoneData.Set(boneIdx, in boneData);
            }
        }

        internal static void Player_Reset(NativeArray<CraPlayerData> playerData, CraHandle player)
        {
            CraPlayerData data = playerData[player.Index];
            data.Finished = false;
            data.Playback = data.PlaybackRange.MinTime;
            data.IsPlaying = false;
            playerData[player.Index] = data;
        }

        public void Player_Reset(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.Finished = false;
            data.Playback = data.PlaybackRange.MinTime;
            data.IsPlaying = false;
            Instance.PlayerData.Set(player.Index, in data);
        }

        public bool Player_IsPlaying(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.IsPlaying;
        }

        public void Player_SetPlayRange(CraHandle player, CraPlayRange range)
        {
            Debug.Assert(range.MinTime < range.MaxTime);

            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            if (data.ClipIndex < 0)
            {
                Debug.LogError($"Cannot set play range of clip {player.Index}, since it has no clip assigned!");
                return;
            }
            var clipData = ClipData.Get(data.ClipIndex);
            float clipDuration = clipData.FrameCount / clipData.FPS;
            range.MinTime = Mathf.Clamp(range.MinTime, 0f, clipDuration - (1f / clipData.FPS));
            range.MaxTime = Mathf.Clamp(range.MaxTime, range.MinTime + (1f / clipData.FPS), clipDuration);
            data.PlaybackRange = range;
            Instance.PlayerData.Set(player.Index, in data);
        }

        public CraPlayRange Player_GetPlayRange(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.PlaybackRange;
        }

        internal static void Player_Play(NativeArray<CraPlayerData> playerData, CraHandle player, float transitionTime = 0.0f)
        {
            CraPlayerData data = playerData[player.Index];
            //if (data.PlaybackSpeed[subIdex] > 0f)
            //{
            //    data.Playback[subIdex] = .001f;
            //}
            //else
            //{
            //    data.Playback[subIdex] = data.Duration[subIdex] - .001f;
            //}
            data.IsPlaying = true;
            data.TransitionTime = transitionTime;
            data.TransitionProgress = transitionTime > 0.0f ? 0f : 1f;
            playerData[player.Index] = data;
        }

        public void Player_SetTime(CraHandle player, float time)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.Playback = time;
            Instance.PlayerData.Set(player.Index, in data);
        }

        public float Player_GetTime(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.Playback;
        }

        public void Player_Play(CraHandle player, float transitionTime = 0.0f)
        {
            Player_Play(Instance.PlayerData.GetMemoryBuffer(), player, transitionTime);
        }

        public float Player_GetPlaybackSpeed(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.PlaybackSpeed;
        }

        public void Player_SetPlaybackSpeed(CraHandle player, float speed)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.PlaybackSpeed = speed;//Mathf.Max(speed, 0.01f);
            Instance.PlayerData.Set(player.Index, in data);
        }

        internal static void Player_SetPlaybackSpeed(NativeArray<CraPlayerData> playerData, CraHandle player, float speed)
        {
            CraPlayerData data = playerData[player.Index];
            data.PlaybackSpeed = speed;
            playerData[player.Index] = data;
        }

        public void Player_ResetTransition(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.TransitionProgress = 0f;
            Instance.PlayerData.Set(player.Index, in data);
        }

        public bool Player_IsLooping(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.Looping;
        }

        public void Player_SetLooping(CraHandle player, bool loop)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            data.Looping = loop;
            Instance.PlayerData.Set(player.Index, in data);
        }

        internal static bool Player_IsFinished(NativeArray<CraPlayerData> playerData, CraHandle player)
        {
            CraPlayerData data = playerData[player.Index];
            return data.Finished;
        }

        public bool Player_IsFinished(CraHandle player)
        {
            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            return data.Finished;
        }

        public void Player_Assign(CraHandle player, Transform root, CraMask? mask = null)
        {
            Debug.Assert(player.IsValid());
            Debug.Assert(root != null);

            if (Instance.Settings.BoneHashFunction == null)
            {
                throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
            }

            List<int> assignedBones = PlayerAssignedBones[player.Index];
            if (assignedBones.Count > 0)
            {
                assignedBones.Clear();
            }

            CraPlayerData data = Instance.PlayerData.Get(player.Index);
            CraHandle clip = new CraHandle(data.ClipIndex);
            if (!clip.IsValid())
            {
                Debug.LogError($"Cannot assign Transform '{root.name}' to CraPlayer! No CraClip set!");
                return;
            }

            CraSourceClip srcClip = KnownClips[clip];
            PlayerAssignInternal(assignedBones, srcClip, clip, root, mask);

            data.TransitionProgress = 1f;
            Instance.PlayerData.Set(player.Index, in data);

            Player_CaptureBones(player);
        }

        public int Player_GetAssignedBonesCount(CraHandle player)
        {
            Debug.Assert(player.IsValid());
            return PlayerAssignedBones[player.Index].Count;
        }

        void PlayerAssignInternal(List<int> assignedBones, CraSourceClip srcClip, CraHandle clip, Transform current, CraMask? mask = null, bool isMaskedChild = false)
        {
            void AddBone(int clipBoneIdx)
            {
                int allocIdx;
                if (!KnownBoneIndices.TryGetValue(current, out allocIdx))
                {
                    allocIdx = BoneData.Alloc();
                    if (allocIdx < 0)
                    {
                        Debug.LogError("Allocating new BoneData entry failed!");
                        return;
                    }

                    KnownBoneIndices.Add(current, allocIdx);
                    BonePlayerClipIndices.Add(new Dictionary<CraHandle, int>());
                    Bones.Add(current);

                    Debug.Assert(KnownBoneIndices.Count == BoneData.GetNumAllocated());
                    Debug.Assert(BonePlayerClipIndices.Count == BoneData.GetNumAllocated());
                    Debug.Assert(Bones.length == BoneData.GetNumAllocated());
                }

                if (BonePlayerClipIndices[allocIdx].ContainsKey(clip))
                {
                    BonePlayerClipIndices[allocIdx][clip] = clipBoneIdx;
                }
                else
                {
                    BonePlayerClipIndices[allocIdx].Add(clip, clipBoneIdx);
                }
                assignedBones.Add(allocIdx);
            }

            int boneHash = Instance.Settings.BoneHashFunction(current.name);
            if (srcClip.BoneHashToIdx.TryGetValue(boneHash, out int clipBoneIdx))
            {
                if (mask.HasValue)
                {
                    bool foundBone = mask.Value.BoneHashes.Contains(boneHash);
                    if (mask.Value.Operation == CraMaskOperation.Intersection)
                    {
                        if (isMaskedChild || foundBone)
                        {
                            AddBone(clipBoneIdx);
                            isMaskedChild = mask.Value.MaskChildren;
                        }
                    }
                    else if (mask.Value.Operation == CraMaskOperation.Difference)
                    {
                        if (isMaskedChild || foundBone)
                        {
                            isMaskedChild = mask.Value.MaskChildren;
                        }
                        else
                        {
                            AddBone(clipBoneIdx);
                        }
                    }
                }
                else
                {
                    AddBone(clipBoneIdx);
                }
            }
            for (int i = 0; i < current.childCount; ++i)
            {
                PlayerAssignInternal(assignedBones, srcClip, clip, current.GetChild(i), mask, isMaskedChild);
            }
        }

        public void Clear()
        {
            KnownClipHandles.Clear();
            KnownClips.Clear();
            KnownBoneIndices.Clear();
            BonePlayerClipIndices.Clear();
            PlayerAssignedBones.Clear();

            Instance.PlayerData.Clear();
            BoneData.Clear();
            Bones.SetTransforms(new Transform[] { });
            ClipData.Clear();
            BakedClipTransforms.Clear();
        }


        public void Destroy()
        {
            KnownClipHandles.Clear();
            KnownClips.Clear();
            KnownBoneIndices.Clear();
            BonePlayerClipIndices.Clear();
            PlayerAssignedBones.Clear();

            Instance.PlayerData.Destroy();
            ClipData.Destroy();
            BoneData.Destroy();
            Bones.Dispose();
            BakedClipTransforms.Destroy();
        }

        public JobHandle Schedule()
        {
            Instance.PlayerJob.DeltaTime = Time.deltaTime;
            Instance.PlayerJob.PlayerData = Instance.PlayerData.GetMemoryBuffer();
            Instance.PlayerJob.ClipData = ClipData.GetMemoryBuffer();

            Instance.BoneJob.PlayerData = Instance.PlayerData.GetMemoryBuffer();
            Instance.BoneJob.BoneData = BoneData.GetMemoryBuffer();
            Instance.BoneJob.ClipData = ClipData.GetMemoryBuffer();
            Instance.BoneJob.BakedClipTransforms = BakedClipTransforms.GetMemoryBuffer();

            JobHandle playerJob = Instance.PlayerJob.Schedule(Instance.PlayerData.GetNumAllocated(), 8);

            // Update ALL players FIRST before evaluating ALL bones!
            JobHandle boneJob = Instance.BoneJob.Schedule(Bones, playerJob);
            boneJob.Complete();

            return playerJob;
        }
#if UNITY_EDITOR
        public unsafe void UpdateStatistics()
        {
            Instance.Statistics.PlayerData.MaxElements = Instance.PlayerData.GetCapacity();
            Instance.Statistics.PlayerData.MaxBytes = (ulong)sizeof(CraPlayerData) * (ulong)Instance.PlayerData.GetCapacity();
            Instance.Statistics.PlayerData.CurrentElements = Instance.PlayerData.GetNumAllocated();
            Instance.Statistics.PlayerData.CurrentBytes = (ulong)sizeof(CraPlayerData) * (ulong)Instance.PlayerData.GetNumAllocated();

            Instance.Statistics.ClipData.MaxElements = ClipData.GetCapacity();
            Instance.Statistics.ClipData.MaxBytes = (ulong)sizeof(CraClipData) * (ulong)ClipData.GetCapacity();
            Instance.Statistics.ClipData.CurrentElements = ClipData.GetNumAllocated();
            Instance.Statistics.ClipData.CurrentBytes = (ulong)sizeof(CraClipData) * (ulong)ClipData.GetNumAllocated();

            Instance.Statistics.BakedClipTransforms.MaxElements = BakedClipTransforms.GetCapacity();
            Instance.Statistics.BakedClipTransforms.MaxBytes = (ulong)sizeof(CraTransform) * (ulong)BakedClipTransforms.GetCapacity();
            Instance.Statistics.BakedClipTransforms.CurrentElements = BakedClipTransforms.GetNumAllocated();
            Instance.Statistics.BakedClipTransforms.CurrentBytes = (ulong)sizeof(CraTransform) * (ulong)BakedClipTransforms.GetNumAllocated();

            Instance.Statistics.BoneData.MaxElements = BoneData.GetCapacity();
            Instance.Statistics.BoneData.MaxBytes = (ulong)sizeof(CraBoneData) * (ulong)BoneData.GetCapacity();
            Instance.Statistics.BoneData.CurrentElements = BoneData.GetNumAllocated();
            Instance.Statistics.BoneData.CurrentBytes = (ulong)sizeof(CraBoneData) * (ulong)BoneData.GetNumAllocated();

            Instance.Statistics.Bones.MaxElements = Bones.capacity;
            Instance.Statistics.Bones.MaxBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.capacity;
            Instance.Statistics.Bones.CurrentElements = Bones.length;
            Instance.Statistics.Bones.CurrentBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.length;

            // Mapping
            int elems = 0;
            ulong bytes = 0;

            // Dictionaries approximately have an overhead of 20 bytes per entry

            elems += KnownClipHandles.Count;
            bytes += (ulong)KnownClipHandles.Count * (ulong)(sizeof(CraHandle) + sizeof(ulong) + 20);

            elems += KnownClips.Count;
            bytes += (ulong)KnownClips.Count * (ulong)(sizeof(CraHandle) + sizeof(ulong) + 20);

            elems += KnownBoneIndices.Count;
            bytes += (ulong)KnownBoneIndices.Count * (ulong)(sizeof(ulong) + sizeof(int) + 20);

            bytes += (ulong)BonePlayerClipIndices.Count * (ulong)(sizeof(ulong));
            for (int i = 0; i < BonePlayerClipIndices.Count; ++i)
            {
                elems += BonePlayerClipIndices[i].Count;
                bytes += (ulong)BonePlayerClipIndices[i].Count * (ulong)(sizeof(CraHandle) + sizeof(int) + 20);
            }

            bytes += (ulong)PlayerAssignedBones.Count * (ulong)(sizeof(ulong));
            for (int i = 0; i < PlayerAssignedBones.Count; ++i)
            {
                elems += PlayerAssignedBones[i].Count;
                bytes += (ulong)PlayerAssignedBones[i].Count * (ulong)sizeof(int);
            }

            Instance.Statistics.Mapping.MaxElements = elems;
            Instance.Statistics.Mapping.MaxBytes = bytes;
            Instance.Statistics.Mapping.CurrentElements = elems;
            Instance.Statistics.Mapping.CurrentBytes = bytes;
        }
#endif
    }

    // Describes a clip (baked transfrom data) within the BakedClipTransforms NativeArray
    struct CraClipData
    {
        public float FPS;

        // Offset into BakedClipTransforms memory
        public int FrameOffset;
        public int FrameCount;
    }

    // Describes what player and clip this particular bone is assigned to
    struct CraBoneData
    {
        // Offset into Instance.PlayerData memory
        public int PlayerIndex;

        // When baking a clip, each bone transform curve ends
        // up with exactly the same number of frames, i.e.
        // FrameCount. Using this information, we can index
        // into BakedClipTransforms not only per Clip, but
        // also per Bone within a Clip
        public int ClipBoneIndex;
    }

    [BurstCompile]
    struct CraPlayJob : IJobParallelFor
    {
        [ReadOnly]
        public float DeltaTime;

        [ReadOnly]
        public NativeArray<CraClipData> ClipData;

        // Read + Write
        public NativeArray<CraPlayerData> PlayerData;

        public void Execute(int index)
        {
            CraPlayerData player = PlayerData[index];
            if (!player.IsPlaying)
            {
                return;
            }

            player.TransitionProgress = math.clamp(player.TransitionProgress + DeltaTime / player.TransitionTime, 0f, 1f);

            CraClipData clip = ClipData[player.ClipIndex];
            float playMin = player.PlaybackRange.MinTime;
            float playMax = player.PlaybackRange.MaxTime;
            player.Playback += DeltaTime * player.PlaybackSpeed;

            if (player.PlaybackSpeed > 0f)
            {
                if (player.Playback >= playMax)
                {
                    if (!player.Looping)
                    {
                        player.Playback = playMax - 0.001f;
                        player.IsPlaying = false;
                        player.Finished = true;
                    }
                    else
                    {
                        player.Playback = playMin;
                        player.FrameIndex = (int)(playMin * clip.FPS);
                        player.Finished = false;
                    }
                }
                else
                {
                    player.FrameIndex = (int)math.floor(clip.FPS * player.Playback);
                }
            }
            else
            {
                if (player.Playback <= 0f)
                {
                    if (!player.Looping)
                    {
                        player.Playback = playMin + 0.001f;
                        player.IsPlaying = false;
                        player.Finished = true;
                    }
                    else
                    {
                        player.Playback = playMax;
                        player.FrameIndex = (int)(playMax * clip.FPS);
                        player.Finished = false;
                    }
                }
                else
                {
                    player.FrameIndex = (int)math.floor(clip.FPS * player.Playback);
                }
            }

            PlayerData[index] = player;
        }
    }


    [BurstCompile]
    struct CraBoneEvalJob : IJobParallelForTransform
    {
        [ReadOnly]
        public NativeArray<CraPlayerData> PlayerData;

        [ReadOnly]
        public NativeArray<CraBoneData> BoneData;

        [ReadOnly]
        public NativeArray<CraClipData> ClipData;

        [ReadOnly]
        public NativeArray<CraTransform> BakedClipTransforms;


        public void Execute(int index, TransformAccess transform)
        {
            int playerIdx = BoneData[index].PlayerIndex;
            int boneIndex = BoneData[index].ClipBoneIndex;

            CraPlayerData player = PlayerData[playerIdx];
            if (!player.IsPlaying)
            {
                return;
            }

            int clipIdx = player.ClipIndex;
            int clipFrameCount = ClipData[clipIdx].FrameCount;
            int clipFrameOffset = ClipData[clipIdx].FrameOffset;
            int localFrameIndex = player.FrameIndex;

            float transition = player.TransitionProgress;
            int frameIndex = clipFrameOffset + (boneIndex * clipFrameCount) + localFrameIndex;

            CraTransform frameTransform = BakedClipTransforms[frameIndex];

            transform.localPosition = math.lerp(
                transform.localPosition,
                frameTransform.Position,
                transition
            );

            transform.localRotation = math.slerp(
                transform.localRotation,
                frameTransform.Rotation,
                transition
            );
        }
    }

    internal struct CraPlayerData
    {
        // setable
        public int ClipIndex;
        public bool Looping;
        public bool IsPlaying;
        public float PlaybackSpeed;
        public CraPlayRange PlaybackRange;
        public float Playback;
        public float TransitionTime;
        public float TransitionProgress;

        // get only
        public bool Finished;
        public int FrameIndex;
    }
}
