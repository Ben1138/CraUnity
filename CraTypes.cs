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
    public int Index { get; private set; }
    public static CraHandle Invalid => new CraHandle(-1);

    public CraHandle(int index)
    {
        Index = index;
    }

    public bool IsValid()
    {
        return Index >= 0;
    }

    public static bool operator ==(CraHandle lhs, CraHandle rhs)
    {
        return lhs.Index == rhs.Index;
    }

    public static bool operator !=(CraHandle lhs, CraHandle rhs)
    {
        return lhs.Index != rhs.Index;
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
    public CraSourceTransformCurve Curve;

    public CraBone(string boneName, CraSourceTransformCurve curve)
    {
        if (CraMain.Instance.Settings.BoneHashFunction == null)
        {
            throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        BoneHash = CraMain.Instance.Settings.BoneHashFunction(boneName);
        Curve = curve;
    }
}

public enum CraMaskOperation
{
    Intersection, // Assign bones defined in this mask, exclude all others
    Difference    // Assign ALL bones EXCEPT the ones defined in this mask
}

/// <summary>
/// Masks are inclusive, i.e. only assign bones to a clip specified in here
/// </summary>
public struct CraMask
{
    public CraMaskOperation Operation;
    public bool MaskChildren;
    public HashSet<int> BoneHashes;

    public CraMask(CraMaskOperation operation, bool maskChildren, params string[] boneNames)
    {
        if (CraMain.Instance.Settings.BoneHashFunction == null)
        {
            throw new Exception("CraMain.Instance.Settings.BoneHashFunction is not assigned! You need to assign a custom hash function!");
        }

        Operation = operation;
        BoneHashes = new HashSet<int>();
        for (int i = 0; i < boneNames.Length; ++i)
        {
            BoneHashes.Add(CraMain.Instance.Settings.BoneHashFunction(boneNames[i]));
        }
        MaskChildren = maskChildren;
    }
}

public enum CraValueType : byte
{
    Int,
    Float,
    Bool,
    Trigger
}

public enum CraConditionType
{
    None,

    // Value conditions
    Equal,
    Greater,
    Less,
    GreaterOrEqual,
    LessOrEqual,
    Flag,           // Value must contain at least one of the flags
    Flags,          // Value must contain all flags
    Trigger,

    // State conditions
    TimeMin,        // State Time must be greater than Min
    TimeMax,        // State Time must be smaller than Max
    IsFinished
}

public enum CraPlayMode
{
    Stop, Play, OneFrame
}

[StructLayout(LayoutKind.Explicit)]
public struct CraValueUnion
{
    [FieldOffset(0)]
    public CraValueType Type;
    //[FieldOffset(1)]
    //public byte TriggerConsumableCount;

    [FieldOffset(4)]
    public int ValueInt;
    [FieldOffset(4)]
    public float ValueFloat;
    [FieldOffset(4)]
    public bool ValueBool;

    public static bool operator ==(CraValueUnion lhs, CraValueUnion rhs)
    {
        return lhs.Type == rhs.Type && lhs.ValueInt == rhs.ValueInt;
    }

    public static bool operator !=(CraValueUnion lhs, CraValueUnion rhs)
    {
        return lhs.Type != rhs.Type || lhs.ValueInt != rhs.ValueInt;
    }
}

public struct CraWriteOutput
{
    public CraMachineValue MachineValue;
    public CraValueUnion Value;
}

public struct CraCondition
{
    public CraConditionType Type;
    public CraMachineValue Input;
    public CraValueUnion Compare;
    public bool CompareToAbsolute;

    public bool  ConditionMet;
}


[StructLayout(LayoutKind.Sequential)]
public struct CraConditionOr
{
    public float CheckTimeMin;    // Before this state time, conditions is always false.
    public float CheckTimeMax;    // After this state time, condition is always false.     Default value of 0f is interpreted as State Duration

    public CraCondition And0;
    public CraCondition And1;
    public CraCondition And2;
    public CraCondition And3;
    public CraCondition And4;
    public CraCondition And5;
    public CraCondition And6;
    public CraCondition And7;
    public CraCondition And8;
    public CraCondition And9;
}

[StructLayout(LayoutKind.Sequential)]
public struct CraTransitionData
{
    public CraState Target;
    public float TransitionTime;
    public CraConditionOr Or0;
    public CraConditionOr Or1;
    public CraConditionOr Or2;
    public CraConditionOr Or3;
    public CraConditionOr Or4;
    public CraConditionOr Or5;
    public CraConditionOr Or6;
    public CraConditionOr Or7;
    public CraConditionOr Or8;
    public CraConditionOr Or9;
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
        lock (CraMain.Instance.Lock)
        {
            Debug.Assert(Elements.IsCreated);
            int newLength = (int)(Elements.Length * GrowFactor);
            Debug.Log($"Growing CraBuffer from {Elements.Length} to {newLength}");

            Debug.Assert(newLength > Elements.Length);
            NativeArray<T> newElements = new NativeArray<T>(newLength, Allocator.Persistent);
            NativeArray<T> view = newElements.GetSubArray(0, Elements.Length);
            Elements.CopyTo(view);
            Elements.Dispose();
            Elements = newElements;
        }
    }
}

#if UNITY_EDITOR
public struct CraMeasure
{
    public int CurrentElements;
    public int MaxElements;
    public ulong CurrentBytes;
    public ulong MaxBytes;
}

public class CraStatistics
{
    public CraMeasure PlayerData;
    public CraMeasure ClipData;
    public CraMeasure BakedClipTransforms;
    public CraMeasure BoneData;
    public CraMeasure Bones;
    public CraMeasure Mapping;

    public CraMeasure StateMachines;
    public CraMeasure Inputs;
    public CraMeasure States;
    public CraMeasure Transitions;
}
#endif

public struct CraBufferSettings
{
    public int Capacity;
    public float GrowFactor;
}

public struct CraPlayRange
{
    public float MinTime;
    public float MaxTime;
}

public struct CraSettings
{
    public CraBufferSettings Players;
    public CraBufferSettings Clips;
    public CraBufferSettings ClipTransforms;
    public CraBufferSettings Bones;
    public int MaxBones;

    public CraBufferSettings StateMachines;
    public CraBufferSettings MachineValues;
    public CraBufferSettings States;
    public CraBufferSettings Transitions;

    public const int MaxTransitions = 20;
    public const int MaxLayers = 2;
    public const int MaxMachineValues = 128;

    public Func<string, int> BoneHashFunction;
}

public struct CraClip
{
    public CraHandle Handle { get; private set; }
    public static CraClip None => new CraClip { Handle = CraHandle.Invalid };

    public static CraClip CreateNew(CraSourceClip srcClip)
    {
        return new CraClip
        {
            Handle = CraMain.Instance.Players.Clip_New(srcClip)
        };
    }

    public CraClip(CraHandle clipHandle)
    {
        Handle = clipHandle;
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public float GetDuration()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Clip_GetDuration(Handle);
    }

    public int GetFrameCount()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Clip_GetFrameCount(Handle);
    }

    public float GetFPS()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Clip_GetFPS(Handle);
    }

    public static bool operator ==(CraClip lhs, CraClip rhs)
    {
        return lhs.Handle == rhs.Handle;
    }

    public static bool operator !=(CraClip lhs, CraClip rhs)
    {
        return lhs.Handle != rhs.Handle;
    }
}

public struct CraPlayer
{
    public CraHandle Handle { get; private set; }
    public static CraPlayer None => new CraPlayer { Handle = CraHandle.Invalid };

    public static CraPlayer CreateNew()
    {
        return new CraPlayer
        {
            Handle = CraMain.Instance.Players.Player_New()
        };
    }

    public CraPlayer(CraHandle playerHandle)
    {
        Handle = playerHandle;
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
        CraMain.Instance.Players.Player_SetClip(Handle, clip.Handle);
    }

    public CraClip GetClip()
    {
        Debug.Assert(IsValid());
        return new CraClip(CraMain.Instance.Players.Player_GetClip(Handle));
    }

    public void Assign(Transform root, CraMask? mask = null)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_Assign(Handle, root, mask);
    }

    public int GetAssignedBonesCount()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetAssignedBonesCount(Handle);
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

    public void SetPlay(CraPlayMode mode = CraPlayMode.Play, float transitionTime=0.0f)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetPlay(Handle, mode, transitionTime);
    }

    public void SetPlayRange(in CraPlayRange range)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetPlayRange(Handle, range);
    }

    public CraPlayRange GetPlayRange()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetPlayRange(Handle);
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

    public float GetTime()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.Players.Player_GetTime(Handle);
    }

    public void SetTime(float time)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.Players.Player_SetTime(Handle, time);
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

    public static CraState CreateNew(CraPlayer player, CraStateMachine machine, CraLayer layer)
    {
        CraHandle h = CraMain.Instance.StateMachines.State_New(player.Handle, machine.Handle, layer.Handle);
        return new CraState
        {
            Handle = h
        };
    }

    public CraState(CraHandle stateHandle)
    {
        Handle = stateHandle;
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public CraTransition NewTransition(in CraTransitionData transition)
    {
        Debug.Assert(IsValid());
        return new CraTransition(CraMain.Instance.StateMachines.Transition_New(Handle, in transition));
    }

    public CraTransition[] GetTransitions()
    {
        Debug.Assert(IsValid());
        CraHandle[] handles = CraMain.Instance.StateMachines.State_GetTransitions(Handle);
        CraTransition[] transitions = new CraTransition[handles.Length];
        for (int i = 0; i < handles.Length; ++i)
        {
            transitions[i] = new CraTransition(handles[i]);
        }
        return transitions;
    }

    public void SetSyncState(CraState syncState)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.State_SetSyncState(Handle, syncState.Handle);
    }

    public CraState GetSyncState()
    {
        Debug.Assert(IsValid());
        return new CraState(CraMain.Instance.StateMachines.State_GetSyncState(Handle));
    }

    public void WriteOnEnter(CraWriteOutput write)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.State_WriteOutputOnEnter(Handle, write.MachineValue.Handle, write.Value);
    }

    public void WriteOnLeave(CraWriteOutput write)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.State_WriteOutputOnLeave(Handle, write.MachineValue.Handle, write.Value);
    }

    public CraPlayer GetPlayer()
    {
        Debug.Assert(IsValid());
        return new CraPlayer(CraMain.Instance.StateMachines.State_GetPlayer(Handle));
    }

    public static bool operator ==(CraState lhs, CraState rhs)
    {
        return lhs.Handle == rhs.Handle;
    }

    public static bool operator !=(CraState lhs, CraState rhs)
    {
        return lhs.Handle != rhs.Handle;
    }

#if UNITY_EDITOR
    public void SetName(string name)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.SetStateName(Handle, name);
    }
    public string GetName()
    {
        Debug.Assert(IsValid());
        string name = CraMain.Instance.StateMachines.GetStateName(Handle);
        if (string.IsNullOrEmpty(name))
        {
            name = $"State {Handle.Index}";
        }
        return name;
    }
#endif
}

public struct CraMachineValue
{
    public CraHandle Handle { get; private set; }

    public static CraMachineValue None => new CraMachineValue { Handle = CraHandle.Invalid };

    public static CraMachineValue CreateNew(CraStateMachine stateMachine, CraValueType type, string name=null)
    {
        CraHandle h = CraMain.Instance.StateMachines.StateMachine_NewValue(stateMachine.Handle, type);
#if UNITY_EDITOR
        CraMain.Instance.StateMachines.SetMachineValueName(h, name);
#endif
        return new CraMachineValue
        {
            Handle = h
        };
    }

    public CraMachineValue(CraHandle inputHandle, string name=null)
    {
        Handle = inputHandle;
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public CraValueUnion GetValue()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetValue(Handle);
    }

    public int GetInt()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetValueInt(Handle);
    }

    public float GetFloat()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetValueFloat(Handle);
    }

    public bool GetBool()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetValueBool(Handle);
    }

    public void SetInt(int value)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.MachineValue_SetValueInt(Handle, value);
    }

    public void SetFloat(float value)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.MachineValue_SetValueFloat(Handle, value);
    }

    public void SetBool(bool value)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.MachineValue_SetValueBool(Handle, value);
    }

    public void SetTrigger(bool value)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.MachineValue_SetValueTrigger(Handle, value);
    }

    public float GetTriggerLifeTime()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetTriggerLifeTime(Handle);
    }

    public float GetTriggerMaxLifeTime()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.MachineValue_GetTriggerMaxLifeTime(Handle);
    }

    public void SetTriggerMaxLifeTime(float maxLifeTime)
    {
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.MachineValue_SetTriggerMaxLifeTime(Handle, maxLifeTime);
    }

#if UNITY_EDITOR
    public string GetName()
    {
        Debug.Assert(IsValid());
        string name = CraMain.Instance.StateMachines.GetMachineValueName(Handle);
        if (string.IsNullOrEmpty(name))
        {
            name = $"Input {Handle.Index}";
        }
        return name;
    }
#endif
}

public struct CraTransition
{
    public CraHandle Handle { get; private set; }

    public CraTransition(CraHandle transitionHandle)
    {
        Handle = transitionHandle;
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public CraTransitionData GetData()
    {
        Debug.Assert(IsValid());
        return CraMain.Instance.StateMachines.Transition_GetData(Handle);
    }
}

public struct CraLayer
{
    public CraHandle Handle { get; private set; }
    CraStateMachine Owner;

    public static CraLayer None => new CraLayer { Handle = CraHandle.Invalid };

    public static CraLayer CreateNew(CraStateMachine stateMachine)
    {
        return new CraLayer
        {
            Owner = stateMachine,
            Handle = CraMain.Instance.StateMachines.Layer_New(stateMachine.Handle),
        };
    }

    public CraLayer(CraStateMachine owner, CraHandle layerHandle)
    {
        Owner = owner;
        Handle = layerHandle;
    }

    public bool IsValid()
    {
        return Handle.IsValid();
    }

    public CraState NewState(CraPlayer player)
    {
        Debug.Assert(IsValid());
        Debug.Assert(Owner.IsValid());
        return CraState.CreateNew(player, Owner, this);
    }

    public CraState GetActiveState()
    {
        Debug.Assert(IsValid());
        Debug.Assert(Owner.IsValid());
        return new CraState(CraMain.Instance.StateMachines.Layer_GetActiveState(Owner.Handle, Handle));
    }

    public void SetActiveState(CraState state, float transitionTime= 0f)
    {
        Debug.Assert(IsValid());
        Debug.Assert(Owner.IsValid());
        CraMain.Instance.StateMachines.Layer_SetActiveState(Owner.Handle, Handle, state.Handle, transitionTime);
    }
#if UNITY_EDITOR
    public CraState[] GetAllStates()
    {
        CraHandle[] handles = CraMain.Instance.StateMachines.Layer_GetAllStates(Owner.Handle, Handle);
        CraState[] states = new CraState[handles.Length];
        for (int i = 0; i < handles.Length; ++i)
        {
            states[i] = new CraState(handles[i]);
        }
        return states;
    }
#endif
}

public struct CraStateMachine
{
    public CraHandle Handle { get; private set; }
    public static CraStateMachine None => new CraStateMachine { Handle = CraHandle.Invalid };

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
        Debug.Assert(IsValid());
        CraMain.Instance.StateMachines.StateMachine_SetActive(Handle, active);
    }


    public CraLayer NewLayer()
    {
        Debug.Assert(IsValid());
        return CraLayer.CreateNew(this);
    }

    public CraMachineValue NewMachineValue(CraValueType type, string name=null)
    {
        Debug.Assert(IsValid());
        return CraMachineValue.CreateNew(this, type, name);
    }

    // CAUTION: CraLayer handles are local to the current state machine!
    public CraLayer[] GetLayers()
    {
        Debug.Assert(IsValid());
        CraHandle[] handles = CraMain.Instance.StateMachines.StateMachine_GetLayers(Handle);
        CraLayer[] layers = new CraLayer[handles.Length];
        for (int i = 0; i < handles.Length; ++i)
        {
            layers[i] = new CraLayer(this, handles[i]);
        }
        return layers;
    }

    public CraMachineValue[] GetMachineValues()
    {
        Debug.Assert(IsValid());
        CraHandle[] handles = CraMain.Instance.StateMachines.StateMachine_GetValues(Handle);
        CraMachineValue[] inputs = new CraMachineValue[handles.Length];
        for (int i = 0; i < handles.Length; ++i)
        {
            inputs[i] = new CraMachineValue(handles[i]);
        }
        return inputs;
    }
}