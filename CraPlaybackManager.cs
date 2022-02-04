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
                return CraHandle.Invalid;
            }

            CraClipData data = ClipData.Get(clipHandle.Internal);
            data.FPS = clip.Fps;
            data.FrameCount = clip.FrameCount;
            data.FrameOffset = BakedClipTransforms.GetNumAllocated();
            ClipData.Set(clipHandle.Internal, in data);

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
            CraClipData data = ClipData.Get(clip.Internal);
            return data.FrameCount / data.FPS;
        }

        public CraHandle Player_New()
        {
            if (Instance.PlayerCounter + 1 >= (Instance.PlayerData.GetCapacity() * 4))
            {
                Debug.LogError($"Limit of {Instance.PlayerData.GetCapacity() * 4} Players reached!");
                return CraHandle.Invalid;
            }

            int dataIdx = Instance.PlayerCounter / 4;
            int subIdex = Instance.PlayerCounter % 4;

            if (dataIdx >= Instance.PlayerData.GetNumAllocated())
            {
                int newIdx = Instance.PlayerData.Alloc();
                Debug.Assert(newIdx == dataIdx);
            }

            CraPlayerData data = Instance.PlayerData.Get(dataIdx);
            data.ClipIndex[subIdex] = -1;
            data.PlaybackSpeed[subIdex] = 1f;
            data.TransitionProgress[subIdex] = 1f;
            Instance.PlayerData.Set(dataIdx, in data);

            Debug.Assert(PlayerAssignedBones.Count == Instance.PlayerCounter);

            PlayerAssignedBones.Add(new List<int>());
            return new CraHandle(Instance.PlayerCounter++);
        }

        public void Player_SetClip(CraHandle player, CraHandle clip)
        {
            Debug.Assert(player.IsValid());
            Debug.Assert(clip.IsValid());

            (CraPlayerData data, int subIdex) = PlayerGet(player);
            data.ClipIndex[subIdex] = clip.Internal;
            PlayerSet(player, in data);
        }

        public CraHandle Player_GetClip(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return new CraHandle(data.ClipIndex[subIdex]);
        }

        public void Player_CaptureBones(CraHandle player)
        {
            List<int> boneIndices = PlayerAssignedBones[player.Internal];
            for (int i = 0; i < boneIndices.Count; ++i)
            {
                int boneIdx = boneIndices[i];

                (CraPlayerData playerData, int subIdex) = PlayerGet(player);
                CraHandle clip = new CraHandle(playerData.ClipIndex[subIdex]);

                // Let the bone point to our Player and Clip
                CraBoneData boneData = BoneData.Get(boneIdx);
                boneData.PlayerIndex = player.Internal;
                boneData.ClipBoneIndex = BonePlayerClipIndices[boneIdx][clip];
                BoneData.Set(boneIdx, in boneData);
            }
        }

        internal static void Player_Reset(NativeArray<CraPlayerData> playerData, CraHandle player)
        {
            PlayerGet(playerData, player, out CraPlayerData data, out int subIdex);
            data.Finished[subIdex] = false;
            data.Playback[subIdex] = 0f;
            data.IsPlaying[subIdex] = false;
            PlayerSet(playerData, player, in data);
        }

        public void Player_Reset(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            data.Finished[subIdex] = false;
            data.Playback[subIdex] = 0f;
            data.IsPlaying[subIdex] = false;
            PlayerSet(player, in data);
        }

        public bool Player_IsPlaying(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return data.IsPlaying[subIdex];
        }

        public float Player_GetPlayback(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return data.Playback[subIdex];
        }

        internal static void Player_Play(NativeArray<CraPlayerData> playerData, CraHandle player, float transitionTime = 0.0f)
        {
            PlayerGet(playerData, player, out CraPlayerData data, out int subIdex);
            //if (data.PlaybackSpeed[subIdex] > 0f)
            //{
            //    data.Playback[subIdex] = .001f;
            //}
            //else
            //{
            //    data.Playback[subIdex] = data.Duration[subIdex] - .001f;
            //}
            data.IsPlaying[subIdex] = true;
            data.TransitionTime[subIdex] = transitionTime;
            data.TransitionProgress[subIdex] = transitionTime > 0.0f ? 0f : 1f;
            PlayerSet(playerData, player, in data);
        }

        public void Player_Play(CraHandle player, float transitionTime = 0.0f)
        {
            Player_Play(Instance.PlayerData.GetMemoryBuffer(), player, transitionTime);
        }

        public float Player_GetPlaybackSpeed(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return data.PlaybackSpeed[subIdex];
        }

        public void Player_SetPlaybackSpeed(CraHandle player, float speed)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            data.PlaybackSpeed[subIdex] = speed;//Mathf.Max(speed, 0.01f);
            PlayerSet(player, in data);
        }

        public void Player_ResetTransition(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            data.TransitionProgress[subIdex] = 0f;
            PlayerSet(player, in data);
        }

        public bool Player_IsLooping(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return data.Looping[subIdex];
        }

        public void Player_SetLooping(CraHandle player, bool loop)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            data.Looping[subIdex] = loop;
            PlayerSet(player, in data);
        }

        internal static bool Player_IsFinished(NativeArray<CraPlayerData> playerData, CraHandle player)
        {
            PlayerGet(playerData, player, out CraPlayerData data, out int subIdex);
            return data.Finished[subIdex];
        }

        public bool Player_IsFinished(CraHandle player)
        {
            (CraPlayerData data, int subIdex) = PlayerGet(player);
            return data.Finished[subIdex];
        }

        public void Player_Assign(CraHandle player, Transform root, CraMask? mask = null)
        {
            Debug.Assert(root != null);

            if (Instance.Settings.BoneHashFunction == null)
            {
                throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
            }

            List<int> assignedBones = PlayerAssignedBones[player.Internal];
            if (assignedBones.Count > 0)
            {
                Debug.LogWarning($"Player {player.Internal} already has bones assigned!");
                return;
            }

            (CraPlayerData data, int subIdex) = PlayerGet(player);
            CraHandle clip = new CraHandle(data.ClipIndex[subIdex]);
            if (!clip.IsValid())
            {
                Debug.LogError($"Cannot assign Transform '{root.name}' to CraPlayer! No CraClip set!");
                return;
            }

            CraSourceClip srcClip = KnownClips[clip];
            PlayerAssignInternal(assignedBones, srcClip, clip, root, mask);

            data.TransitionProgress[subIdex] = 1f;
            PlayerSet(player, in data);

            Player_CaptureBones(player);
        }



        void PlayerAssignInternal(List<int> assignedBones, CraSourceClip srcClip, CraHandle clip, Transform current, CraMask? mask = null, bool maskedChild = false)
        {
            void AddBone(int clipBoneIdx)
            {
                int allocIdx;
                if (!KnownBoneIndices.TryGetValue(current, out allocIdx))
                {
                    allocIdx = BoneData.Alloc();
                    KnownBoneIndices.Add(current, allocIdx);
                    BonePlayerClipIndices.Add(new Dictionary<CraHandle, int>());
                    Bones.Add(current);

                    Debug.Assert(KnownBoneIndices.Count == BoneData.GetNumAllocated());
                    Debug.Assert(BonePlayerClipIndices.Count == BoneData.GetNumAllocated());
                    Debug.Assert(Bones.length == BoneData.GetNumAllocated());
                }

                BonePlayerClipIndices[allocIdx].Add(clip, clipBoneIdx);
                assignedBones.Add(allocIdx);
            }

            int boneHash = CraMain.Instance.Settings.BoneHashFunction(current.name);
            bool isMasked = false;
            if (srcClip.BoneHashToIdx.TryGetValue(boneHash, out int clipBoneIdx))
            {
                if (mask.HasValue)
                {
                    if (maskedChild || mask.Value.BoneHashes.Contains(boneHash))
                    {
                        AddBone(clipBoneIdx);
                        isMasked = mask.Value.MaskChildren;
                    }
                }
                else
                {
                    AddBone(clipBoneIdx);
                }
            }
            for (int i = 0; i < current.childCount; ++i)
            {
                PlayerAssignInternal(assignedBones, srcClip, clip, current.GetChild(i), mask, isMasked);
            }
        }

        static void PlayerGet(NativeArray<CraPlayerData> playerData, CraHandle player, out CraPlayerData outPlayer, out int outSubIndex)
        {
            int dataIdx = player.Internal / 4;
            outSubIndex = player.Internal % 4;
            outPlayer = playerData[dataIdx];
        }

        (CraPlayerData, int) PlayerGet(CraHandle player)
        {
            PlayerGet(Instance.PlayerData.GetMemoryBuffer(), player, out CraPlayerData outPlayer, out int outSubIndex);
            return (outPlayer, outSubIndex);
        }

        static void PlayerSet(NativeArray<CraPlayerData> playerData, CraHandle player, in CraPlayerData data)
        {
            int dataIdx = player.Internal / 4;
            Debug.Assert(playerData.IsCreated);
            Debug.Assert(dataIdx >= 0 && dataIdx < playerData.Length);
            playerData[dataIdx] = data;
        }

        void PlayerSet(CraHandle player, in CraPlayerData data)
        {
            int dataIdx = player.Internal / 4;
            Instance.PlayerData.Set(dataIdx, in data);
        }

        public void Clear()
        {
            Instance.PlayerData.Clear();
            Instance.PlayerCounter = 0;

            BoneData.Clear();
            Bones.SetTransforms(new Transform[] { });

            ClipData.Clear();
            BakedClipTransforms.Clear();

            KnownClipHandles.Clear();
            KnownClips.Clear();

            KnownBoneIndices.Clear();

            BonePlayerClipIndices.Clear();
            PlayerAssignedBones.Clear();
        }


        public void Destroy()
        {
            Debug.Log("Deleting all animator manager data");
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

        public void UpdateStatistics()
        {
            Instance.Statistics.PlayerData.MaxElements = Instance.PlayerData.GetCapacity();
            Instance.Statistics.PlayerData.MaxBytes = CraPlayerData.SIZE * (ulong)Instance.PlayerData.GetCapacity();

            Instance.Statistics.ClipData.MaxElements = ClipData.GetCapacity();
            Instance.Statistics.ClipData.MaxBytes = CraClipData.SIZE * (ulong)ClipData.GetCapacity();

            Instance.Statistics.BakedClipTransforms.MaxElements = BakedClipTransforms.GetCapacity();
            Instance.Statistics.BakedClipTransforms.MaxBytes = CraTransform.SIZE * (ulong)BakedClipTransforms.GetCapacity();

            Instance.Statistics.BoneData.MaxElements = BoneData.GetCapacity();
            Instance.Statistics.BoneData.MaxBytes = CraBoneData.SIZE * (ulong)BoneData.GetCapacity();

            Instance.Statistics.Bones.MaxElements = Bones.capacity;
            Instance.Statistics.Bones.MaxBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.capacity;


            Instance.Statistics.PlayerData.CurrentElements = Instance.PlayerData.GetNumAllocated();
            Instance.Statistics.PlayerData.CurrentBytes = CraPlayerData.SIZE * (ulong)Instance.PlayerData.GetNumAllocated();

            Instance.Statistics.ClipData.CurrentElements = ClipData.GetNumAllocated();
            Instance.Statistics.ClipData.CurrentBytes = CraClipData.SIZE * (ulong)ClipData.GetNumAllocated();

            Instance.Statistics.BakedClipTransforms.CurrentElements = BakedClipTransforms.GetNumAllocated();
            Instance.Statistics.BakedClipTransforms.CurrentBytes = CraTransform.SIZE * (ulong)BakedClipTransforms.GetNumAllocated();

            Instance.Statistics.BoneData.CurrentElements = BoneData.GetNumAllocated();
            Instance.Statistics.BoneData.CurrentBytes = CraBoneData.SIZE * (ulong)BoneData.GetNumAllocated();

            Instance.Statistics.Bones.CurrentElements = Bones.length;
            Instance.Statistics.Bones.CurrentBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.length;
        }
    }

    // Describes a clip (baked transfrom data) within the BakedClipTransforms NativeArray
    struct CraClipData
    {
        public float FPS;

        // Offset into BakedClipTransforms memory
        public int FrameOffset;
        public int FrameCount;

        // For Debug reasons only
        public const ulong SIZE =
            sizeof(float) +
            sizeof(int) +
            sizeof(int);
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

        // For Debug reasons only
        public const ulong SIZE =
            sizeof(int) +
            sizeof(int);
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

            //player.Playback += DeltaTime * player.PlaybackSpeed;
            //bool4 end = player.Playback >= player.Duration;

            player.TransitionProgress = math.clamp(player.TransitionProgress + DeltaTime / player.TransitionTime, float4.zero, new float4(1f, 1f, 1f, 1f));

            // TODO: This is BAAAAAAAAAAD
            for (int i = 0; i < 4; ++i)
            {
                if (!player.IsPlaying[i])
                {
                    continue;
                }

                CraClipData clip = ClipData[player.ClipIndex[i]];
                float duration = clip.FrameCount / clip.FPS;
                player.Playback[i] += DeltaTime * player.PlaybackSpeed[i];

                if (player.PlaybackSpeed[i] > 0f)
                {
                    if (player.Playback[i] >= duration)
                    {
                        if (!player.Looping[i])
                        {
                            player.Playback[i] = duration - 0.001f;
                            player.IsPlaying[i] = false;
                            player.Finished[i] = true;
                        }
                        else
                        {
                            player.Playback[i] = 0;
                            player.FrameIndex[i] = 0;
                            player.Finished[i] = false;
                        }
                    }
                    else
                    {
                        player.FrameIndex[i] = (int)math.floor(clip.FPS * player.Playback[i]);
                    }
                }
                else
                {
                    if (player.Playback[i] <= 0f)
                    {
                        if (!player.Looping[i])
                        {
                            player.Playback[i] = 0.001f;
                            player.IsPlaying[i] = false;
                            player.Finished[i] = true;
                        }
                        else
                        {
                            player.Playback[i] = duration;
                            player.FrameIndex[i] = (int)math.floor(clip.FPS * duration);
                            player.Finished[i] = false;
                        }
                    }
                    else
                    {
                        player.FrameIndex[i] = (int)math.floor(clip.FPS * player.Playback[i]);
                    }
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

            int playerMemIdx = playerIdx / 4;
            int playerSubIdx = playerIdx % 4;

            CraPlayerData player = PlayerData[playerMemIdx];
            if (!player.IsPlaying[playerSubIdx])
            {
                return;
            }

            int clipIdx = player.ClipIndex[playerSubIdx];
            int clipFrameCount = ClipData[clipIdx].FrameCount;
            int clipFrameOffset = ClipData[clipIdx].FrameOffset;
            int localFrameIndex = player.FrameIndex[playerSubIdx];

            float transition = player.TransitionProgress[playerSubIdx];
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
        public int4 ClipIndex;
        public bool4 Looping;
        public bool4 IsPlaying;
        public float4 PlaybackSpeed;
        public float4 Playback;
        public float4 TransitionTime;
        public float4 TransitionProgress;

        // get only
        public bool4 Finished;
        public int4 FrameIndex;

        // For Debug reasons only
        public const ulong SIZE =
            sizeof(int) * 4 +
            sizeof(bool) * 4 +
            sizeof(bool) * 4 +
            sizeof(float) * 4 +
            sizeof(float) * 4 +
            sizeof(float) * 4 +

            sizeof(bool) * 4 +
            sizeof(int) * 4;
    }
}