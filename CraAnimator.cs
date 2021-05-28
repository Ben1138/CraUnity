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

using UnityEditor;


public class CraLayer
{
    public int CurrentStateIdx { get; private set; } = CraSettings.STATE_NONE;

    public Action OnTransitFinished;
    public Action OnStateFinished;

    public CraPlayer[] States { get; private set; } = new CraPlayer[CraSettings.MAX_STATES];
    bool OnStateFinishedInvoked = false;

    static int COUNT = 0;

    public int AddState(CraPlayer state)
    {
        if (States[0] == null)
        {
            CurrentStateIdx = 0;
        }
        for (int i = 0; i < CraSettings.MAX_STATES; ++i)
        {
            if (States[i] == null)
            {
                States[i] = state;
                return i;
            }
        }

        Debug.LogError($"No more state slots available! Consider increasing the max slots size of {CraSettings.MAX_STATES}");
        return CraSettings.STATE_NONE;
    }

    public CraPlayer GetCurrentState()
    {
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            return States[CurrentStateIdx];
        }
        return null;
    }

    public bool RemoveState(int stateIdx)
    {
        Debug.Assert(stateIdx >= 0 && stateIdx < CraSettings.MAX_STATES);
        if (States[stateIdx] == null)
        {
            return false;
        }
        States[stateIdx] = null;
        return true;
    }

    public void SetState(int stateIdx)
    {
        if (stateIdx == CurrentStateIdx) return;

        Debug.Assert(stateIdx < CraSettings.MAX_STATES);
        if (CurrentStateIdx != CraSettings.STATE_NONE)
        {
            States[CurrentStateIdx].Reset();
        }
        CurrentStateIdx = stateIdx;
        if (stateIdx != CraSettings.STATE_NONE)
        {
            States[stateIdx].Play(true);
            OnStateFinishedInvoked = false;
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
            Profiler.EndSample();

            Profiler.BeginSample("Retrieve current state " + CurrentStateIdx);
            if (!OnStateFinishedInvoked && state.IsFinished())
            {
                OnStateFinished?.Invoke();
                OnStateFinishedInvoked = true;
            }
            Profiler.EndSample();

            //Profiler.BeginSample("CaptureBones");
            state.CaptureBones();
            //Profiler.EndSample();
        }
    }
}

public class CraAnimator : MonoBehaviour
{
    public Action<int> OnTransitFinished;
    public Action<int> OnStateFinished;

    protected CraLayer[] Layers = new CraLayer[CraSettings.STATE_MAX_LAYERS];


    public int AddState(int layer, CraPlayer state)
    {
        return Layers[layer].AddState(state);
    }

    public CraPlayer GetCurrentState(int layer)
    {
        return Layers[layer].GetCurrentState();
    }

    public bool RemoveState(int layer, int stateIdx)
    {
        return Layers[layer].RemoveState(stateIdx);
    }

    public void SetState(int layer, int stateIdx)
    {
        Layers[layer].SetState(stateIdx);

        if (stateIdx == CraSettings.STATE_NONE)
        {
            for (int i = layer; i >= 0; --i)
            {
                if (Layers[i].CurrentStateIdx != CraSettings.STATE_NONE)
                {
                    Layers[i].TransitFromAboveLayer();
                    break;
                }
            }
        }
    }

    public void SetPlaybackSpeed(int layer, int stateIdx, float playbackSpeed)
    {
        Layers[layer].SetPlaybackSpeed(stateIdx, playbackSpeed);
    }

    public void Tick(float deltaTime)
    {
        for (int layer = 0; layer < CraSettings.STATE_MAX_LAYERS; ++layer)
        {
            Profiler.BeginSample("Tick Layer " + layer);
            Layers[layer].Tick(deltaTime);
            Profiler.EndSample();
        }
    }

    public CraPlayer[] GetAllStates()
    {
        List<CraPlayer> states = new List<CraPlayer>();
        for (int layerIdx = 0; layerIdx < CraSettings.STATE_MAX_LAYERS; ++layerIdx)
        {
            for (int stateIdx = 0; stateIdx < Layers[layerIdx].States.Length; ++stateIdx)
            {
                if (Layers[layerIdx].States[stateIdx] != null)
                {
                    states.Add(Layers[layerIdx].States[stateIdx]);
                }
            }
        }
        return states.ToArray();
    }

    void Awake()
    {
        for (int layer = 0; layer < CraSettings.STATE_MAX_LAYERS; ++layer)
        {
            int idx = layer;
            Layers[layer] = new CraLayer();
            Layers[layer].OnStateFinished += () => OnStateFinished?.Invoke(idx);
            Layers[layer].OnTransitFinished += () => OnTransitFinished?.Invoke(idx);
        }
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
}