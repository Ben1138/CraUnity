using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public struct CraHandle
{
    public int Handle { get; private set; }

    public CraHandle(int handle)
    {
        Handle = handle;
    }

    public bool IsValid()
    {
        return Handle >= 0;
    }
}

public struct CraTransform
{
    public float3 Position;
    public float4 Rotation;

    //public override bool Equals(object obj)
    //{
    //    CraTransform other = (CraTransform)obj;
    //    return (Vector3)other.Position == (Vector3)Position && new Quaternion(other.Rotation.x, other.Rotation.y, other.Rotation.z, other.Rotation.w) == new Quaternion(Rotation.x, Rotation.y, Rotation.z, Rotation.w);
    //}

    // For Debug reasons only
    public const ulong SIZE =
        sizeof(float) * 3 +
        sizeof(float) * 4;
}

public struct CraMeasure
{
    public int CurrentElements;
    public int MaxElements;
    public ulong CurrentBytes;
    public ulong MaxBytes;
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

public struct CraPlayer
{
    public CraHandle Handle { get; private set; }

    public static CraPlayer CreateNew()
    {
        return new CraPlayer
        {
            Handle = CraPlaybackManager.Instance.PlayerNew()
        };
    }

    public static CraPlayer CreateEmpty()
    {
        return new CraPlayer
        {
            Handle = new CraHandle(CraSettings.STATE_NONE)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public void SetClip(CraClip clip)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetClip(Handle, clip);
    }

    public void Assign(Transform root, CraMask? mask = null)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerAssign(Handle, root, mask);
    }

    public void CaptureBones()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerCaptureBones(Handle);
    }

    public void Reset()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerReset(Handle);
    }

    public bool IsPlaying()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsPlaying(Handle);
    }

    public float GetDuration()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetDuration(Handle);
    }

    public void Play(bool transit = false)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerPlay(Handle, transit);
    }

    public float GetPlayback()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetPlayback(Handle);
    }

    public float GetPlaybackSpeed()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerGetPlaybackSpeed(Handle);
    }

    public void SetPlaybackSpeed(float speed)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetPlaybackSpeed(Handle, speed);
    }

    public void ResetTransition()
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerResetTransition(Handle);
    }

    public bool IsLooping()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsLooping(Handle);
    }

    public void SetLooping(bool loop)
    {
        Debug.Assert(IsValid());
        CraPlaybackManager.Instance.PlayerSetLooping(Handle, loop);
    }

    public bool IsFinished()
    {
        Debug.Assert(IsValid());
        return CraPlaybackManager.Instance.PlayerIsFinished(Handle);
    }
}

public struct CraLayer
{
    public CraHandle Handle { get; private set; }


    public static CraLayer CreateNew(int maxStates)
    {
        return new CraLayer
        {
            Handle = CraAnimatorManager.Instance.LayerNew(maxStates)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public int AddState(CraPlayer state)
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.LayerAddState(Handle, state);
    }

    public CraPlayer GetCurrentState()
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.LayerGetCurrentState(Handle);
    }

    public int GetCurrentStateIdx()
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.LayerGetCurrentStateIdx(Handle);
    }

    public void SetState(int stateIdx)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerSetState(Handle, stateIdx);
    }

    public void CaptureBones()
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerCaptureBones(Handle);
    }

    public void RestartState()
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerRestartState(Handle);
    }

    public void TransitFromAboveLayer()
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerTransitFromAboveLayer(Handle);
    }

    public void SetPlaybackSpeed(int stateIdx, float playbackSpeed)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerSetPlaybackSpeed(Handle, stateIdx, playbackSpeed);
    }

    public void AddOnTransitFinishedListener(Action callback)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerAddOnTransitFinishedListener(Handle, callback);
    }

    public void AddOnStateFinishedListener(Action callback)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.LayerAddOnStateFinishedListener(Handle, callback);
    }
}

public struct CraAnimator
{
    CraHandle Handle;


    public static CraAnimator CreateNew(int numLayers, int maxStatesPerLayerCount)
    {
        return new CraAnimator
        {
            Handle = CraAnimatorManager.Instance.AnimatorNew(numLayers, maxStatesPerLayerCount)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public int AddState(int layer, CraPlayer state)
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.AnimatorAddState(Handle, layer, state);
    }

    public CraPlayer GetCurrentState(int layer)
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.AnimatorGetCurrentState(Handle, layer);
    }

    public int GetCurrentStateIdx(int layer)
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.AnimatorGetCurrentStateIdx(Handle, layer);
    }

    public void SetState(int layer, int stateIdx)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.AnimatorSetState(Handle, layer, stateIdx);
    }

    public void SetPlaybackSpeed(int layer, int stateIdx, float playbackSpeed)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.AnimatorSetPlaybackSpeed(Handle, layer, stateIdx, playbackSpeed);
    }

    public void RestartState(int layer)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.AnimatorRestartState(Handle, layer);
    }

    public void AddOnTransitFinishedListener(Action<int> callback)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.AnimatorAddOnTransitFinishedListener(Handle, callback);
    }

    public void AddOnStateFinishedListener(Action<int> callback)
    {
        Debug.Assert(Handle.IsValid());
        CraAnimatorManager.Instance.AnimatorAddOnStateFinishedListener(Handle, callback);
    }

    public int GetNumLayers()
    {
        Debug.Assert(Handle.IsValid());
        return CraAnimatorManager.Instance.AnimatorGetNumLayers(Handle);
    }
}