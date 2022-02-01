using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;


// Debug only
public interface ICraAnimated
{
    CraStateMachine GetStateMachine();
}

public struct CraHandle
{
    public int Internal { get; private set; }

    public CraHandle(int internalHandle)
    {
        Internal = internalHandle;
    }

    public bool IsValid()
    {
        return Internal >= 0;
    }

    public static CraHandle Invalid => new CraHandle(-1);
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
        if (CraMain.Instance.Settings.BoneHashFunction == null)
        {
            throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        BoneHash = CraMain.Instance.Settings.BoneHashFunction(boneName);
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
        if (CraMain.Instance.Settings.BoneHashFunction == null)
        {
            throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        BoneHashes = new HashSet<int>();
        for (int i = 0; i < boneNames.Length; ++i)
        {
            BoneHashes.Add(CraMain.Instance.Settings.BoneHashFunction(boneNames[i]));
        }
        MaskChildren = maskChildren;
    }
}

public enum CraValueType : int
{
    Int,
    Float,
    Bool
}

public enum CraCondition
{
    Equal,
    Greater,
    Less,
    GreaterOrEqual,
    LessOrEqual,

    Trigger,
    IsFinished
}

[StructLayout(LayoutKind.Explicit)]
public struct CraValueUnion
{
    [FieldOffset(0)]
    public CraValueType Type;

    [FieldOffset(4)]
    public int ValueInt;
    [FieldOffset(4)]
    public float ValueFloat;
    [FieldOffset(4)]
    public bool ValueBool;
}

public struct CraTransitionCondition
{
    public CraCondition Condition;
    public CraInput Input;
    public CraValueUnion Value;
    public bool ValueAsAbsolute;
}

public struct CraTransition
{
    public CraState Target;
    public CraTransitionCondition Condition;
}

public class CraBuffer<T> where T : struct
{
    NativeArray<T> Elements;
    int Head;
    float GrowFactor;

    ~CraBuffer()
    {
        Destroy();
    }

    public CraBuffer(in CraBufferSettings settings)
    {
        GrowFactor = settings.GrowFactor;
        Elements = new NativeArray<T>(settings.Capacity, Allocator.Persistent);
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
            if (GrowFactor > 1.0f)
            {
                Grow();
            }
            else
            {
                Debug.LogError($"Max capacity of {Elements.Length} reached!");
                return -1;
            }
        }
        return Head++;
    }

    public bool Alloc(int count)
    {
        Debug.Assert(count > 0);

        int space = Elements.Length - (Head + count);
        if (GrowFactor > 1.0f)
        {
            while (space < 0)
            {
                Grow();
                space = Elements.Length - (Head + count);
            }
        }
        else if (space < 0)
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

    public void Set(int index, in T value)
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

    public void Destroy()
    {
        Head = 0;
        Elements.Dispose();
    }

    void Grow()
    {
        Debug.Assert(Elements.IsCreated);
        Debug.Assert(Elements.Length * GrowFactor > Elements.Length);
        NativeArray<T> tmp = new NativeArray<T>((int)(Elements.Length * GrowFactor), Allocator.Persistent);
        Elements.Dispose();
        Elements = tmp;
    }
}

#if UNITY_EDITOR
public class CraStatistics
{
    public CraMeasure PlayerData;
    public CraMeasure ClipData;
    public CraMeasure BakedClipTransforms;
    public CraMeasure BoneData;
    public CraMeasure Bones;
}
#endif

public struct CraBufferSettings
{
    public int Capacity;
    public float GrowFactor;
}

public struct CraSettings
{
    public CraBufferSettings Players;
    public CraBufferSettings Clips;
    public CraBufferSettings ClipTransforms;
    public CraBufferSettings Bones;

    public CraBufferSettings StateMachines;
    public CraBufferSettings Inputs;
    public CraBufferSettings States;
    public CraBufferSettings Transitions;

    public int MaxBones;

    public const int MaxTransitions = 5;
    public const int MaxLayers = 5;
    public const int MaxInputs = 5;

    public Func<string, int> BoneHashFunction;
}

public struct CraPlayer
{
    public CraHandle Handle { get; private set; }

    public static CraPlayer CreateNew()
    {
        return new CraPlayer
        {
            Handle = CraMain.Instance.Players.Player_New()
        };
    }

    public static CraPlayer CreateEmpty()
    {
        return new CraPlayer
        {
            Handle = CraHandle.Invalid
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public void SetClip(CraClip clip)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetClip(Handle, clip);
    }

    public void Assign(Transform root, CraMask? mask = null)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_Assign(Handle, root, mask);
    }

    public void CaptureBones()
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_CaptureBones(Handle);
    }

    public void Reset()
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_Reset(Handle);
    }

    public bool IsPlaying()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_IsPlaying(Handle);
    }

    public float GetDuration()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetDuration(Handle);
    }

    public void Play(float transitionTime=0.0f)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_Play(Handle, transitionTime);
    }

    public float GetPlayback()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetPlayback(Handle);
    }

    public float GetPlaybackSpeed()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetPlaybackSpeed(Handle);
    }

    public void SetPlaybackSpeed(float speed)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetPlaybackSpeed(Handle, speed);
    }

    public void ResetTransition()
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_ResetTransition(Handle);
    }

    public bool IsLooping()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_IsLooping(Handle);
    }

    public void SetLooping(bool loop)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetLooping(Handle, loop);
    }

    public bool IsFinished()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_IsFinished(Handle);
    }
}

public struct CraState
{
    public CraHandle Handle { get; private set; }

    public static CraState None => new CraState { Handle = CraHandle.Invalid };

    public static CraState CreateNew(CraPlayer player)
    {
        return new CraState
        {
            Handle = CraMain.Instance.StateMachines.State_New(player.Handle)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public bool NewTransition(CraState toState, in CraTransitionCondition condition)
    {
        return CraMain.Instance.StateMachines.Transition_New(Handle, toState.Handle, condition).IsValid();
    }
}

public struct CraInput
{
    public CraHandle Handle { get; private set; }


    public static CraInput CreateNew(CraStateMachine stateMachine)
    {
        return new CraInput
        {
            Handle = CraMain.Instance.StateMachines.Inputs_New(stateMachine.Handle)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public void SetInt(int value)
    {
        CraMain.Instance.StateMachines.Inputs_SetValueInt(Handle, value);
    }

    public void SetFloat(float value)
    {
        CraMain.Instance.StateMachines.Inputs_SetValueFloat(Handle, value);
    }

    public void SetBool(bool value)
    {
        CraMain.Instance.StateMachines.Inputs_SetValueBool(Handle, value);
    }
}

public struct CraLayer
{
    public CraHandle Handle { get; private set; }
    CraStateMachine Owner;

    public static CraLayer None => new CraLayer { Handle = CraHandle.Invalid };

    public static CraLayer CreateNew(CraStateMachine stateMachine, CraState activeState)
    {
        return new CraLayer
        {
            Owner = stateMachine,
            Handle = CraMain.Instance.StateMachines.Layer_New(stateMachine.Handle, activeState.Handle)
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public void SetActiveState(CraState state)
    {
        Debug.Assert(Owner.IsValid());
        CraMain.Instance.StateMachines.Layer_SetActiveState(Owner.Handle, Handle, state.Handle);
    }
}

public struct CraStateMachine
{
    public CraHandle Handle { get; private set; }

    public static CraStateMachine CreateNew()
    {
        return new CraStateMachine
        {
            Handle = CraMain.Instance.StateMachines.StateMachine_New()
        };
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public void SetActive(bool active)
    {
        CraMain.Instance.StateMachines.StateMachine_SetActive(Handle, active);
    }


    public CraLayer NewLayer(CraState activeState)
    {
        return CraLayer.CreateNew(this, activeState);
    }

    public CraInput NewInput()
    {
        return CraInput.CreateNew(this);
    }
}

//public struct CraLayer
//{
//    public CraHandle Handle { get; private set; }


//    public static CraLayer CreateNew(int maxStates)
//    {
//        return new CraLayer
//        {
//            Handle = CraMain.Instance.StateMachine.LayerNew(maxStates)
//        };
//    }

//    public bool IsValid()
//    {
//        return Handle.IsValid();
//    }

//    public int AddState(CraPlayer state)
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.LayerAddState(Handle, state);
//    }

//    public CraPlayer GetCurrentState()
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.LayerGetCurrentState(Handle);
//    }

//    public int GetCurrentStateIdx()
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.LayerGetCurrentStateIdx(Handle);
//    }

//    public void SetState(int stateIdx)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerSetState(Handle, stateIdx);
//    }

//    public void CaptureBones()
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerCaptureBones(Handle);
//    }

//    public void RestartState()
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerRestartState(Handle);
//    }

//    public void TransitFromAboveLayer()
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerTransitFromAboveLayer(Handle);
//    }

//    public void SetPlaybackSpeed(int stateIdx, float playbackSpeed)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerSetPlaybackSpeed(Handle, stateIdx, playbackSpeed);
//    }

//    public void AddOnTransitFinishedListener(Action callback)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerAddOnTransitFinishedListener(Handle, callback);
//    }

//    public void AddOnStateFinishedListener(Action callback)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.LayerAddOnStateFinishedListener(Handle, callback);
//    }
//}

//public struct CraAnimator
//{
//    CraHandle Handle;


//    public static CraAnimator CreateNew(int numLayers, int maxStatesPerLayerCount)
//    {
//        return new CraAnimator
//        {
//            Handle = CraMain.Instance.StateMachine.AnimatorNew(numLayers, maxStatesPerLayerCount)
//        };
//    }

//    public bool IsValid()
//    {
//        return Handle.IsValid();
//    }

//    public int AddState(int layer, CraPlayer state)
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.AnimatorAddState(Handle, layer, state);
//    }

//    public CraPlayer GetCurrentState(int layer)
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.AnimatorGetCurrentState(Handle, layer);
//    }

//    public int GetCurrentStateIdx(int layer)
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.AnimatorGetCurrentStateIdx(Handle, layer);
//    }

//    public void SetState(int layer, int stateIdx)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.AnimatorSetState(Handle, layer, stateIdx);
//    }

//    public void SetPlaybackSpeed(int layer, int stateIdx, float playbackSpeed)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.AnimatorSetPlaybackSpeed(Handle, layer, stateIdx, playbackSpeed);
//    }

//    public void RestartState(int layer)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.AnimatorRestartState(Handle, layer);
//    }

//    public void AddOnTransitFinishedListener(Action<int> callback)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.AnimatorAddOnTransitFinishedListener(Handle, callback);
//    }

//    public void AddOnStateFinishedListener(Action<int> callback)
//    {
//        Debug.Assert(Handle.IsValid());
//        CraMain.Instance.StateMachine.AnimatorAddOnStateFinishedListener(Handle, callback);
//    }

//    public int GetNumLayers()
//    {
//        Debug.Assert(Handle.IsValid());
//        return CraMain.Instance.StateMachine.AnimatorGetNumLayers(Handle);
//    }
//}