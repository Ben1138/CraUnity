using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;


public class CraPlaybackManager
{
    struct CraPlayerData
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
        public float4 Duration;
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
            sizeof(float) * 4 +
            sizeof(int) * 4;
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
        // Offset into PlayerData memory
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
                player.Playback[i] += DeltaTime * player.PlaybackSpeed[i];

                if (player.PlaybackSpeed[i] > 0f)
                {
                    if (player.Playback[i] >= player.Duration[i])
                    {
                        if (!player.Looping[i])
                        {
                            player.Playback[i] = player.Duration[i] - 0.001f;
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
                            player.Playback[i] = player.Duration[i];
                            player.FrameIndex[i] = (int)math.floor(clip.FPS * player.Duration[i]);
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

#if UNITY_EDITOR
    public CraStatistics Statistics { get; private set; } = new CraStatistics();
#endif

    // Player data memory
    CraBuffer<CraPlayerData> PlayerData;
    int PlayerCounter;

    // Bone data memory. These two arrays have the same length!
    CraBuffer<CraBoneData> BoneData;
    TransformAccessArray Bones;

    // Clip data memory
    CraBuffer<CraClipData> ClipData;
    CraBuffer<CraTransform> BakedClipTransforms;

    // Jobs
    CraPlayJob PlayerJob;
    CraBoneEvalJob BoneJob;

    // int is an index pointing into ClipData
    Dictionary<CraClip, int> KnownClipIndices = new Dictionary<CraClip, int>();
    Dictionary<int, CraClip> KnownClips = new Dictionary<int, CraClip>();

    // Map from a given Unity Transform into BoneData & Bones
    Dictionary<Transform, int> KnownBoneIndices = new Dictionary<Transform, int>();

    // for each bone in our memory buffer, provides a map to retrieve the bone's local
    // bone index within a clip, i.e.     ClipIndex -> LocalBoneIndex
    List<Dictionary<int, int>> BonePlayerClipIndices = new List<Dictionary<int, int>>();

    // For each player, provide a list of bone indices into BoneData & Bones
    List<List<int>> PlayerAssignedBones = new List<List<int>>();


    public CraPlaybackManager()
    {
        PlayerData = new CraBuffer<CraPlayerData>(CraMain.Instance.Settings.Players);
        ClipData = new CraBuffer<CraClipData>(CraMain.Instance.Settings.Clips);
        BakedClipTransforms = new CraBuffer<CraTransform>(CraMain.Instance.Settings.ClipTransforms);
        BoneData = new CraBuffer<CraBoneData>(CraMain.Instance.Settings.Bones);
        Bones = new TransformAccessArray(CraMain.Instance.Settings.MaxBones);

        PlayerJob = new CraPlayJob()
        {
            PlayerData = PlayerData.GetMemoryBuffer(),
            ClipData = ClipData.GetMemoryBuffer()
        };

        BoneJob = new CraBoneEvalJob()
        {
            PlayerData = PlayerData.GetMemoryBuffer(),
            BoneData = BoneData.GetMemoryBuffer(),
            ClipData = ClipData.GetMemoryBuffer(),
            BakedClipTransforms = BakedClipTransforms.GetMemoryBuffer()
        };
    }

    public CraHandle Player_New()
    {
        if (PlayerCounter + 1 >= (PlayerData.GetCapacity() * 4))
        {
            Debug.LogError($"Limit of {PlayerData.GetCapacity() * 4} Players reached!");
            return CraHandle.Invalid;
        }

        int dataIdx = PlayerCounter / 4;
        int subIdex = PlayerCounter % 4;

        if (dataIdx >= PlayerData.GetNumAllocated())
        {
            int newIdx = PlayerData.Alloc();
            Debug.Assert(newIdx == dataIdx);
        }

        CraPlayerData data = PlayerData.Get(dataIdx);
        data.ClipIndex[subIdex] = -1;
        data.PlaybackSpeed[subIdex] = 1f;
        data.TransitionProgress[subIdex] = 1f;
        PlayerData.Set(dataIdx, in data);

        Debug.Assert(PlayerAssignedBones.Count == PlayerCounter);

        PlayerAssignedBones.Add(new List<int>());
        return new CraHandle(PlayerCounter++);
    }

    public void Player_SetClip(CraHandle player, CraClip clip)
    {
        Debug.Assert(clip != null);

        (CraPlayerData data, int subIdex) = PlayerGet(player);
        int clipIdx = data.ClipIndex[subIdex];

        if (clipIdx >= 0)
        {
            Debug.LogWarning($"Player {player.Internal} already has clip {clipIdx} assigned!");
            return;
        }

        if (!KnownClipIndices.TryGetValue(clip, out clipIdx))
        {
            clipIdx = CopyClip(clip);
            if (clipIdx < 0)
            {
                Debug.LogError($"Setting clip {clip.Name} to Player {player.Internal} failed!");
                return;
            }
        }

        data.ClipIndex[subIdex] = clipIdx;
        data.Duration[subIdex] = clip.FrameCount / clip.Fps;
        data.TransitionProgress[subIdex] = 1f;
        PlayerSet(player, ref data);
    }

    public void Player_CaptureBones(CraHandle player)
    {
        List<int> boneIndices = PlayerAssignedBones[player.Internal];
        for (int i = 0; i < boneIndices.Count; ++i)
        {
            int boneIdx = boneIndices[i];

            (CraPlayerData playerData, int subIdex) = PlayerGet(player);
            int clipIdx = playerData.ClipIndex[subIdex];

            // Let the bone point to our Player and Clip
            CraBoneData boneData = BoneData.Get(boneIdx);
            boneData.PlayerIndex = player.Internal;
            boneData.ClipBoneIndex = BonePlayerClipIndices[boneIdx][clipIdx];
            BoneData.Set(boneIdx, in boneData);
        }
    }

    public void Player_Reset(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.Finished[subIdex] = false;
        data.Playback[subIdex] = 0f;
        data.IsPlaying[subIdex] = false;
        PlayerSet(player, ref data);
    }

    public bool Player_IsPlaying(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.IsPlaying[subIdex];
    }

    public float Player_GetDuration(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Duration[subIdex];
    }

    public float Player_GetPlayback(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Playback[subIdex];
    }

    public void Player_Play(CraHandle player, float transitionTime=0.0f)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        if (data.PlaybackSpeed[subIdex] > 0f)
        {
            data.Playback[subIdex] = .001f;
        }
        else 
        {
            data.Playback[subIdex] = data.Duration[subIdex] - .001f;
        }
        data.IsPlaying[subIdex] = true;
        data.TransitionTime[subIdex] = transitionTime;
        data.TransitionProgress[subIdex] = transitionTime > 0.0f ? 0f : 1f;
        PlayerSet(player, ref data);
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
        PlayerSet(player, ref data);
    }

    public void Player_ResetTransition(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.TransitionProgress[subIdex] = 0f;
        PlayerSet(player, ref data);
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
        PlayerSet(player, ref data);
    }

    public bool Player_IsFinished(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Finished[subIdex];
    }

    public void Player_Assign(CraHandle player, Transform root, CraMask? mask = null)
    {
        Debug.Assert(root != null);

        if (CraMain.Instance.Settings.BoneHashFunction == null)
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
        int clipIdx = data.ClipIndex[subIdex];
        if (clipIdx < 0)
        {
            Debug.LogError($"Cannot assign Transform '{root.name}' to CraPlayer! No clip(s) set!");
            return;
        }

        CraClip clip = KnownClips[clipIdx];
        PlayerAssignInternal(assignedBones, clip, clipIdx, root, mask);

        data.TransitionProgress[subIdex] = 1f;
        PlayerSet(player, ref data);

        Player_CaptureBones(player);
    }



    void PlayerAssignInternal(List<int> assignedBones, CraClip clip, int clipIdx, Transform current, CraMask? mask = null, bool maskedChild = false)
    {
        void AddBone(int clipBoneIdx)
        {
            int allocIdx;
            if (!KnownBoneIndices.TryGetValue(current, out allocIdx))
            {
                allocIdx = BoneData.Alloc();
                KnownBoneIndices.Add(current, allocIdx);
                BonePlayerClipIndices.Add(new Dictionary<int, int>());
                Bones.Add(current);

                Debug.Assert(KnownBoneIndices.Count == BoneData.GetNumAllocated());
                Debug.Assert(BonePlayerClipIndices.Count == BoneData.GetNumAllocated());
                Debug.Assert(Bones.length == BoneData.GetNumAllocated());
            }

            BonePlayerClipIndices[allocIdx].Add(clipIdx, clipBoneIdx);
            assignedBones.Add(allocIdx);
        }

        int boneHash = CraMain.Instance.Settings.BoneHashFunction(current.name);
        bool isMasked = false;
        if (clip.BoneHashToIdx.TryGetValue(boneHash, out int clipBoneIdx))
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
            PlayerAssignInternal(assignedBones, clip, clipIdx, current.GetChild(i), mask, isMasked);
        }
    }

    (CraPlayerData, int) PlayerGet(CraHandle player)
    {
        int dataIdx = player.Internal / 4;
        int subIdex = player.Internal % 4;
        return (PlayerData.Get(dataIdx), subIdex);
    }

    void PlayerSet(CraHandle player, ref CraPlayerData data)
    {
        int dataIdx = player.Internal / 4;
        PlayerData.Set(dataIdx, in data);
    }

    int CopyClip(CraClip clip)
    {
        int clipIdx = ClipData.Alloc();
        if (clipIdx < 0)
        {
            return -1;
        }

        CraClipData data = ClipData.Get(clipIdx);
        data.FPS = clip.Fps;
        data.FrameCount = clip.FrameCount;
        data.FrameOffset = BakedClipTransforms.GetNumAllocated();
        ClipData.Set(clipIdx, in data);

        KnownClipIndices.Add(clip, clipIdx);
        KnownClips.Add(clipIdx, clip);

        for (int i = 0; i < clip.Bones.Length; ++i)
        {
            if (clip.Bones[i].Curve.BakedFrames == null)
            {
                Debug.LogError($"Given clip '{clip.Name}' is not fully baked!");
                return -1;
            }

            if (!BakedClipTransforms.AllocFrom(clip.Bones[i].Curve.BakedFrames))
            {
                return -1;
            }
        }

        return clipIdx;
    }

    public void Clear()
    {
        PlayerData.Clear();
        PlayerCounter = 0;

        BoneData.Clear();
        Bones.SetTransforms(new Transform[] { });

        ClipData.Clear();
        BakedClipTransforms.Clear();

        KnownClipIndices.Clear();
        KnownClips.Clear();

        KnownBoneIndices.Clear();

        BonePlayerClipIndices.Clear();
        PlayerAssignedBones.Clear();
    }


    public void Destroy()
    {
        Debug.Log("Deleting all animator manager data");
        PlayerData.Destroy();
        ClipData.Destroy();
        BoneData.Destroy();
        Bones.Dispose();
        BakedClipTransforms.Destroy();
    }

    public void Tick()
    {
        PlayerJob.DeltaTime = Time.deltaTime;
        JobHandle playerJob = PlayerJob.Schedule(PlayerData.GetNumAllocated(), 8);

        // Update ALL players FIRST before evaluating ALL bones!
        JobHandle boneJob = BoneJob.Schedule(Bones, playerJob);
        boneJob.Complete();

#if UNITY_EDITOR
        Statistics.PlayerData.MaxElements = PlayerData.GetCapacity();
        Statistics.PlayerData.MaxBytes = CraPlayerData.SIZE * (ulong)PlayerData.GetCapacity();

        Statistics.ClipData.MaxElements = ClipData.GetCapacity();
        Statistics.ClipData.MaxBytes = CraClipData.SIZE * (ulong)ClipData.GetCapacity();

        Statistics.BakedClipTransforms.MaxElements = BakedClipTransforms.GetCapacity();
        Statistics.BakedClipTransforms.MaxBytes = CraTransform.SIZE * (ulong)BakedClipTransforms.GetCapacity();

        Statistics.BoneData.MaxElements = BoneData.GetCapacity();
        Statistics.BoneData.MaxBytes = CraBoneData.SIZE * (ulong)BoneData.GetCapacity();

        Statistics.Bones.MaxElements = Bones.capacity;
        Statistics.Bones.MaxBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.capacity;


        Statistics.PlayerData.CurrentElements = PlayerData.GetNumAllocated();
        Statistics.PlayerData.CurrentBytes = CraPlayerData.SIZE * (ulong)PlayerData.GetNumAllocated();

        Statistics.ClipData.CurrentElements = ClipData.GetNumAllocated();
        Statistics.ClipData.CurrentBytes = CraClipData.SIZE * (ulong)ClipData.GetNumAllocated();

        Statistics.BakedClipTransforms.CurrentElements = BakedClipTransforms.GetNumAllocated();
        Statistics.BakedClipTransforms.CurrentBytes = CraTransform.SIZE * (ulong)BakedClipTransforms.GetNumAllocated();

        Statistics.BoneData.CurrentElements = BoneData.GetNumAllocated();
        Statistics.BoneData.CurrentBytes = CraBoneData.SIZE * (ulong)BoneData.GetNumAllocated();

        Statistics.Bones.CurrentElements = Bones.length;
        Statistics.Bones.CurrentBytes = (sizeof(bool) + sizeof(int) * 2) * (ulong)Bones.length;
#endif
    }
}