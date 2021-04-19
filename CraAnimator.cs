using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class CraStateTransition
{
    public float Time = 1f;
    public int[] Bones;
}

public struct CraState
{
    public CraPlayer Animation;
    public CraStateTransition[] _Transitions;

    public CraState(CraPlayer animation)
    {
        Animation = animation;
        _Transitions = new CraStateTransition[CraSettings.MAX_STATES];
    }
}

public struct CraPosRot
{
    public Vector3 Position;
    public Quaternion Rotation;

    public CraPosRot(Vector3 pos, Quaternion rot)
    {
        Position = pos;
        Rotation = rot;
    }
}

public class CraLayer
{
    public Dictionary<int, CraPosRot> BasePose;
    public int CurrentStateIdx = CraSettings.STATE_NONE;

    public Action OnTransitFinished;
    public Action OnStateFinished;

    public int BlendStateIdx = CraSettings.STATE_NONE;
    public float BlendValue;

    CraState[] States = new CraState[CraSettings.MAX_STATES];
    bool[] StateSlots = new bool[CraSettings.MAX_STATES];
    float TransitTimer;
    bool OnStateFinishedInvoked = false;

    public int AddState(CraState state)
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
            States[CurrentStateIdx].Animation.Reset();
        }
        CurrentStateIdx = stateIdx;
        if (stateIdx != CraSettings.STATE_NONE)
        {
            States[stateIdx].Animation.Play();
            OnStateFinishedInvoked = false;
        }
    }

    public void SetPlaybackSpeed(int stateIdx, float playbackSpeed)
    {
        States[stateIdx].Animation.PlaybackSpeed = playbackSpeed;
    }

    public void ConnectStates(int stateIdx1, int stateIdx2)
    {
        Debug.Assert(stateIdx1 >= 0 && stateIdx1 < CraSettings.MAX_STATES && StateSlots[stateIdx1]);
        Debug.Assert(stateIdx2 >= 0 && stateIdx2 < CraSettings.MAX_STATES && StateSlots[stateIdx2]);

        ref CraState state1 = ref States[stateIdx1];
        ref CraState state2 = ref States[stateIdx2];

        HashSet<int> bones = new HashSet<int>();
        bones.UnionWith(state1.Animation.AssignedIndices);
        bones.UnionWith(state2.Animation.AssignedIndices);

        CraStateTransition transition = new CraStateTransition();
        transition.Bones = bones.ToArray();

        state1._Transitions[stateIdx2] = transition;
        state2._Transitions[stateIdx1] = transition;
    }

    public void DisconnectStates(int stateIdx1, int stateIdx2)
    {
        Debug.Assert(stateIdx1 >= 0 && stateIdx1 < CraSettings.MAX_STATES && StateSlots[stateIdx1]);
        Debug.Assert(stateIdx2 >= 0 && stateIdx2 < CraSettings.MAX_STATES && StateSlots[stateIdx2]);

        ref CraState state1 = ref States[stateIdx1];
        ref CraState state2 = ref States[stateIdx2];

        state1._Transitions[stateIdx2] = null;
        state2._Transitions[stateIdx1] = null;
    }

    public void TransitTo(int stateIdx)
    {
        if (BlendStateIdx != CraSettings.STATE_NONE)
        {
            Debug.LogWarning("There's already a transit taking place!");
            return;
        }
        Debug.Assert(stateIdx != CraSettings.STATE_NONE && stateIdx < CraSettings.MAX_STATES && StateSlots[stateIdx]);

        ref CraState from = ref States[CurrentStateIdx];
        ref CraState to = ref States[BlendStateIdx];

        from.Animation.IsPlaying = false;
        to.Animation.IsPlaying = true;
        to.Animation.Playback = from.Animation.Playback;
        CurrentStateIdx = stateIdx;

        //if (from._Transitions[stateIdx] == null)
        //{
        //    Debug.LogError($"Cannot transit from '{CurrentStateIdx}' to '{stateIdx}', they are not connected!");
        //    return;
        //}

        //BlendStateIdx = stateIdx;
        //TransitTimer = 0f;

        //// from now on, WE are controlling the animation
        //from.Animation.IsPlaying = false;
        //to.Animation.IsPlaying = false;
        //to.Animation.Playback = 0f;
    }

    public void BlendTo(int stateIdx, float blend)
    {
        BlendStateIdx = stateIdx;
        BlendValue = blend;
    }

    public void Tick(float deltaTime)
    {
        if (BlendStateIdx != CraSettings.STATE_NONE)
        {
            ref CraState from = ref States[CurrentStateIdx];
            ref CraState to = ref States[BlendStateIdx];

            CraPlayer aFrom = from.Animation;
            CraPlayer aTo = to.Animation;

            aFrom.Playback += deltaTime;
            aTo.Playback += deltaTime;

            if (aFrom.Playback > aFrom.Duration)
            {
                aFrom.Playback = 0;
            }
            if (aTo.Playback > aTo.Duration)
            {
                aTo.Playback = 0;
            }

            int frameIdxFrom = Mathf.FloorToInt(aFrom.Playback * aFrom.Clip.Fps);
            int frameIdxTo = Mathf.FloorToInt(aTo.Playback * aTo.Clip.Fps);

            CraStateTransition transition = from._Transitions[BlendStateIdx];

            if (false)
            {
                BlendValue = TransitTimer / transition.Time;
            }

            Vector3 fromPos, toPos;
            Quaternion fromRot, toRot;

            for (int j = 0; j < transition.Bones.Length; ++j)
            {
                int idx = transition.Bones[j];

                if (aFrom.Clip.Bones[idx].Curve == null)
                {
                    fromPos = BasePose[idx].Position;
                    fromRot = BasePose[idx].Rotation;
                }
                else
                {
                    fromPos = aFrom.Clip.Bones[idx].Curve.BakedPositions[frameIdxFrom];
                    fromRot = aFrom.Clip.Bones[idx].Curve.BakedRotations[frameIdxFrom];
                }

                if (aTo.Clip.Bones[idx].Curve == null)
                {
                    toPos = BasePose[idx].Position;
                    toRot = BasePose[idx].Rotation;
                }
                else
                {
                    toPos = aFrom.Clip.Bones[idx].Curve.BakedPositions[frameIdxTo];
                    toRot = aFrom.Clip.Bones[idx].Curve.BakedRotations[frameIdxTo];
                }

                aFrom.AssignedBones[idx].localPosition = Vector3.Lerp(fromPos, toPos, BlendValue);
                aFrom.AssignedBones[idx].localRotation = Quaternion.Slerp(fromRot, toRot, BlendValue);
            }

            if (false)
            {
                TransitTimer += deltaTime;
                if (TransitTimer >= transition.Time)
                {
                    from.Animation.Reset();
                    to.Animation.IsPlaying = true;
                    CurrentStateIdx = BlendStateIdx;
                    BlendStateIdx = CraSettings.STATE_NONE;
                    OnTransitFinished?.Invoke();
                    OnStateFinishedInvoked = false;
                }
            }
        }
        else if (CurrentStateIdx > -1)
        {
            ref CraState state = ref States[CurrentStateIdx];
            state.Animation.Update(deltaTime);
            if (!OnStateFinishedInvoked && state.Animation.Finished)
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

    public CraLayer[] Layers = new CraLayer[CraSettings.STATE_LEVEL_COUNT];
    Dictionary<int, CraPosRot> BasePose = new Dictionary<int, CraPosRot>();


    public int AddState(int layer, CraState state)
    {
        return Layers[layer].AddState(state);
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

    public void ConnectStates(int layer, int stateIdx1, int stateIdx2)
    {
        Layers[layer].ConnectStates(stateIdx1, stateIdx2);
    }

    public void DisconnectStates(int layer, int stateIdx1, int stateIdx2)
    {
        Layers[layer].DisconnectStates(stateIdx1, stateIdx2);
    }

    public void TransitTo(int layer, int stateIdx)
    {
        Layers[layer].TransitTo(stateIdx);
    }

    public void BlendTo(int layer, int stateIdx, float blend)
    {
        Layers[layer].BlendTo(stateIdx, blend);
    }

    public void Tick(float deltaTime)
    {
        for (int layer = 0; layer < CraSettings.STATE_LEVEL_COUNT; ++layer)
        {
            Layers[layer].Tick(deltaTime);
        }
    }

    void Awake()
    {
        void Parse(Transform t)
        {
            BasePose.Add(CraSettings.BoneHashFunction(t.name), new CraPosRot(t.localPosition, t.localRotation));
            for (int i = 0; i < t.childCount; ++i)
            {
                Parse(t.GetChild(i));
            }
        }
        Parse(transform);

        for (int layer = 0; layer < CraSettings.STATE_LEVEL_COUNT; ++layer)
        {
            int idx = layer;
            Layers[layer] = new CraLayer();
            Layers[layer].OnStateFinished += () => OnStateFinished?.Invoke(idx);
            Layers[layer].OnTransitFinished += () => OnTransitFinished?.Invoke(idx);
        }
    }
}