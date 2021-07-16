using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Jobs;
using UnityEngine.Profiling;
using UnityEngine.Jobs;



public struct CraLayer
{
    public Action OnTransitFinished;
    public Action OnStateFinished;

    public int CurrentStateIdx { get; private set; }
    public CraPlayer[] States { get; private set; }

    bool OnStateFinishedInvoked;


    public static CraLayer CreateNew(int maxStateCount)
    {
        CraLayer layer = new CraLayer();
        layer.States = new CraPlayer[maxStateCount];
        layer.OnStateFinishedInvoked = false;
        layer.CurrentStateIdx = CraSettings.STATE_NONE;
        for (int i = 0; i < layer.States.Length; ++i)
        {
            layer.States[i] = CraPlayer.CreateEmpty();
        }
        return layer;
    }

    public int AddState(CraPlayer state)
    {
        for (int i = 0; i < States.Length; ++i)
        {
            if (!States[i].IsValid())
            {
                States[i] = state;
                return i;
            }
        }

        Debug.LogError($"No more state slots available! Consider increasing the max slots size of {States.Length}");
        return CraSettings.STATE_NONE;
    }

    public CraPlayer GetCurrentState()
    {
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            Debug.Assert(States[CurrentStateIdx].IsValid());
            return States[CurrentStateIdx];
        }
        return CraPlayer.CreateEmpty();
    }

    public bool RemoveState(int stateIdx)
    {
        Debug.Assert(stateIdx >= 0 && stateIdx < States.Length);
        if (!States[stateIdx].IsValid())
        {
            return false;
        }
        States[stateIdx] = CraPlayer.CreateEmpty();
        return true;
    }

    public void SetState(int stateIdx)
    {
        if (stateIdx == CurrentStateIdx) return;

        Debug.Assert(stateIdx < States.Length);
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            States[CurrentStateIdx].Reset();
        }
        CurrentStateIdx = stateIdx;
        if (stateIdx != CraSettings.STATE_NONE)
        {
            States[stateIdx].CaptureBones();
            States[stateIdx].Play(true);
            OnStateFinishedInvoked = false;
        }
    }

    public void CaptureBones()
    {
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            States[CurrentStateIdx].CaptureBones();
        }
    }

    public void RestartState()
    {
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            States[CurrentStateIdx].Play(true);
        }
    }

    public void TransitFromAboveLayer()
    {
        Debug.Assert(CurrentStateIdx != CraSettings.STATE_NONE);
        States[CurrentStateIdx].ResetTransition();
    }

    public void SetPlaybackSpeed(int stateIdx, float playbackSpeed)
    {
        States[stateIdx].SetPlaybackSpeed(playbackSpeed);
    }

    public void Tick(float deltaTime)
    {
        if (CurrentStateIdx > -1)
        {
            Profiler.BeginSample("Retrieve current state " + CurrentStateIdx);
            CraPlayer state = States[CurrentStateIdx];
            if (!OnStateFinishedInvoked && state.IsFinished())
            {
                OnStateFinished?.Invoke();
                OnStateFinishedInvoked = true;
            }
            Profiler.EndSample();
        }
    }
}

public struct CraAnimator
{
    public Action<int> OnTransitFinished;
    public Action<int> OnStateFinished;

    CraLayer[] Layers;


    public CraAnimator CreateNew(int maxLayerCount, int maxStatesPerLayerCount)
    {
        CraAnimator anim = new CraAnimator();
        anim.Layers = new CraLayer[maxLayerCount];
        for (int layer = 0; layer < anim.Layers.Length; ++layer)
        {
            int idx = layer;
            Layers[layer] = new CraLayer();
            Layers[layer].OnStateFinished += () => OnStateFinished?.Invoke(idx);
            Layers[layer].OnTransitFinished += () => OnTransitFinished?.Invoke(idx);
        }
        return anim;
    }

    public int AddState(int layer, CraPlayer state)
    {
        if (!state.IsValid())
        {
            return CraSettings.STATE_NONE;
        }

        return Layers[layer].AddState(state);
    }

    public CraPlayer GetCurrentState(int layer)
    {
        return Layers[layer].GetCurrentState();
    }

    public int GetCurrentStateIdx(int layer)
    {
        return Layers[layer].CurrentStateIdx;
    }

    public bool RemoveState(int layer, int stateIdx)
    {
        return Layers[layer].RemoveState(stateIdx);
    }

    public void SetState(int layer, int stateIdx)
    {
        Layers[layer].SetState(stateIdx);

        // if some layer in between gets removed (1.g. 1), the next layer
        // below it (e.g. 0) should do a transition, since it now has
        // potentially authority of more bones again
        if (stateIdx == CraSettings.STATE_NONE)
        {
            for (int i = layer - 1; i >= 0; --i)
            {
                if (Layers[i].CurrentStateIdx != CraSettings.STATE_NONE)
                {
                    Layers[i].TransitFromAboveLayer();
                    break;
                }
            }
        }

        // rebuild the bone authority
        for (int i = 0; i < CraSettings.MAX_LAYERS; ++i)
        {
            Layers[i].CaptureBones();
        }
    }

    public void SetPlaybackSpeed(int layer, int stateIdx, float playbackSpeed)
    {
        Layers[layer].SetPlaybackSpeed(stateIdx, playbackSpeed);
    }

    public void RestartState(int layer)
    {
        Layers[layer].RestartState();
    }

    public void Tick(float deltaTime)
    {
        for (int layer = 0; layer < CraSettings.MAX_LAYERS; ++layer)
        {
            //Profiler.BeginSample("Tick Layer " + layer);
            Layers[layer].Tick(deltaTime);
            //Profiler.EndSample();
        }
    }

    public CraPlayer[] GetAllStates()
    {
        List<CraPlayer> states = new List<CraPlayer>();
        for (int layerIdx = 0; layerIdx < CraSettings.MAX_LAYERS; ++layerIdx)
        {
            for (int stateIdx = 0; stateIdx < Layers[layerIdx].States.Length; ++stateIdx)
            {
                if (Layers[layerIdx].States[stateIdx].IsValid())
                {
                    states.Add(Layers[layerIdx].States[stateIdx]);
                }
            }
        }
        return states.ToArray();
    }
}

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

public class CraStatistics
{
    public CraMeasure PlayerData;
    public CraMeasure ClipData;
    public CraMeasure BakedClipTransforms;
    public CraMeasure BoneData;
    public CraMeasure Bones;
}