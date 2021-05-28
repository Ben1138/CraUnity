using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class CraSettings
{
    public const int STATE_NONE = -1;
    public const int MAX_LAYERS = 2;
    public const int MAX_STATES_PER_LAYER = 16;
    public const float PLAYBACK_LERP_THRESHOLD = 0.5f;
    public const float TRANSITION_TIME = 0.5f;

    public const int MAX_PlayerData = 16384;
    public const int MAX_ClipData = 256;
    public const int MAX_BakedClipTransforms = 65536;
    public const int MAX_BoneData = 65536;
    public const int MAX_Bones = 65536;

    public static Func<string, int> BoneHashFunction;
}

public class CraPlaybackManager : MonoBehaviour
{
    public static CraPlaybackManager Instance { get; private set; }


    struct CraPlayerData
    {
        // setable
        public int4 ClipIndex;
        public bool4 Looping;
        public bool4 IsPlaying;
        public float4 PlaybackSpeed;
        public float4 Playback;
        public float4 Transition;

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

        const float TransitionSpeed = 2f;

        public void Execute(int index)
        {
            CraPlayerData player = PlayerData[index];

            //player.Playback += DeltaTime * player.PlaybackSpeed;
            //bool4 end = player.Playback >= player.Duration;

            player.Transition = math.clamp(player.Transition + DeltaTime * TransitionSpeed, float4.zero, new float4(1f, 1f, 1f, 1f));

            // TODO: This is BAAAAAAAAAAD
            for (int i = 0; i < 4; ++i)
            {
                if (!player.IsPlaying[i])
                {
                    continue;
                }

                CraClipData clip = ClipData[player.ClipIndex[i]];
                player.Playback[i] += DeltaTime * player.PlaybackSpeed[i];

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

            float transition = player.Transition[playerSubIdx];
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
    CraDataContainer<CraPlayerData> PlayerData;
    int PlayerCounter;

    // Bone data memory. These two arrays have the same length!
    CraDataContainer<CraBoneData> BoneData;
    TransformAccessArray Bones;

    // Clip data memory
    CraDataContainer<CraClipData> ClipData;
    CraDataContainer<CraTransform> BakedClipTransforms;

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


    public CraHandle PlayerNew()
    {
        if (PlayerCounter + 1 >= (PlayerData.GetCapacity() * 4))
        {
            Debug.LogError($"Limit of {PlayerData.GetCapacity()} Animation Players reached!");
            return new CraHandle(-1);
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
        data.Transition[subIdex] = 1f;
        PlayerData.Set(dataIdx, ref data);

        Debug.Assert(PlayerAssignedBones.Count == PlayerCounter);

        PlayerAssignedBones.Add(new List<int>());
        return new CraHandle(PlayerCounter++);
    }

    public void PlayerSetClip(CraHandle player, CraClip clip)
    {
        Debug.Assert(clip != null);

        (CraPlayerData data, int subIdex) = PlayerGet(player);
        int clipIdx = data.ClipIndex[subIdex];

        if (clipIdx >= 0)
        {
            Debug.LogWarning($"Player {player.Handle} already has clip {clipIdx} assigned!");
            return;
        }

        if (!KnownClipIndices.TryGetValue(clip, out clipIdx))
        {
            clipIdx = CopyClip(clip);
            if (clipIdx < 0)
            {
                Debug.LogError($"Setting clip {clip.Name} to Player {player.Handle} failed!");
                return;
            }
        }

        data.ClipIndex[subIdex] = clipIdx;
        data.Duration[subIdex] = clip.FrameCount / clip.Fps;
        data.Transition[subIdex] = 1f;
        PlayerSet(player, ref data);
    }

    public void PlayerCaptureBones(CraHandle player)
    {
        List<int> boneIndices = PlayerAssignedBones[player.Handle];
        for (int i = 0; i < boneIndices.Count; ++i)
        {
            int boneIdx = boneIndices[i];

            (CraPlayerData playerData, int subIdex) = PlayerGet(player);
            int clipIdx = playerData.ClipIndex[subIdex];

            // Let the bone point to our Player & Clip
            CraBoneData boneData = BoneData.Get(boneIdx);
            boneData.PlayerIndex = player.Handle;
            boneData.ClipBoneIndex = BonePlayerClipIndices[boneIdx][clipIdx];
            BoneData.Set(boneIdx, ref boneData);
        }
    }

    public void PlayerReset(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.Finished[subIdex] = false;
        data.Playback[subIdex] = 0f;
        data.IsPlaying[subIdex] = false;
        PlayerSet(player, ref data);
    }

    public bool PlayerIsPlaying(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.IsPlaying[subIdex];
    }

    public float PlayerGetDuration(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Duration[subIdex];
    }

    public float PlayerGetPlayback(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Playback[subIdex];
    }

    public void PlayerPlay(CraHandle player, bool transit = false)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.Playback[subIdex] = 0f;
        data.IsPlaying[subIdex] = true;
        data.Transition[subIdex] = transit ? 0f : 1f;
        PlayerSet(player, ref data);
    }

    public float PlayerGetPlaybackSpeed(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.PlaybackSpeed[subIdex];
    }

    public void PlayerSetPlaybackSpeed(CraHandle player, float speed)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.PlaybackSpeed[subIdex] = Mathf.Clamp01(speed);
        PlayerSet(player, ref data);
    }

    public void PlayerResetTransition(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.Transition[subIdex] = 0f;
        PlayerSet(player, ref data);
    }

    public bool PlayerIsLooping(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Looping[subIdex];
    }

    public void PlayerSetLooping(CraHandle player, bool loop)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        data.Looping[subIdex] = loop;
        PlayerSet(player, ref data);
    }

    public bool PlayerIsFinished(CraHandle player)
    {
        (CraPlayerData data, int subIdex) = PlayerGet(player);
        return data.Finished[subIdex];
    }

    public void PlayerAssign(CraHandle player, Transform root, CraMask? mask = null)
    {
        Debug.Assert(root != null);

        if (CraSettings.BoneHashFunction == null)
        {
            throw new Exception("CraSettings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        List<int> assignedBones = PlayerAssignedBones[player.Handle];
        if (assignedBones.Count > 0)
        {
            Debug.LogWarning($"Player {player.Handle} already has bones assigned!");
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

        data.Transition[subIdex] = 1f;
        PlayerSet(player, ref data);

        PlayerCaptureBones(player);
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

        int boneHash = CraSettings.BoneHashFunction(current.name);
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
        int dataIdx = player.Handle / 4;
        int subIdex = player.Handle % 4;
        return (PlayerData.Get(dataIdx), subIdex);
    }

    void PlayerSet(CraHandle player, ref CraPlayerData data)
    {
        int dataIdx = player.Handle / 4;
        PlayerData.Set(dataIdx, ref data);
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
        ClipData.Set(clipIdx, ref data);

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

    void Awake()
    {
        Instance = this;

        PlayerData = new CraDataContainer<CraPlayerData>(CraSettings.MAX_PlayerData);
        ClipData = new CraDataContainer<CraClipData>(CraSettings.MAX_ClipData);
        BakedClipTransforms = new CraDataContainer<CraTransform>(CraSettings.MAX_BakedClipTransforms);
        BoneData = new CraDataContainer<CraBoneData>(CraSettings.MAX_BoneData);
        Bones = new TransformAccessArray(CraSettings.MAX_Bones);

#if UNITY_EDITOR
        Statistics.PlayerData.MaxElements = CraSettings.MAX_PlayerData;
        Statistics.PlayerData.MaxBytes = CraPlayerData.SIZE * CraSettings.MAX_PlayerData;

        Statistics.ClipData.MaxElements = CraSettings.MAX_ClipData;
        Statistics.ClipData.MaxBytes = CraClipData.SIZE * CraSettings.MAX_ClipData;

        Statistics.BakedClipTransforms.MaxElements = CraSettings.MAX_BakedClipTransforms;
        Statistics.BakedClipTransforms.MaxBytes = CraTransform.SIZE * CraSettings.MAX_BakedClipTransforms;

        Statistics.BoneData.MaxElements = CraSettings.MAX_BoneData;
        Statistics.BoneData.MaxBytes = CraBoneData.SIZE * CraSettings.MAX_BoneData;

        Statistics.Bones.MaxElements = CraSettings.MAX_Bones;
        Statistics.Bones.MaxBytes = (sizeof(bool) + sizeof(int) * 2) * CraSettings.MAX_Bones;
#endif

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

#if UNITY_EDITOR
        EditorApplication.quitting += ClearData;
#endif
    }

    void ClearData()
    {
        Debug.Log("Clearing animator manager data");
        PlayerData.Delete();
        ClipData.Delete();
        BoneData.Delete();
        Bones.Dispose();
        BakedClipTransforms.Delete();
    }

    void Update()
    {
        PlayerJob.DeltaTime = Time.deltaTime;
        JobHandle playerJob = PlayerJob.Schedule(PlayerData.GetNumAllocated(), 8);

        // Update ALL players FIRST before evaluating ALL bones!
        JobHandle boneJob = BoneJob.Schedule(Bones, playerJob);
        boneJob.Complete();

#if UNITY_EDITOR
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

    void OnDestroy()
    {
        ClearData();
    }
}

public class CraDataContainer<T> where T : struct
{
    NativeArray<T> Elements;
    int Head;

    ~CraDataContainer()
    {
        Delete();
    }

    public CraDataContainer(int capacity)
    {
        Elements = new NativeArray<T>(capacity, Allocator.Persistent);
    }

    public NativeArray<T> GetMemoryBuffer()
    {
        Debug.Assert(Elements.IsCreated);
        return Elements;
    }

    public int GetCapacity()
    {
        return Elements.Length;
    }

    public int GetNumAllocated()
    {
        return Head;
    }

    // returns index
    public int Alloc()
    {
        Debug.Assert(Elements.IsCreated);
        if (Head == Elements.Length)
        {
            Debug.LogError($"Max capacity of {Elements.Length} reached!");
            return -1;
        }
        return Head++;
    }

    public bool Alloc(int count)
    {
        Debug.Assert(count > 0);

        int space = Elements.Length - (Head + count);
        if (space < 0)
        {
            Debug.LogError($"Alloc {count} elements exceeds the capacity of {Elements.Length}!");
            return false;
        }
        Head += count;
        return true;
    }

    public T Get(int index)
    {
        Debug.Assert(Elements.IsCreated);
        Debug.Assert(index >= 0 && index < Head);
        return Elements[index];
    }

    public void Set(int index, ref T value)
    {
        Debug.Assert(Elements.IsCreated);
        Debug.Assert(index >= 0 && index < Head);
        Elements[index] = value;
    }

    public bool AllocFrom(T[] buffer)
    {
        Debug.Assert(buffer != null);
        Debug.Assert(buffer.Length > 0);

        int previousHead = Head;
        if (!Alloc(buffer.Length))
        {
            return false;
        }
        NativeArray<T>.Copy(buffer, 0, Elements, previousHead, buffer.Length);
        return true;
    }

    public void Clear()
    {
        Head = 0;
    }

    public void Delete()
    {
        Head = 0;
        Elements.Dispose();
    }
}