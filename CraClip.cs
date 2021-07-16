using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;

using UnityEditor;


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
        return Mathf.Max(Mathf.CeilToInt(endTime * fps), 1);
    }

    public void Bake(float fps, int frameCount, CraInterpMethod method)
    {
        if (fps <= 0)
        {
            Debug.LogError($"Cannot bake curve with {fps} fps!");
            return;
        }
        if (frameCount <= 0)
        {
            Debug.LogError($"Cannot bake curve with {frameCount} frames!");
            return;
        }
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

    public CraTransform[] BakedFrames;

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
        if (fps <= 0)
        {
            Debug.LogError($"Cannot bake curve with {fps} fps!");
            return;
        }
        if (frameCount <= 0)
        {
            Debug.LogError($"Cannot bake curve with {frameCount} frames!");
            return;
        }
        if (BakedFrames != null)
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

        BakedFrames = new CraTransform[FrameCount];

        for (int i = 0; i < FrameCount; ++i)
        {
            BakedFrames[i].Rotation.x = Curves[0].BakedFrames[i];
            BakedFrames[i].Rotation.y = Curves[1].BakedFrames[i];
            BakedFrames[i].Rotation.z = Curves[2].BakedFrames[i];
            BakedFrames[i].Rotation.w = Curves[3].BakedFrames[i];
            BakedFrames[i].Position.x = Curves[4].BakedFrames[i];
            BakedFrames[i].Position.y = Curves[5].BakedFrames[i];
            BakedFrames[i].Position.z = Curves[6].BakedFrames[i];
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
                quaternion last = BakedFrames[lastRotFrameIdx].Rotation;
                quaternion next = BakedFrames[nextRotFrameIdx].Rotation;

                float t = (i - lastRotFrameIdx) / frameDuration;
                BakedFrames[i].Rotation = math.slerp(last, next, t).value;
            }
        }

        // remove redundant data
        for (int i = 0; i < 7; ++i)
        {
            Curves[i].BakedFrames = null;
            Curves[i]._BakedKeyIndices = null;
        }

        BakeMemoryConsumption = (ulong)BakedFrames.Length * sizeof(float) * 7;
    }
}

public class CraClip
{
    public string Name;
    public float Fps { get; private set; } = -1f;    // -1 => not yet baked
    public int FrameCount { get; private set; } = 0;
    public CraBone[] Bones { get; private set; }
    public Dictionary<int, int> BoneHashToIdx { get; private set; } = new Dictionary<int, int>();



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
        if (fps <= 0)
        {
            Debug.LogError($"Cannot bake curve with {fps} fps!");
            return;
        }
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

        FrameCount = 0;
        Fps = fps;

        for (int i = 0; i < Bones.Length; ++i)
        {
            FrameCount = Mathf.Max(FrameCount, Bones[i].Curve.GetEstimatedFrameCount(Fps));
        }

        for (int i = 0; i < Bones.Length; ++i)
        {
            Bones[i].Curve.Bake(Fps, FrameCount);
        }
    }
}

public struct CraPlayer
{
    public CraHandle PlayerHandle { get; private set; }

    public static CraPlayer CreateNew()
    {
        return new CraPlayer
        {
            PlayerHandle = CraPlaybackManager.Instance.PlayerNew()
        };
    }

    public static CraPlayer CreateEmpty()
    {
        return new CraPlayer
        {
            PlayerHandle = new CraHandle(CraSettings.STATE_NONE)
        };
    }

    public bool IsValid()
    {
        return PlayerHandle.IsValid();
    }

    public void SetClip(CraClip clip)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetClip(PlayerHandle, clip);
    }

    public void Assign(Transform root, CraMask? mask=null)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerAssign(PlayerHandle, root, mask);
    }

    public void CaptureBones()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerCaptureBones(PlayerHandle);
    }

    public void Reset()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerReset(PlayerHandle);
    }

    public bool IsPlaying()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsPlaying(PlayerHandle);
    }

    public float GetDuration()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetDuration(PlayerHandle);
    }

    public void Play(bool transit=false)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerPlay(PlayerHandle, transit);
    }

    public float GetPlayback()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetPlayback(PlayerHandle);
    }

    public float GetPlaybackSpeed()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetPlaybackSpeed(PlayerHandle);
    }

    public void SetPlaybackSpeed(float speed)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetPlaybackSpeed(PlayerHandle, speed);
    }

    public void ResetTransition()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerResetTransition(PlayerHandle);
    }

    public bool IsLooping()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsLooping(PlayerHandle);
    }

    public void SetLooping(bool loop)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetLooping(PlayerHandle, loop);
    }

    public bool IsFinished()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsFinished(PlayerHandle);
    }
}