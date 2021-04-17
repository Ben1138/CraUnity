using System;
using System.Collections.Generic;
using UnityEngine;


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
}

public class CraCurve
{
    public List<CraKey> EditKeys = new List<CraKey>();
    public float[] BakedFrames;
    public int FrameCount { get; private set; }
    public float Fps { get; private set; } = 30f;

    public int GetEstimatedFrameCount(float fps)
    {
        float endTime = EditKeys[EditKeys.Count - 1].Time;
        float timeStep = 1f / fps;
        return Mathf.CeilToInt(endTime / timeStep);
    }

    public void Bake(float fps, int frameCount)
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
                }
                float duration = EditKeys[nextIdx].Time - EditKeys[lastIdx].Time;
                Debug.Assert(duration > 0f);
                float t = (timeNow - EditKeys[lastIdx].Time) / duration;
                BakedFrames[i] = Mathf.Lerp(EditKeys[lastIdx].Value, EditKeys[nextIdx].Value, t);
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

        for (int i = 0; i < 7; ++i)
        {
            Curves[i].Bake(fps, frameCount);
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

        // remove redundant data
        for (int i = 0; i < 7; ++i)
        {
            Curves[i].BakedFrames = null;
        }
    }
}

public class CraClip
{
    public string Name;
    public float Fps { get; private set; } = -1f;    // -1 => not yet baked
    public int FrameCount { get; private set; } = 0;
    public List<CraBone> Bones = new List<CraBone>();


    public void Bake(float fps)
    {
        if (Fps > -1)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        FrameCount = 0;
        Fps = fps;

        for (int i = 0; i < Bones.Count; ++i)
        {
            FrameCount = Mathf.Max(FrameCount, Bones[i].Curve.GetEstimatedFrameCount(Fps));
        }

        for (int i = 0; i < Bones.Count; ++i)
        {
            Bones[i].Curve.Bake(Fps, FrameCount);
        }
    }
}

public class CraPlayer
{
    public static Func<string, int> BoneHashFunction;

    public bool Looping = false;
    public bool AnimationEnded { get; private set; }
    CraClip Clip;
    Dictionary<int, int> HashToClipBoneIdx = new Dictionary<int, int>();
    int[] AssignedIndices;
    Transform[] AssignedBones;
    bool IsPlaying;
    float Duration = 0f;
    float Playback = 0f;

    public void SetClip(CraClip clip)
    {
        HashToClipBoneIdx.Clear();
        for (int i = 0; i < clip.Bones.Count; ++i)
        {
            HashToClipBoneIdx.Add(clip.Bones[i].BoneHash, i);
        }
        Clip = clip;
        Duration = clip.FrameCount / clip.Fps;
    }

    public void Assign(Transform root)
    {
        if (BoneHashFunction == null)
        {
            Debug.LogError("CraPlayer.BoneHashFunction is not assigned! You need to assign a custom hash function!");
            return;
        }
        if (Clip == null)
        {
            Debug.LogError($"Cannot assign Transform '{root.name}' to CraPlayer! No clip set!");
            return;
        }
        AssignedBones = new Transform[Clip.Bones.Count];
        AssignInternal(root);

        // store assigned indices so we don't have to do a null
        // check for each bone for every single evaluation
        List<int> assignedIndices = new List<int>();
        for (int i = 0; i < AssignedBones.Length; ++i)
        {
            if (AssignedBones[i] != null)
            {
                assignedIndices.Add(i);
            }
        }
        AssignedIndices = assignedIndices.ToArray();

        Evaluate(0f);
    }

    public void Evaluate(float timePos)
    {
        int frameIdx = Mathf.FloorToInt(timePos * Clip.Fps);
        for (int i = 0; i < AssignedIndices.Length; ++i)
        {
            int idx = AssignedIndices[i];
            AssignedBones[idx].localPosition = Clip.Bones[idx].Curve.BakedPositions[frameIdx];
            AssignedBones[idx].localRotation = Clip.Bones[idx].Curve.BakedRotations[frameIdx];
        }
    }

    public void Reset()
    {
        AnimationEnded = false;
        Playback = 0f;
    }

    public void Play()
    {
        Playback = 0f;
        IsPlaying = true;
    }

    public void Update(float deltaTime)
    {
        if (!IsPlaying) return;

        Playback += deltaTime;
        if (Playback > Duration)
        {
            Playback = 0;
            if (!Looping)
            {
                IsPlaying = false;
                AnimationEnded = true;
                return;
            }
        }    
        Evaluate(Playback);
    }

    void AssignInternal(Transform root)
    {
        int boneHash = BoneHashFunction(root.name);
        if (HashToClipBoneIdx.TryGetValue(boneHash, out int boneIdx))
        {
            AssignedBones[boneIdx] = root;
        }
        for (int i = 0; i < root.childCount; ++i)
        {
            AssignInternal(root.GetChild(i));
        }
    }
}