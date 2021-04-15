using System;
using System.Collections.Generic;
using UnityEngine;


public struct CraKey<T> : IComparable where T : struct
{
    public float Time;
    public T Value;

    public CraKey(float time, ref T value)
    {
        Time = time;
        Value = value;
    }

    public int CompareTo(object obj)
    {
        if (!(obj is CraKey<T>))
        {
            throw new ArgumentException($"Cannot compare '{GetType()}' to {obj.GetType()}!");
        }
        return (int)(Time * 100f - ((CraKey<T>)obj).Time * 100f);
    }
}

public struct CraBone
{
    public Transform Bone;
    public CraTransformCurve Curve;
    public bool Update;


}

public class CraTransformCurve
{
    public int FrameCount { get; private set; }
    public float Fps { get; private set; } = 30f;

    public Vector3[] BakedPositionFrames = null;
    public Quaternion[] BakedRotationFrames = null;

    List<CraKey<Vector3>> EditPositionKeys;
    List<CraKey<Quaternion>> EditRotationKeys;


    public void AddPositionKey(float time, ref Vector3 value)
    {
        if (BakedPositionFrames != null)
        {
            throw new Exception("Cannot change add position key after bake!");
        }
        EditPositionKeys.Add(new CraKey<Vector3>(time, ref value));
    }

    public void AddRotationKey(float time, ref Quaternion value)
    {
        if (BakedPositionFrames != null)
        {
            throw new Exception("Cannot change add rotation key after bake!");
        }
        EditRotationKeys.Add(new CraKey<Quaternion>(time, ref value));
    }

    public int GetEstimatedFrameCount()
    {
        float endTimePos = EditPositionKeys.Count > 0 ? EditPositionKeys[EditPositionKeys.Count - 1].Time : 0f;
        float endTimeRot = EditRotationKeys.Count > 0 ? EditRotationKeys[EditRotationKeys.Count - 1].Time : 0f;
        float endTime = Mathf.Max(endTimePos, endTimeRot);

        float timeStep = 1f / Fps;
        return Mathf.CeilToInt(endTime / timeStep);
    }

    public void Bake(float fps, int frameCount)
    {
        if (EditPositionKeys.Count == 0 && EditRotationKeys.Count == 0)
        {
            Debug.LogError("Cannot bake empty transform curve!");
            return;
        }

        if (BakedPositionFrames != null)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        Fps = fps;
        FrameCount = frameCount;

        EditPositionKeys.Sort();
        EditRotationKeys.Sort();

        float endTimePos = EditPositionKeys.Count > 0 ? EditPositionKeys[EditPositionKeys.Count - 1].Time : 0f;
        float endTimeRot = EditRotationKeys.Count > 0 ? EditRotationKeys[EditRotationKeys.Count - 1].Time : 0f;

        BakedPositionFrames = new Vector3[FrameCount];
        BakedRotationFrames = new Quaternion[FrameCount];

        BakedPositionFrames[0] = EditPositionKeys.Count > 0 ? EditPositionKeys[0].Value : Vector3.zero;
        BakedRotationFrames[0] = EditRotationKeys.Count > 0 ? EditRotationKeys[0].Value : Quaternion.identity;

        int lastPos = 0;
        int lastRot = 0;
        int nextPos = 0;
        int nextRot = 0;
        float timeStep = 1f / Fps;
        for (int i = 1; i < FrameCount - 1; ++i)
        {
            float timeNow  = timeStep * i;

            // Bake Positions
            if (timeNow <= endTimePos)
            {
                for (float posTimeNext = EditPositionKeys[nextPos].Time; posTimeNext < timeNow;)
                {
                    lastPos = nextPos++;
                    posTimeNext = EditPositionKeys[nextPos].Time;
                }
                float duration = EditPositionKeys[nextPos].Time - EditPositionKeys[lastPos].Time;
                Debug.Assert(duration > 0f);
                float t = (timeNow - EditPositionKeys[lastPos].Time) / duration;
                BakedPositionFrames[i] = Vector3.Lerp(EditPositionKeys[lastPos].Value, EditPositionKeys[nextPos].Value, t);
            }
            else
            {
                BakedPositionFrames[i] = BakedPositionFrames[i - 1];
            }

            // Bake Rotations
            if (timeNow <= endTimeRot)
            {
                for (float rotTimeNext = EditPositionKeys[nextPos].Time; rotTimeNext < timeNow;)
                {
                    lastRot = nextRot++;
                    rotTimeNext = EditPositionKeys[nextRot].Time;
                }
                float duration = EditRotationKeys[nextRot].Time - EditRotationKeys[lastRot].Time;
                Debug.Assert(duration > 0f);
                float t = (timeNow - EditRotationKeys[lastRot].Time) / duration;
                BakedRotationFrames[i] = Quaternion.Slerp(EditRotationKeys[lastRot].Value, EditRotationKeys[nextRot].Value, t);
            }
            else
            {
                BakedRotationFrames[i] = BakedRotationFrames[i - 1];
            }
        }

        EditPositionKeys.Clear();
        EditRotationKeys.Clear();
    }
}

public class CraClip
{
    public bool AnimationEnded { get; private set; }

    List<CraBone> Bones;
    float Fps = -1f;

    int FrameCount = -1;
    int FrameIdx = 0;

    public void AddBone(CraBone bone)
    {
        if (bone.Bone == null)
        {
            Debug.LogError($"Cannot add bone with no Transform assigned!");
            return;
        }
        if (bone.Curve == null)
        {
            Debug.LogWarning($"Added bone '{bone.Bone.name}' does not have a curve assigned!");
        }

        if (Fps == -1f && bone.Curve != null)
        {
            Fps = bone.Curve.Fps;
        }

        if (bone.Curve != null)
        {
            if (bone.Curve.Fps != Fps)
            {
                Debug.LogError($"Cannot add bone with a Transform Curve of '{bone.Curve.Fps}' Fps, while the clip uses '{Fps}' Fps!");
                return;
            }
            FrameCount = Mathf.Max(FrameCount, bone.Curve.FrameCount);
        }

        Bones.Add(bone);
    }

    public void Bake(float fps)
    {
        if (FrameCount > -1)
        {
            Debug.LogError("Cannot bake twice!");
            return;
        }

        FrameCount = 0;
        Fps = fps;

        for (int i = 0; i < Bones.Count; ++i)
        {
            FrameCount = Mathf.Max(FrameCount, Bones[i].Curve.GetEstimatedFrameCount());
        }

        for (int i = 0; i < Bones.Count; ++i)
        {
            Bones[i].Curve.Bake(Fps, FrameCount);
        }
    }

    public void AdvanceFrameLoop()
    {
        FrameIdx = ++FrameIdx % FrameCount;
        for (int i = 0; i < Bones.Count; ++i)
        {
            Bones[i].Bone.position = Bones[i].Curve.BakedPositionFrames[FrameIdx];
            Bones[i].Bone.rotation = Bones[i].Curve.BakedRotationFrames[FrameIdx];
        }
    }

    public void AdvanceFrame()
    {
        FrameIdx++;
        if (FrameIdx >= FrameCount)
        {
            FrameIdx = FrameCount - 1;
            AnimationEnded = true;
            return;
        }
        for (int i = 0; i < Bones.Count; ++i)
        {
            Bones[i].Bone.position = Bones[i].Curve.BakedPositionFrames[FrameIdx];
            Bones[i].Bone.rotation = Bones[i].Curve.BakedRotationFrames[FrameIdx];
        }
    }

    public void Reset()
    {
        AnimationEnded = false;
        FrameIdx = 0;
    }
}
