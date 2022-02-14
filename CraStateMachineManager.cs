using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

public unsafe partial class CraMain
{
    public unsafe class CraStateMachineManager
    {
#if UNITY_EDITOR
        Dictionary<CraHandle, List<CraHandle>[]> StateMachineLayerStates;
        Dictionary<CraHandle, string> StateNames;
        Dictionary<CraHandle, string> InputNames;
#endif

        CraBuffer<CraStateMachineData> StateMachines;
        CraBuffer<CraValueUnion> Inputs;
        CraBuffer<CraStateData> States;
        CraBuffer<CraTransitionData> Transitions;

        internal CraStateMachineManager()
        {
#if UNITY_EDITOR
            StateMachineLayerStates = new Dictionary<CraHandle, List<CraHandle>[]>();
            StateNames = new Dictionary<CraHandle, string>();
            InputNames = new Dictionary<CraHandle, string>();
#endif
            StateMachines = new CraBuffer<CraStateMachineData>(Instance.Settings.Transitions);
            Inputs = new CraBuffer<CraValueUnion>(Instance.Settings.Inputs);
            States = new CraBuffer<CraStateData>(Instance.Settings.States);
            Transitions = new CraBuffer<CraTransitionData>(Instance.Settings.Transitions);
        }

        public CraHandle StateMachine_New()
        {
            var h = new CraHandle(StateMachines.Alloc());
            var machine = StateMachines.Get(h.Index);
            machine.Active = true;
            StateMachines.Set(h.Index, machine);
#if UNITY_EDITOR
            StateMachineLayerStates.Add(h, new List<CraHandle>[CraSettings.MaxLayers]);
#endif
            return h;
        }

        public void StateMachine_SetActive(CraHandle stateMachine, bool active)
        {
            Debug.Assert(stateMachine.IsValid());

            var machine = StateMachines.Get(stateMachine.Index);
            if (machine.Active == active)
            {
                return;
            }

            machine.Active = active;
            if (active)
            {
                for (int i = 0; i < machine.LayerCount; ++i)
                {
                    machine.Transitioning[i] = true;
                    if (machine.ActiveState[i] >= 0)
                    {
                        CraStateData data = States.Get(machine.ActiveState[i]);
                        CraPlaybackManager.Player_Play(Instance.PlayerData.GetMemoryBuffer(), data.Player);
                    }
                }
            }
            else
            {
                for (int i = 0; i < machine.LayerCount; ++i)
                {
                    machine.Transitioning[i] = false;
                    if (machine.ActiveState[i] >= 0)
                    {
                        CraStateData data = States.Get(machine.ActiveState[i]);
                        CraPlaybackManager.Player_Reset(Instance.PlayerData.GetMemoryBuffer(), data.Player);
                    }
                }
            }

            StateMachines.Set(stateMachine.Index, machine);
        }

        public CraHandle[] StateMachine_GetLayers(CraHandle stateMachine)
        {
            Debug.Assert(stateMachine.IsValid());
            var machine = StateMachines.Get(stateMachine.Index);
            CraHandle[] res = new CraHandle[machine.LayerCount];
            for (int i = 0; i < machine.LayerCount; ++i)
            {
                res[i] = new CraHandle(i);
            }
            return res;
        }

        public CraHandle[] StateMachine_GetInputs(CraHandle stateMachine)
        {
            Debug.Assert(stateMachine.IsValid());
            var machine = StateMachines.Get(stateMachine.Index);
            CraHandle[] res = new CraHandle[machine.InputCount];
            for (int i = 0; i < machine.InputCount; ++i)
            {
                res[i] = new CraHandle(machine.Inputs[i]);
            }
            return res;
        }

        public CraHandle Inputs_New(CraHandle stateMachine, CraValueType type)
        {
            Debug.Assert(stateMachine.IsValid());

            var machine = StateMachines.Get(stateMachine.Index);
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

            var input = Inputs.Get(h.Index);
            input.Type = type;
            Inputs.Set(h.Index, input);

            machine.Inputs[machine.InputCount++] = h.Index;
            StateMachines.Set(stateMachine.Index, machine);

            return h;
        }

        public CraValueUnion Inputs_GetValue(CraHandle inputHandle)
        {
            Debug.Assert(inputHandle.IsValid());
            return Inputs.Get(inputHandle.Index);
        }

        public void Inputs_SetValueInt(CraHandle inputHandle, int value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Int);
            input.ValueInt = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public void Inputs_SetValueFloat(CraHandle inputHandle, float value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Float);
            input.ValueFloat = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public void Inputs_SetValueBool(CraHandle inputHandle, bool value)
        {
            // Since CraStateMachineJob can write to Inputs to reset
            // a trigger bool to false, we should lock here.
            lock (Instance.Lock)
            {
                Debug.Assert(inputHandle.IsValid());
                var input = Inputs.Get(inputHandle.Index);
                Debug.Assert(input.Type == CraValueType.Bool);
                input.ValueBool = value;
                Inputs.Set(inputHandle.Index, input);
            }
        }

        public CraHandle Layer_New(CraHandle stateMachine)
        {
            Debug.Assert(stateMachine.IsValid());

            var machine = StateMachines.Get(stateMachine.Index);
            if (machine.LayerCount >= CraSettings.MaxLayers)
            {
                Debug.LogError($"Cannot add new layer to state machine, maximum of {CraSettings.MaxLayers} exceeded!");
                return CraHandle.Invalid;
            }

            int layerIdx = machine.LayerCount;
#if UNITY_EDITOR
            StateMachineLayerStates[stateMachine][layerIdx] = new List<CraHandle>();
#endif
            machine.ActiveState[layerIdx] = -1;
            machine.LayerCount++;

            StateMachines.Set(stateMachine.Index, machine);
            return new CraHandle(layerIdx);
        }

        public CraHandle Layer_GetActiveState(CraHandle stateMachine, CraHandle layer)
        {
            Debug.Assert(stateMachine.IsValid());
            Debug.Assert(layer.IsValid());
            var machine = StateMachines.Get(stateMachine.Index);
            return new CraHandle(machine.ActiveState[layer.Index]);
        }
#if UNITY_EDITOR
        public CraHandle[] Layer_GetAllStates(CraHandle stateMachine, CraHandle layer)
        {
            return StateMachineLayerStates[stateMachine][layer.Index].ToArray();
        }
#endif
        public void Layer_SetActiveState(CraHandle stateMachine, CraHandle layer, CraHandle activeState)
        {
            Debug.Assert(stateMachine.IsValid());
            Debug.Assert(layer.IsValid());
            Debug.Assert(activeState.IsValid());
#if UNITY_EDITOR
            if (!StateMachineLayerStates[stateMachine][layer.Index].Contains(activeState))
            {
                Debug.LogError($"Tried to set layer {layer.Index} of state machine {stateMachine.Index} to active state {activeState.Index}, which does not reside in said layer!");
                return;
            }
#endif
            var machine = StateMachines.Get(stateMachine.Index);
            machine.ActiveState[layer.Index] = activeState.Index;
            machine.Transitioning[layer.Index] = true;
            StateMachines.Set(stateMachine.Index, machine);

            var state = States.Get(activeState.Index);
            if (state.Player.IsValid())
            {
                Instance.Players.Player_Play(state.Player);
            }
        }

        public CraHandle State_New(CraHandle player, CraHandle stateMachine, CraHandle layerHandle)
        {
            var h = new CraHandle(States.Alloc());
            if (!h.IsValid())
            {
                Debug.LogError($"Allocation of new state failed!");
                return CraHandle.Invalid;
            }
            var state = States.Get(h.Index);
            state.Player = player;
            States.Set(h.Index, state);
#if UNITY_EDITOR
            StateMachineLayerStates[stateMachine][layerHandle.Index].Add(h);
#endif
            return h;
        }

        public CraHandle[] State_GetTransitions(CraHandle stateHandle)
        {
            Debug.Assert(stateHandle.IsValid());

            var state = States.Get(stateHandle.Index);
            CraHandle[] transitions = new CraHandle[state.TransitionsCount];
            for (int i = 0; i < state.TransitionsCount; ++i)
            {
                transitions[i] = new CraHandle(state.Transitions[i]);
            }
            return transitions;
        }

        public CraHandle State_GetPlayer(CraHandle stateHandle)
        {
            Debug.Assert(stateHandle.IsValid());

            var state = States.Get(stateHandle.Index);
            return state.Player;
        }

        public CraHandle Transition_New(CraHandle stateHandle, in CraTransitionData transition)
        {
            Debug.Assert(transition.Target.IsValid());

            var state = States.Get(stateHandle.Index);
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
            Transitions.Set(h.Index, transition);

            state.Transitions[state.TransitionsCount++] = h.Index;
            States.Set(stateHandle.Index, state);

            return h;
        }

#if UNITY_EDITOR
        public string GetStateName(CraHandle state)
        {
            if (StateNames.TryGetValue(state, out string name))
            {
                return name;
            }
            return null;
        }
        public void SetStateName(CraHandle state, string name)
        {
            if (StateNames.ContainsKey(state))
            {
                StateNames[state] = name;
            }
            else
            {
                StateNames.Add(state, name);
            }
        }
        public string GetInputName(CraHandle input)
        {
            if (InputNames.TryGetValue(input, out string name))
            {
                return name;
            }
            return null;
        }
        public void SetInputName(CraHandle input, string name)
        {
            if (InputNames.ContainsKey(input))
            {
                InputNames[input] = name;
            }
            else
            {
                InputNames.Add(input, name);
            }
        }
        public unsafe void UpdateStatistics()
        {
            Instance.Statistics.StateMachines.MaxElements = StateMachines.GetCapacity();
            Instance.Statistics.StateMachines.MaxBytes = (ulong)StateMachines.GetCapacity() * (ulong)sizeof(CraStateMachineData);
            Instance.Statistics.StateMachines.CurrentElements = StateMachines.GetNumAllocated();
            Instance.Statistics.StateMachines.CurrentBytes = (ulong)StateMachines.GetNumAllocated() * (ulong)sizeof(CraStateMachineData);

            Instance.Statistics.Inputs.MaxElements = Inputs.GetCapacity();
            Instance.Statistics.Inputs.MaxBytes = (ulong)Inputs.GetCapacity() * (ulong)sizeof(CraValueUnion);
            Instance.Statistics.Inputs.CurrentElements = Inputs.GetNumAllocated();
            Instance.Statistics.Inputs.CurrentBytes = (ulong)Inputs.GetNumAllocated() * (ulong)sizeof(CraValueUnion);

            Instance.Statistics.States.MaxElements = States.GetCapacity();
            Instance.Statistics.States.MaxBytes = (ulong)States.GetCapacity() * (ulong)sizeof(CraStateData);
            Instance.Statistics.States.CurrentElements = States.GetNumAllocated();
            Instance.Statistics.States.CurrentBytes = (ulong)States.GetNumAllocated() * (ulong)sizeof(CraStateData);

            Instance.Statistics.Transitions.MaxElements = Transitions.GetCapacity();
            Instance.Statistics.Transitions.MaxBytes = (ulong)Transitions.GetCapacity() * (ulong)sizeof(CraStateData);
            Instance.Statistics.Transitions.CurrentElements = Transitions.GetNumAllocated();
            Instance.Statistics.Transitions.CurrentBytes = (ulong)Transitions.GetNumAllocated() * (ulong)sizeof(CraTransitionData);
        }
#endif

        public CraTransitionData Transition_GetData(CraHandle transitionHandle)
        {
            Debug.Assert(transitionHandle.IsValid());
            return Transitions.Get(transitionHandle.Index);
        }

        public void Schedule(JobHandle playerJob)
        {
            Instance.MachineJob.Players = Instance.PlayerData.GetMemoryBuffer();
            Instance.MachineJob.StateMachines = StateMachines.GetMemoryBuffer();
            Instance.MachineJob.Inputs = Inputs.GetMemoryBuffer();
            Instance.MachineJob.States = States.GetMemoryBuffer();
            Instance.MachineJob.Transitions = Transitions.GetMemoryBuffer();

            JobHandle scheduled = Instance.MachineJob.Schedule(StateMachines.GetNumAllocated(), 4, playerJob);
            scheduled.Complete();



            // Hack: Capture Bones for transitioning states
            for (int mi = 0; mi < StateMachines.GetNumAllocated(); ++mi)
            {
                bool needSave = false;
                CraStateMachineData data = StateMachines.Get(mi);
                for (int li = 0; li < data.LayerCount; ++li)
                {
                    if (data.Transitioning[li])
                    {
                        CraState state = new CraState(new CraHandle(data.ActiveState[li]));
                        CraPlayer player = state.GetPlayer();
                        if (player.IsValid())
                        {
                            player.CaptureBones();
                        }
                        data.Transitioning[li] = false;
                        needSave = true;
                    }
                }

                if (needSave)
                {
                    StateMachines.Set(mi, data);
                }
            }
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
    }

    struct CraStateMachineData
    {
        public bool Active;
        public int LayerCount;
        public int InputCount;
        public fixed int ActiveState[CraSettings.MaxLayers];
        public fixed bool Transitioning[CraSettings.MaxLayers];
        public fixed int Inputs[CraSettings.MaxInputs];
    }

    struct CraStateData
    {
        public CraHandle Player;
        public int TransitionsCount;
        public fixed int Transitions[CraSettings.MaxTransitions];
    }

    [BurstCompile]
    struct CraStateMachineJob : IJobParallelFor
    {
        // Read + Write
        [NativeDisableParallelForRestriction]
        public NativeArray<CraPlayerData> Players;

        // Read + Write
        public NativeArray<CraStateMachineData> StateMachines;

        // Read + Write
        [NativeDisableParallelForRestriction]
        public NativeArray<CraValueUnion> Inputs;

        [ReadOnly]
        public NativeArray<CraStateData> States;

        [ReadOnly]
        public NativeArray<CraTransitionData> Transitions;


        public void Execute(int index)
        {
            var machine = StateMachines[index];
            if (!machine.Active) return;

            // Maybe also parallelize layers
            for (int li = 0; li < machine.LayerCount; ++li)
            {
                var stateIdx = machine.ActiveState[li];
                if (stateIdx < 0 || machine.Transitioning[li])
                {
                    continue;
                }

                var state = States[stateIdx];

                for (int ti = 0; ti < state.TransitionsCount; ++ti)
                {
                    var tranIdx = state.Transitions[ti];
                    var tran = Transitions[tranIdx];
                    bool transit =
                        CheckCondition(tran.Or0, state) ||
                        CheckCondition(tran.Or1, state) ||
                        CheckCondition(tran.Or2, state) ||
                        CheckCondition(tran.Or3, state);

                    if (transit)
                    {
                        if (state.Player.IsValid())
                        {
                            CraPlaybackManager.Player_Reset(Players, state.Player);
                        }
                        machine.ActiveState[li] = tran.Target.Handle.Index;
                        machine.Transitioning[li] = true;
                        var newState = States[machine.ActiveState[li]];
                        if (newState.Player.IsValid())
                        {
                            CraPlaybackManager.Player_Play(Players, newState.Player, tran.TransitionTime);
                        }

                        // No need to check for further transitions
                        break;
                    }
                }
            }

            StateMachines[index] = machine;
        }

        bool HasAtLeastOneCondition(in CraConditionOr or)
        {
            return
                or.And0.Type != CraConditionType.None ||
                or.And1.Type != CraConditionType.None ||
                or.And2.Type != CraConditionType.None ||
                or.And3.Type != CraConditionType.None;
        }

        bool CheckCondition(in CraConditionOr or, in CraStateData state)
        {
            return
                HasAtLeastOneCondition(or) &&
                CheckCondition(or.And0, state) &&
                CheckCondition(or.And1, state) &&
                CheckCondition(or.And2, state) &&
                CheckCondition(or.And3, state);
        }

        bool CheckCondition(in CraCondition con, in CraStateData state)
        {
            if (con.Type == CraConditionType.None)
            {
                return true;
            }

            var input = Inputs[con.Input.Handle.Index];
            int valueInt = con.CompareToAbsolute ? Mathf.Abs(input.ValueInt) : input.ValueInt;
            float valueFloat = con.CompareToAbsolute ? Mathf.Abs(input.ValueFloat) : input.ValueFloat;

            bool conditionMet = false;
            if (con.Type == CraConditionType.Equal && con.Input.IsValid())
            {
                conditionMet =
                    (input.Type == CraValueType.Int && valueInt == con.Value.ValueInt) ||
                    (input.Type == CraValueType.Float && valueFloat == con.Value.ValueFloat) ||
                    (input.Type == CraValueType.Bool && input.ValueBool == con.Value.ValueBool);
            }
            else if (con.Type == CraConditionType.Greater && con.Input.IsValid())
            {
                conditionMet =
                    (input.Type == CraValueType.Int && valueInt > con.Value.ValueInt) ||
                    (input.Type == CraValueType.Float && valueFloat > con.Value.ValueFloat);
            }
            else if (con.Type == CraConditionType.GreaterOrEqual && con.Input.IsValid())
            {
                conditionMet =
                    (input.Type == CraValueType.Int && valueInt >= con.Value.ValueInt) ||
                    (input.Type == CraValueType.Float && valueFloat >= con.Value.ValueFloat);
            }
            else if (con.Type == CraConditionType.Less && con.Input.IsValid())
            {
                conditionMet =
                    (input.Type == CraValueType.Int && valueInt < con.Value.ValueInt) ||
                    (input.Type == CraValueType.Float && valueFloat < con.Value.ValueFloat);
            }
            else if (con.Type == CraConditionType.LessOrEqual && con.Input.IsValid())
            {
                conditionMet =
                    (input.Type == CraValueType.Int && valueInt <= con.Value.ValueInt) ||
                    (input.Type == CraValueType.Float && valueFloat <= con.Value.ValueFloat);
            }
            else if (con.Type == CraConditionType.Trigger && con.Input.IsValid())
            {
                conditionMet = input.Type == CraValueType.Bool && input.ValueBool;
                input.ValueBool = false;
                Inputs[con.Input.Handle.Index] = input;
            }
            else if (con.Type == CraConditionType.IsFinished && state.Player.IsValid() && CraPlaybackManager.Player_IsFinished(Players, state.Player))
            {
                conditionMet = true;
            }

            return conditionMet;
        }
    }
}