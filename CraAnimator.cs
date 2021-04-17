using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

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
        _Transitions = new CraStateTransition[CraAnimator.MAX_STATES];
    }
}

public class CraAnimator : MonoBehaviour
{
    public const int MAX_STATES = 32;

    public Action OnTransitFinished;
    public Action OnStateFinished;

    public int CurrentStateIdx { get; private set; } = -1;

    struct PosRot
    {
        public Vector3 Position;
        public Quaternion Rotation;

        public PosRot(Vector3 pos, Quaternion rot)
        {
            Position = pos;
            Rotation = rot;
        }
    }

    Dictionary<int, PosRot> BasePose = new Dictionary<int, PosRot>();
    CraState[] States = new CraState[MAX_STATES];
    bool[] StateSlots = new bool[MAX_STATES];
    int TransitToIdx = -1;
    float TransitTimer;
    bool OnStateFinishedInvoked = false;


    public int AddState(CraState state)
    {
        Debug.Assert(state.Animation.AssignedTo == transform);
        if (!StateSlots[0])
        {
            CurrentStateIdx = 0;
        }

        for (int i = 0; i < MAX_STATES; ++i)
        {
            if (!StateSlots[i])
            {
                States[i] = state;
                StateSlots[i] = true;
                return i;
            }
        }
        Debug.LogError($"No more state slots available! Consider increasing the max slots size of {MAX_STATES}.");
        return -1;
    }

    public bool RemoveState(int stateIdx)
    {
        Debug.Assert(stateIdx >= 0 && stateIdx < MAX_STATES);
        if (!StateSlots[stateIdx]) return false;
        StateSlots[stateIdx] = false;
        return true;
    }

    public void SetState(int stateIdx)
    {
        Debug.Assert(stateIdx >= 0 && stateIdx < MAX_STATES);
        States[CurrentStateIdx].Animation.Reset();
        CurrentStateIdx = stateIdx;
        States[CurrentStateIdx].Animation.Play();
        OnStateFinishedInvoked = false;
    }

    public void ConnectStates(int stateIdx1, int stateIdx2)
    {
        Debug.Assert(stateIdx1 >= 0 && stateIdx1 < MAX_STATES && StateSlots[stateIdx1]);
        Debug.Assert(stateIdx2 >= 0 && stateIdx2 < MAX_STATES && StateSlots[stateIdx2]);

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
        Debug.Assert(stateIdx1 >= 0 && stateIdx1 < MAX_STATES && StateSlots[stateIdx1]);
        Debug.Assert(stateIdx2 >= 0 && stateIdx2 < MAX_STATES && StateSlots[stateIdx2]);

        ref CraState state1 = ref States[stateIdx1];
        ref CraState state2 = ref States[stateIdx2];

        state1._Transitions[stateIdx2] = null;
        state2._Transitions[stateIdx1] = null;
    }

    public void TransitTo(int stateIdx)
    {
        if (TransitToIdx > -1)
        {
            Debug.LogWarning("There's already a transit taking place!");
            return;
        }
        Debug.Assert(stateIdx >= 0 && stateIdx < MAX_STATES && StateSlots[stateIdx]);

        ref CraState from = ref States[CurrentStateIdx];
        if (from._Transitions[stateIdx] == null)
        {
            Debug.LogError($"Cannot transit from '{CurrentStateIdx}' to '{stateIdx}', they are not connected!");
            return;
        }

        TransitToIdx = stateIdx;
        TransitTimer = 0f;
        ref CraState to = ref States[TransitToIdx];

        // from now on, WE are controlling the animation
        from.Animation.IsPlaying = false;
        to.Animation.IsPlaying = false;
        to.Animation.Playback = 0f;
    }

    void Awake()
    {
        void Parse(Transform t)
        {
            BasePose.Add(CraSettings.BoneHashFunction(t.name), new PosRot(t.localPosition, t.localRotation));
            for (int i = 0; i < t.childCount; ++i)
            {
                Parse(t.GetChild(i));
            }
        }
        Parse(transform);
    }

    protected void Tick(float deltaTime)
    {
        if (TransitToIdx > -1)
        {
            ref CraState from = ref States[CurrentStateIdx];
            ref CraState to   = ref States[TransitToIdx];

            CraPlayer aFrom = from.Animation;
            CraPlayer aTo = to.Animation;

            aFrom.Playback += deltaTime;
            aTo.Playback += deltaTime;

            int frameIdxFrom = Mathf.FloorToInt(aFrom.Playback * aFrom.Clip.Fps);
            int frameIdxTo = Mathf.FloorToInt(aTo.Playback * aTo.Clip.Fps);

            CraStateTransition transition = from._Transitions[TransitToIdx];

            Vector3 fromPos, toPos;
            Quaternion fromRot, toRot;

            for (int i = 0; i < transition.Bones.Length; ++i)
            {
                int idx = transition.Bones[i];

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

                float lerp = TransitTimer / transition.Time;
                aFrom.AssignedBones[idx].localPosition = Vector3.Lerp(fromPos, toPos, lerp);
                aFrom.AssignedBones[idx].localRotation = Quaternion.Slerp(fromRot, toRot, lerp);
            }

            TransitTimer += deltaTime;
            if (TransitTimer >= transition.Time)
            {
                from.Animation.Reset();
                to.Animation.IsPlaying = true;
                CurrentStateIdx = TransitToIdx;
                TransitToIdx = -1;
                OnTransitFinished?.Invoke();
                OnStateFinishedInvoked = false;
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
