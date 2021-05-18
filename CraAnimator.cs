using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class CraLayer
{
    public int CurrentStateIdx { get; private set; } = CraSettings.STATE_NONE;

    public Action OnTransitFinished;
    public Action OnStateFinished;

    CraPlayer[] States = new CraPlayer[CraSettings.MAX_STATES];
    bool[] StateSlots = new bool[CraSettings.MAX_STATES];
    bool OnStateFinishedInvoked = false;

    public int AddState(CraPlayer state)
    {
        if (!StateSlots[0])
        {
            CurrentStateIdx = 0;
        }

        for (int i = 0; i < CraSettings.MAX_STATES; ++i)
        {
            if (!StateSlots[i])
            {
                States[i] = state;
                StateSlots[i] = true;
                return i;
            }
        }
        Debug.LogError($"No more state slots available! Consider increasing the max slots size of {CraSettings.MAX_STATES}.");
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
        if (!StateSlots[stateIdx]) return false;
        StateSlots[stateIdx] = false;
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
            States[stateIdx].Play();
            OnStateFinishedInvoked = false;
        }
    }

    public void SetPlaybackSpeed(int stateIdx, float playbackSpeed)
    {
        States[stateIdx].PlaybackSpeed = playbackSpeed;
    }

    public void Tick(float deltaTime)
    {
        if (CurrentStateIdx > -1)
        {
            CraPlayer state = States[CurrentStateIdx];
            state.Update(deltaTime);
            if (!OnStateFinishedInvoked && state.Finished)
            {
                OnStateFinished?.Invoke();
                OnStateFinishedInvoked = true;
            }
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
    }

    public void SetPlaybackSpeed(int layer, int stateIdx, float playbackSpeed)
    {
        Layers[layer].SetPlaybackSpeed(stateIdx, playbackSpeed);
    }

    public void Tick(float deltaTime)
    {
        for (int layer = 0; layer < CraSettings.STATE_MAX_LAYERS; ++layer)
        {
            Layers[layer].Tick(deltaTime);
        }
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