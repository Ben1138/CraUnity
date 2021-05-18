using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;


public enum CraInterpMethod
{
    Linear,
    NearestNeighbour
}

public struct CraKey : IComparable 
{
    public float Time;
    public float Value;

    public CraKey(float time, float value)
    {
        Time = time;
        Value = value;
    }

    public int CompareTo(object obj)
    {
        if (!(obj is CraKey))
        {
            throw new ArgumentException($"Cannot compare '{GetType()}' to {obj.GetType()}!");
        }
        return (int)(Time * 100f - ((CraKey)obj).Time * 100f);
    }
}

public struct CraBone
{
    public int BoneHash;
    public CraTransformCurve Curve;

    public CraBone(string boneName, CraTransformCurve curve)
    {
        if (CraSettings.BoneHashFunction == null)
        {
            throw new Exception("CraSettings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        BoneHash = CraSettings.BoneHashFunction(boneName);
        Curve = curve;
    }
}

/// <summary>
/// Masks are inclusive, i.e. only assign bones to a clip specified in here
/// </summary>
public struct CraMask
{
    public bool MaskChildren;
    public HashSet<int> BoneHashes;

    public CraMask(bool maskChildren, params string[] boneNames)
    {
        if (CraSettings.BoneHashFunction == null)
        {
            throw new Exception("CraSettings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        BoneHashes = new HashSet<int>();
        for (int i = 0; i < boneNames.Length; ++i)
        {
            BoneHashes.Add(CraSettings.BoneHashFunction(boneNames[i]));
        }
        MaskChildren = maskChildren;
    }
}

public class CraCurve
{
    public List<CraKey> EditKeys = new List<CraKey>();
    public float[] BakedFrames;
    public int FrameCount { get; private set; }
    public float Fps { get; private set; } = 30f;

    // contains indices of baked frames corresponding to the original edit keys
    public Queue<int> _BakedKeyIndices;

    public int GetEstimatedFrameCount(float fps)
    {
        float endTime = EditKeys[EditKeys.Count - 1].Time;
        float timeStep = 1f / fps;
        return Mathf.CeilToInt(endTime / timeStep);
    }

    public void Bake(float fps, int frameCount, CraInterpMethod method)
    {
        if (EditKeys.Count == 0)
        {
            Debug.LogError("Cannot bake empty transform curve!");
            return;
        }

        if (BakedFrames != null)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        Fps = fps;
        FrameCount = frameCount;

        BakedFrames = new float[FrameCount];
        BakedFrames[0] = EditKeys[0].Value;

        _BakedKeyIndices = new Queue<int>();

        float endTime = EditKeys[EditKeys.Count - 1].Time;

        int lastIdx = 0;
        int nextIdx = 0;
        float timeStep = 1f / Fps;
        for (int i = 1; i < FrameCount; ++i)
        {
            float timeNow = timeStep * i;

            // Bake Positions
            if (timeNow <= endTime)
            {
                for (float posTimeNext = EditKeys[nextIdx].Time; posTimeNext < timeNow;)
                {
                    lastIdx = nextIdx++;
                    posTimeNext = EditKeys[nextIdx].Time;

                    _BakedKeyIndices.Enqueue(i);
                }

                float duration = EditKeys[nextIdx].Time - EditKeys[lastIdx].Time;
                Debug.Assert(duration > 0f);

                if (method == CraInterpMethod.Linear)
                {
                    float t = (timeNow - EditKeys[lastIdx].Time) / duration;
                    BakedFrames[i] = Mathf.Lerp(EditKeys[lastIdx].Value, EditKeys[nextIdx].Value, t);
                }
                else if (method == CraInterpMethod.NearestNeighbour)
                {
                    float diffLast = timeNow - EditKeys[lastIdx].Time;
                    float diffNext = EditKeys[nextIdx].Time - timeNow;
                    BakedFrames[i] = diffLast < diffNext ? EditKeys[lastIdx].Value : EditKeys[nextIdx].Value;
                }
            }
            else
            {
                BakedFrames[i] = BakedFrames[i - 1];
            }
        }

        EditKeys.Clear();
    }
}

public class CraTransformCurve
{
    public int FrameCount { get; private set; }
    public float Fps { get; private set; } = 30f;

    public ulong BakeMemoryConsumption { get; private set; }

    // 0 : rot X
    // 1 : rot Y
    // 2 : rot Z
    // 3 : rot W
    // 4 : pos X
    // 5 : pos Y
    // 6 : pos Z
    public CraCurve[] Curves = new CraCurve[7];

    public Vector3[] BakedPositions;
    public Quaternion[] BakedRotations;

    public CraTransformCurve()
    {
        for (int i = 0; i < 7; ++i)
        {
            Curves[i] = new CraCurve();
        }
    }

    public int GetEstimatedFrameCount(float fps)
    {
        int frameCount = 0;
        for (int i = 0; i < 7; ++i)
        {
            frameCount = Mathf.Max(frameCount, Curves[i].GetEstimatedFrameCount(fps));
        }
        return frameCount;
    }

    public void Bake(float fps, int frameCount)
    {
        if (BakedPositions != null)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        Fps = fps;
        FrameCount = frameCount;
        BakeMemoryConsumption = 0;

        for (int i = 0; i < 7; ++i)
        {
            if (i >= 4)
            {
                // Bake positions with simple linear interpolation. Done.
                Curves[i].Bake(fps, frameCount, CraInterpMethod.Linear);
            }
            else
            {
                // Bake rotations with nearest neighbour. This will first lead to "edgy" rotations,
                // but will ensure consistent key frames accross all channels (x y z w) we can
                // then use for spherical linear interpolation
                Curves[i].Bake(fps, frameCount, CraInterpMethod.NearestNeighbour);
            }
        }

        BakedPositions = new Vector3[FrameCount];
        BakedRotations = new Quaternion[FrameCount];

        for (int i = 0; i < FrameCount; ++i)
        {
            BakedRotations[i].x = Curves[0].BakedFrames[i];
            BakedRotations[i].y = Curves[1].BakedFrames[i];
            BakedRotations[i].z = Curves[2].BakedFrames[i];
            BakedRotations[i].w = Curves[3].BakedFrames[i];
            BakedPositions[i].x = Curves[4].BakedFrames[i];
            BakedPositions[i].y = Curves[5].BakedFrames[i];
            BakedPositions[i].z = Curves[6].BakedFrames[i];
        }

        // Do proper spherical linear interpolation between rotation key frames, overriding in between frames.
        // TODO: It seems there's still a faulty when dealing with either:
        // - start to first key frame
        // - last key frame to end
        // further investigation needed!
        int lastRotFrameIdx = 0;
        int nextRotFrameIdx = 0;
        for (int i = 0; i < FrameCount; ++i)
        {
            // ensure all prior key indices get dequeued
            for (int ch = 0; ch < 4; ++ch)
            {
                while (Curves[ch]._BakedKeyIndices.Count > 0 && Curves[ch]._BakedKeyIndices.Peek() <= i)
                {
                    Curves[ch]._BakedKeyIndices.Dequeue();
                }
            }

            int nextCh = FrameCount - 1;
            for (int ch = 0; ch < 4; ++ch)
            {
                if (Curves[ch]._BakedKeyIndices.Count > 0)
                {
                    // get the next key frame
                    int peek = Curves[ch]._BakedKeyIndices.Peek();
                    if (Curves[ch]._BakedKeyIndices.Count > 0 && peek > i)
                    {
                        nextCh = Mathf.Min(nextCh, peek);
                    }
                }
            }

            if (nextCh > nextRotFrameIdx)
            {
                lastRotFrameIdx = nextRotFrameIdx;
                nextRotFrameIdx = nextCh;
            }

            float frameDuration = nextRotFrameIdx - lastRotFrameIdx;
            if (frameDuration > 0f)
            {
                ref Quaternion last = ref BakedRotations[lastRotFrameIdx];
                ref Quaternion next = ref BakedRotations[nextRotFrameIdx];

                float t = (i - lastRotFrameIdx) / frameDuration;
                BakedRotations[i] = Quaternion.Slerp(last, next, t);
            }
        }

        // remove redundant data
        for (int i = 0; i < 7; ++i)
        {
            Curves[i].BakedFrames = null;
            Curves[i]._BakedKeyIndices = null;
        }

        BakeMemoryConsumption = (ulong)BakedPositions.Length * sizeof(float) * 3 + (ulong)BakedRotations.Length * sizeof(float) * 4;
    }
}

public class CraClip
{
    public string Name;
    public float Fps { get; private set; } = -1f;    // -1 => not yet baked
    public int FrameCount { get; private set; } = 0;
    public CraBone[] Bones { get; private set; }

    Dictionary<int, int> BoneHashToIdx = new Dictionary<int, int>();

    // in bytes
    public ulong BakeMemoryConsumption { get; private set; }
    public static ulong GlobalBakeMemoryConsumption { get; private set; }

    ~CraClip()
    {
        GlobalBakeMemoryConsumption -= BakeMemoryConsumption;
    }

    public void SetBones(CraBone[] bones)
    {
        Bones = bones;
        BoneHashToIdx.Clear();
        for (int i = 0; i < Bones.Length; ++i)
        {
            BoneHashToIdx.Add(Bones[i].BoneHash, i);
        }
    }

    public int GetBoneIdx(string boneName)
    {
        if (BoneHashToIdx.TryGetValue(CraSettings.BoneHashFunction(boneName), out int idx))
        {
            return idx;
        }
        return -1;
    }

    public CraBone? GetBone(int hash)
    {
        if (BoneHashToIdx.TryGetValue(hash, out int idx))
        {
            return Bones[idx];
        }
        return null;
    }

    public void Bake(float fps)
    {
        if (Bones == null)
        {
            Debug.LogError("Cannot bake empty clip!");
            return;
        }

        if (Fps > -1)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        GlobalBakeMemoryConsumption -= BakeMemoryConsumption;
        BakeMemoryConsumption = 0;
        FrameCount = 0;
        Fps = fps;

        for (int i = 0; i < Bones.Length; ++i)
        {
            FrameCount = Mathf.Max(FrameCount, Bones[i].Curve.GetEstimatedFrameCount(Fps));
        }

        for (int i = 0; i < Bones.Length; ++i)
        {
            Bones[i].Curve.Bake(Fps, FrameCount);
            BakeMemoryConsumption += Bones[i].Curve.BakeMemoryConsumption;
        }

        GlobalBakeMemoryConsumption += BakeMemoryConsumption;
    }
}

public class CraPlayer
{
    public bool Looping = false;
    public float PlaybackSpeed = 1f;
    public float Blending; // blending between clip1 (0.0) and clip2 (1.0)
    public bool Finished { get; private set; }
    public float Duration { get; private set; }
    public bool IsPlaying { get; private set; }
    public float Playback { get; private set; }
    public bool SupportBlending => ClipMinor != null;

    const float DIST_WEIGHT_MUL = 0.8f;

    CraClip ClipMajor;
    CraClip ClipMinor;
    Transform[] AssignedBones;
    int FrameCount;

    // Contains all indices of 'AssignedBones' slots that are not null.
    // A slot in 'AssignedBones' can be null, if the clip animates a bone that
    // hasn't been found in the assigned skeleton.
    int[] AssignedBoneIndices;

    int[] AssignedBoneIndicesMajor;
    int[] AssignedBoneIndicesMinor;

    // Map a bone hash to a specific index of 'AssignedBones'
    Dictionary<int, int> HashToBoneIdx = new Dictionary<int, int>();

    // For each major frame, map to a minor frame that's most 'alike'
    int[] NearestFrameIdx;


    public void SetClip(CraClip clip)
    {
        Debug.Assert(clip != null);

        // A clip animates certain bones. Assign each of those bones an index we can later lookup
        HashToBoneIdx.Clear();
        for (int i = 0; i < clip.Bones.Length; ++i)
        {
            HashToBoneIdx.Add(clip.Bones[i].BoneHash, i);
        }
        ClipMajor = clip;
        ClipMinor = null;
        FrameCount = clip.FrameCount;
        Duration = FrameCount / clip.Fps;
    }

    public void SetClips(CraClip clip1, CraClip clip2)
    {
        Debug.Assert(clip1 != null);
        Debug.Assert(clip2 != null);
        Debug.Assert(clip1.Fps == clip2.Fps);

        // A clip animates certain bones. Assign each of those bones an index we can later lookup
        HashToBoneIdx.Clear();
        for (int i = 0; i < clip1.Bones.Length; ++i)
        {
            HashToBoneIdx.Add(clip1.Bones[i].BoneHash, i);
        }

        int idx = clip1.Bones.Length;
        for (int i = 0; i < clip2.Bones.Length; ++i)
        {
            if (!HashToBoneIdx.ContainsKey(clip2.Bones[i].BoneHash))
            {
                HashToBoneIdx.Add(clip2.Bones[i].BoneHash, idx++);
            }
        }

        // the longer clip is always the major clip
        if (clip1.FrameCount >= clip2.FrameCount)
        {
            ClipMajor = clip1;
            ClipMinor = clip2;
            FrameCount = clip1.FrameCount;
        }
        else
        {
            ClipMajor = clip2;
            ClipMinor = clip1;
            FrameCount = clip2.FrameCount;
        }

        Duration = FrameCount / clip1.Fps;
    }

    public void Assign(Transform root, CraMask? mask=null)
    {
        Debug.Assert(root != null);

        if (CraSettings.BoneHashFunction == null)
        {
            throw new Exception("CraSettings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }
        if (ClipMajor == null)
        {
            Debug.LogError($"Cannot assign Transform '{root.name}' to CraPlayer! No clip(s) set!");
            return;
        }

        AssignedBones = new Transform[HashToBoneIdx.Count];
        AssignInternal(root, mask);

        // Store assigned indices so we don't have to do a null check
        // for each bone in 'AssignedBones' for every single evaluation.
        // Also, store the bone index for each assigned bone for both
        // the major and minor clip for easy indexing.
        List<int> assignedIndices = new List<int>();
        List<int> assignedIndicesMajor = new List<int>();
        List<int> assignedIndicesMinor = new List<int>();
        for (int i = 0; i < AssignedBones.Length; ++i)
        {
            if (AssignedBones[i] != null)
            {
                assignedIndices.Add(i);
                assignedIndicesMajor.Add(ClipMajor.GetBoneIdx(AssignedBones[i].name));

                if (ClipMinor != null)
                {
                    assignedIndicesMinor.Add(ClipMinor.GetBoneIdx(AssignedBones[i].name));
                }
            }
        }
        AssignedBoneIndices = assignedIndices.ToArray();
        AssignedBoneIndicesMajor = assignedIndicesMajor.ToArray();
        AssignedBoneIndicesMinor = assignedIndicesMinor.ToArray();

        // for each major frame, calculate nearest minor frame. needed for blending
        if (ClipMinor != null)
        {
            NearestFrameIdx = new int[ClipMajor.FrameCount];
            for (int frameIdx = 0; frameIdx < NearestFrameIdx.Length; ++frameIdx)
            {
                // To get the nearest neighbour of a frame, iterate all bones from
                // major and minor clip and compare their rotation angle distances.

                // We multiply the rotation distance by a weight that get's decreased 
                // while going down the bone hierarchy. This should reflect the importance
                // of different bones. E.g. if two frames differentiate by, lets say 20 degrees,
                // one would say that if that 20 degrees completely apply to one thigh bone, 
                // that frame is more 'different' than if that 20 degrees completely apply
                // to one foot bone.

                // TODO: Using the bones world distances instead might yield better results.

                float currDist = float.MaxValue;
                int   currMinorFrameIdx = 0;

                for (int minorFrame = 0; minorFrame < ClipMinor.FrameCount; ++minorFrame)
                {
                    float dist = 0f;
                    for (int bi1 = 0; bi1 < ClipMajor.Bones.Length; bi1++)
                    {
                        ref CraBone majorBone = ref ClipMajor.Bones[bi1];
                        CraBone?    minorBone = ClipMinor.GetBone(majorBone.BoneHash);
                        if (minorBone.HasValue)
                        {
                            float angle = Quaternion.Angle(majorBone.Curve.BakedRotations[frameIdx], minorBone.Value.Curve.BakedRotations[minorFrame]);
                            dist += angle;
                        }
                    }

                    if (dist < currDist)
                    {
                        currDist = dist;
                        currMinorFrameIdx = minorFrame;
                    }
                }

                NearestFrameIdx[frameIdx] = currMinorFrameIdx;
            }
        }

        Evaluate();
    }

    public void EvaluateFrame(float timePos)
    {
        Reset();
        Playback = timePos;
        Evaluate();
    }

    void Evaluate()
    {
        float frameNo = Playback * ClipMajor.Fps;
        int frameIdx = Mathf.FloorToInt(frameNo);

        if (ClipMinor != null)
        {
            for (int i = 0; i < AssignedBoneIndices.Length; ++i)
            {
                int boneIdx = AssignedBoneIndices[i];

                ref CraBone boneMajor = ref ClipMajor.Bones[AssignedBoneIndicesMajor[i]];
                ref CraBone boneMinor = ref ClipMinor.Bones[AssignedBoneIndicesMinor[i]];

                Vector3 posMajor = boneMajor.Curve.BakedPositions[frameIdx];
                Vector3 posMinor = boneMinor.Curve.BakedPositions[NearestFrameIdx[frameIdx]];

                Quaternion rotMajor = boneMajor.Curve.BakedRotations[frameIdx];
                Quaternion rotMinor = boneMinor.Curve.BakedRotations[NearestFrameIdx[frameIdx]];

                AssignedBones[boneIdx].localPosition = Vector3.Lerp(posMajor, posMinor, Blending);
                AssignedBones[boneIdx].localRotation = Quaternion.Slerp(rotMajor, rotMinor, Blending);
            }
        }
        else
        {
            for (int i = 0; i < AssignedBoneIndices.Length; ++i)
            {
                int boneIdx = AssignedBoneIndices[i];
                ref CraBone boneMajor = ref ClipMajor.Bones[AssignedBoneIndicesMajor[i]];
                AssignedBones[boneIdx].localPosition = boneMajor.Curve.BakedPositions[frameIdx];
                AssignedBones[boneIdx].localRotation = boneMajor.Curve.BakedRotations[frameIdx];
            }
        }
    }

    public void Reset()
    {
        Finished = false;
        Playback = 0f;
        IsPlaying = false;
    }

    public void Play()
    {
        Playback = 0f;
        IsPlaying = true;
    }

    public void Update(float deltaTime)
    {
        if (!IsPlaying) return;

        Playback += deltaTime * PlaybackSpeed;
        if (Playback > Duration)
        {
            Playback = 0;
            if (!Looping)
            {
                IsPlaying = false;
                Finished = true;
                return;
            }
        }
        Evaluate();
    }

    void AssignInternal(Transform root, CraMask? mask = null, bool maskedChild=false)
    {
        int boneHash = CraSettings.BoneHashFunction(root.name);
        bool isMasked = false;
        if (HashToBoneIdx.TryGetValue(boneHash, out int boneIdx))
        {
            if (mask.HasValue)
            {
                if (maskedChild || mask.Value.BoneHashes.Contains(boneHash))
                {
                    AssignedBones[boneIdx] = root;
                    isMasked = mask.Value.MaskChildren;
                }
            }
            else
            {
                AssignedBones[boneIdx] = root;
            }
        }
        for (int i = 0; i < root.childCount; ++i)
        {
            AssignInternal(root.GetChild(i), mask, isMasked);
        }
    }
}

public static class CraSettings
{
    public const int    STATE_NONE = -1;
    public const int    STATE_MAX_LAYERS = 2;
    public const int    MAX_STATES = 32;
    public const float  PLAYBACK_LERP_THRESHOLD = 0.5f;

    public static Func<string, int> BoneHashFunction;
}