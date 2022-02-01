using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;


public unsafe class CraStateMachineManager
{
    CraBuffer<CraStateMachineData> StateMachines;
    CraBuffer<CraValueUnion> Inputs;
    CraBuffer<CraStateData> States;
    CraBuffer<CraTransitionData> Transitions;

    StateMachineJob Job;


    public CraStateMachineManager()
    {
        StateMachines = new CraBuffer<CraStateMachineData>(CraMain.Instance.Settings.Transitions);
        Inputs = new CraBuffer<CraValueUnion>(CraMain.Instance.Settings.Inputs);
        States = new CraBuffer<CraStateData>(CraMain.Instance.Settings.States);
        Transitions = new CraBuffer<CraTransitionData>(CraMain.Instance.Settings.Transitions);
    }

    public CraHandle StateMachine_New()
    {
        var h = new CraHandle(StateMachines.Alloc());
        var machine = StateMachines.Get(h.Internal);
        machine.Active = true;
        StateMachines.Set(h.Internal, machine);
        return h;
    }

    public void StateMachine_SetActive(CraHandle stateMachine, bool active)
    {
        Debug.Assert(stateMachine.IsValid());

        var machine = StateMachines.Get(stateMachine.Internal);
        machine.Active = active;
        StateMachines.Set(stateMachine.Internal, machine);
    }

    public CraHandle Inputs_New(CraHandle stateMachine)
    {
        Debug.Assert(stateMachine.IsValid());

        var machine = StateMachines.Get(stateMachine.Internal);
        if (machine.InputCount >= CraSettings.MaxInputs)
        {
            Debug.LogError($"Cannot add new input to state machine, maximum of {CraSettings.MaxInputs} exceeded!");
            return CraHandle.Invalid;
        }

        var h = new CraHandle(Inputs.Alloc());
        if (!h.IsValid())
        {
            Debug.LogError($"Allocation of new input failed!");
            return CraHandle.Invalid;
        }

        machine.Inputs[machine.InputCount++] = h.Internal;
        StateMachines.Set(stateMachine.Internal, machine);

        return h;
    }

    public void Inputs_SetValueInt(CraHandle inputHandle, int value)
    {
        Debug.Assert(inputHandle.IsValid());
        var input = Inputs.Get(inputHandle.Internal);
        input.Type = CraValueType.Int;
        input.ValueInt = value;
        Inputs.Set(inputHandle.Internal, input);
    }

    public void Inputs_SetValueFloat(CraHandle inputHandle, float value)
    {
        Debug.Assert(inputHandle.IsValid());
        var input = Inputs.Get(inputHandle.Internal);
        input.Type = CraValueType.Float;
        input.ValueFloat = value;
        Inputs.Set(inputHandle.Internal, input);
    }

    public void Inputs_SetValueBool(CraHandle inputHandle, bool value)
    {
        Debug.Assert(inputHandle.IsValid());
        var input = Inputs.Get(inputHandle.Internal);
        input.Type = CraValueType.Bool;
        input.ValueBool = value;
        Inputs.Set(inputHandle.Internal, input);
    }

    public CraHandle Layer_New(CraHandle stateMachine, CraHandle activeState)
    {
        Debug.Assert(stateMachine.IsValid());
        Debug.Assert(activeState.IsValid());

        var machine = StateMachines.Get(stateMachine.Internal);
        if (machine.LayerCount >= CraSettings.MaxLayers)
        {
            Debug.LogError($"Cannot add new layer to state machine, maximum of {CraSettings.MaxLayers} exceeded!");
            return CraHandle.Invalid;
        }
        machine.ActiveState[machine.LayerCount++] = activeState.Internal;
        StateMachines.Set(stateMachine.Internal, machine);
        return new CraHandle(machine.LayerCount++);
    }

    public void Layer_SetActiveState(CraHandle stateMachine, CraHandle layer, CraHandle activeState)
    {
        Debug.Assert(stateMachine.IsValid());
        Debug.Assert(layer.IsValid());
        Debug.Assert(activeState.IsValid());
        var machine = StateMachines.Get(stateMachine.Internal);
        machine.ActiveState[layer.Internal] = activeState.Internal;
        StateMachines.Set(stateMachine.Internal, machine);
    }

    public CraHandle State_New(CraHandle player)
    {
        var h = new CraHandle(States.Alloc());
        if (!h.IsValid())
        {
            Debug.LogError($"Allocation of new state failed!");
            return CraHandle.Invalid;
        }
        var state = States.Get(h.Internal);
        state.Player = player;
        States.Set(h.Internal, state);
        return h;
    }

    public CraHandle Transition_New(CraHandle stateFrom, CraHandle stateTo, in CraTransitionCondition condition)
    {
        Debug.Assert(stateFrom.IsValid());
        Debug.Assert(stateTo.IsValid());

        var state = States.Get(stateFrom.Internal);
        if (state.TransitionsCount >= CraSettings.MaxTransitions)
        {
            Debug.LogError($"Cannot add new transition to state, maximum of {CraSettings.MaxTransitions} exceeded!");
            return CraHandle.Invalid;
        }

        var h = new CraHandle(Transitions.Alloc());
        if (!h.IsValid())
        {
            Debug.LogError($"Allocation of new transition failed!");
            return CraHandle.Invalid;
        }

        var tran = Transitions.Get(h.Internal);
        tran.Condition = condition;
        tran.TargetState = stateTo;
        Transitions.Set(h.Internal, tran);

        state.Transitions[state.TransitionsCount++] = h.Internal;
        States.Set(stateFrom.Internal, state);

        return h;
    }

    public void Tick()
    {
        Job.StateMachines = StateMachines.GetMemoryBuffer();
        Job.Inputs = Inputs.GetMemoryBuffer();
        Job.States = States.GetMemoryBuffer();
        Job.Transitions = Transitions.GetMemoryBuffer();

        JobHandle scheduled = Job.Schedule(StateMachines.GetNumAllocated(), 4);
        scheduled.Complete();
    }

    public void Clear()
    {
        StateMachines.Clear();
        Inputs.Clear();
        States.Clear();
        Transitions.Clear();
    }

    public void Destroy()
    {
        StateMachines.Destroy();
        Inputs.Destroy();
        States.Destroy();
        Transitions.Destroy();
    }

    struct CraStateMachineData
    {
        public bool Active;
        public int LayerCount;
        public int InputCount;
        public fixed int ActiveState[CraSettings.MaxLayers];
        public fixed int Inputs[CraSettings.MaxInputs];
    }

    struct CraStateData
    {
        public CraHandle Player;
        public int TransitionsCount;
        public fixed int Transitions[CraSettings.MaxTransitions];
    }

    struct CraTransitionData
    {
        public CraHandle TargetState;
        public CraTransitionCondition Condition;
        public float TransitionTime;
    }


    [BurstCompile]
    struct StateMachineJob : IJobParallelFor
    {
        // Read + Write
        public NativeArray<CraStateMachineData> StateMachines;

        // Read + Write
        public NativeArray<CraValueUnion> Inputs;

        [ReadOnly]
        public NativeArray<CraStateData> States;

        [ReadOnly]
        public NativeArray<CraTransitionData> Transitions;


        public void Execute(int index)
        {
            var machine = StateMachines[index];

            // Maybe also parallelize layers
            for (int li = 0; li < machine.LayerCount; ++li)
            {
                var stateIdx = machine.ActiveState[li];
                var state = States[stateIdx];

                for (int ti = 0; ti < state.TransitionsCount; ++ti)
                {
                    var tranIdx = state.Transitions[ti];
                    var tran = Transitions[tranIdx];
                    var con = tran.Condition;
                    bool transit = false;

                    int valueInt = con.ValueAsAbsolute ? Mathf.Abs(con.Value.ValueInt) : con.Value.ValueInt;
                    float valueFloat = con.ValueAsAbsolute ? Mathf.Abs(con.Value.ValueInt) : con.Value.ValueInt;
                    var input = Inputs[con.Input.Handle.Internal];

                    if (con.Condition == CraCondition.Equal && con.Input.IsValid())
                    {
                        transit = 
                            (input.Type == CraValueType.Int   && valueInt             == input.ValueInt) ||
                            (input.Type == CraValueType.Float && valueFloat           == input.ValueFloat) ||
                            (input.Type == CraValueType.Bool  && con.Value.ValueBool  == input.ValueBool);
                    }
                    else if (con.Condition == CraCondition.Greater && con.Input.IsValid())
                    {
                        transit =
                            (input.Type == CraValueType.Int   && valueInt   > input.ValueInt) ||
                            (input.Type == CraValueType.Float && valueFloat > input.ValueFloat);
                    }
                    else if (con.Condition == CraCondition.GreaterOrEqual && con.Input.IsValid())
                    {
                        transit =
                            (input.Type == CraValueType.Int   && valueInt   >= input.ValueInt) ||
                            (input.Type == CraValueType.Float && valueFloat >= input.ValueFloat);
                    }
                    else if (con.Condition == CraCondition.Less && con.Input.IsValid())
                    {
                        transit =
                            (input.Type == CraValueType.Int   && valueInt   < input.ValueInt) ||
                            (input.Type == CraValueType.Float && valueFloat < input.ValueFloat);
                    }
                    else if (con.Condition == CraCondition.LessOrEqual && con.Input.IsValid())
                    {
                        transit =
                            (input.Type == CraValueType.Int   && valueInt   <= input.ValueInt) ||
                            (input.Type == CraValueType.Float && valueFloat <= input.ValueFloat);
                    }
                    else if (con.Condition == CraCondition.Trigger && con.Input.IsValid())
                    {
                        transit = input.Type == CraValueType.Bool && con.Value.ValueBool;
                        input.ValueBool = false;
                        Inputs[con.Input.Handle.Internal] = input;
                    }
                    else if (con.Condition == CraCondition.IsFinished && state.Player.IsValid() && CraMain.Instance.Players.Player_IsFinished(state.Player))
                    {
                        transit = true;
                    }

                    if (transit)
                    {
                        CraMain.Instance.Players.Player_Reset(state.Player);
                        machine.ActiveState[li] = tran.TargetState.Internal;
                        CraMain.Instance.Players.Player_Play(state.Player, tran.TransitionTime);
                    }
                }
            }

            StateMachines[index] = machine;
        }
    }
}