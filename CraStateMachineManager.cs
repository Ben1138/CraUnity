using System;
using System.Runtime.InteropServices;
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
        Dictionary<CraHandle, CraHandle> StateToMachine;
        Dictionary<CraHandle, CraHandle> StateToLayer;
        Dictionary<CraHandle, string> StateNames;
        Dictionary<CraHandle, string> InputNames;
        Dictionary<CraHandle, string> OutputNames;
#endif

        CraBuffer<CraStateMachineData> StateMachines;
        CraBuffer<CraValueUnion> Inputs;
        CraBuffer<CraValueUnion> Outputs;
        CraBuffer<CraStateData> States;
        CraBuffer<CraTransitionData> Transitions;

        internal CraStateMachineManager()
        {
#if UNITY_EDITOR
            StateMachineLayerStates = new Dictionary<CraHandle, List<CraHandle>[]>();
            StateToMachine = new Dictionary<CraHandle, CraHandle>();
            StateToLayer = new Dictionary<CraHandle, CraHandle>();
            StateNames = new Dictionary<CraHandle, string>();
            InputNames = new Dictionary<CraHandle, string>();
            OutputNames = new Dictionary<CraHandle, string>();
#endif
            StateMachines = new CraBuffer<CraStateMachineData>(Instance.Settings.Transitions);
            Inputs = new CraBuffer<CraValueUnion>(Instance.Settings.Inputs);
            Outputs = new CraBuffer<CraValueUnion>(Instance.Settings.Outputs);
            States = new CraBuffer<CraStateData>(Instance.Settings.States);
            Transitions = new CraBuffer<CraTransitionData>(Instance.Settings.Transitions);
        }

        public CraHandle StateMachine_New()
        {
            var h = new CraHandle(StateMachines.Alloc());
            if (!h.IsValid())
            {
                Debug.LogError($"Allocation of new StateMachine failed!");
                return CraHandle.Invalid;
            }

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

        public CraHandle[] StateMachine_GetOutputs(CraHandle stateMachine)
        {
            Debug.Assert(stateMachine.IsValid());
            var machine = StateMachines.Get(stateMachine.Index);
            CraHandle[] res = new CraHandle[machine.OutputCount];
            for (int i = 0; i < machine.OutputCount; ++i)
            {
                res[i] = new CraHandle(machine.Outputs[i]);
            }
            return res;
        }

        public CraHandle Input_New(CraHandle stateMachine, CraValueType type)
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

        public CraValueUnion Input_GetValue(CraHandle inputHandle)
        {
            Debug.Assert(inputHandle.IsValid());
            return Inputs.Get(inputHandle.Index);
        }

        public void Input_SetValueInt(CraHandle inputHandle, int value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Int);
            input.ValueInt = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public void Input_SetValueFloat(CraHandle inputHandle, float value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Float);
            input.ValueFloat = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public void Input_SetValueBool(CraHandle inputHandle, bool value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Bool);
            input.ValueBool = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public void Input_SetValueTrigger(CraHandle inputHandle, bool value)
        {
            Debug.Assert(inputHandle.IsValid());
            var input = Inputs.Get(inputHandle.Index);
            Debug.Assert(input.Type == CraValueType.Trigger);
            input.ValueBool = value;
            Inputs.Set(inputHandle.Index, input);
        }

        public CraHandle Output_New(CraHandle stateMachine, CraValueType type)
        {
            Debug.Assert(stateMachine.IsValid());

            var machine = StateMachines.Get(stateMachine.Index);
            if (machine.OutputCount >= CraSettings.MaxOutputs)
            {
                Debug.LogError($"Cannot add new output to state machine, maximum of {CraSettings.MaxOutputs} exceeded!");
                return CraHandle.Invalid;
            }

            var h = new CraHandle(Outputs.Alloc());
            if (!h.IsValid())
            {
                Debug.LogError($"Allocation of new output failed!");
                return CraHandle.Invalid;
            }

            var output = Outputs.Get(h.Index);
            output.Type = type;
            Outputs.Set(h.Index, output);

            machine.Outputs[machine.OutputCount++] = h.Index;
            StateMachines.Set(stateMachine.Index, machine);

            return h;
        }

        public CraValueUnion Output_GetValue(CraHandle outputHandle)
        {
            Debug.Assert(outputHandle.IsValid());
            return Outputs.Get(outputHandle.Index);
        }

        public void Output_SetValue(CraHandle outputHandle, CraValueUnion value)
        {
            Debug.Assert(outputHandle.IsValid());
            Outputs.Set(outputHandle.Index, value);
        }

        public int Output_GetValueInt(CraHandle outputHandle)
        {
            Debug.Assert(outputHandle.IsValid());
            var output = Outputs.Get(outputHandle.Index);
            Debug.Assert(output.Type == CraValueType.Int);
            return output.ValueInt;
        }

        public float Output_GetValueFloat(CraHandle outputHandle)
        {
            Debug.Assert(outputHandle.IsValid());
            var output = Outputs.Get(outputHandle.Index);
            Debug.Assert(output.Type == CraValueType.Float);
            return output.ValueFloat;
        }

        public bool Output_GetValueBool(CraHandle outputHandle)
        {
            Debug.Assert(outputHandle.IsValid());
            var output = Outputs.Get(outputHandle.Index);
            Debug.Assert(output.Type == CraValueType.Bool);
            return output.ValueBool;
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
        public void Layer_SetActiveState(CraHandle stateMachine, CraHandle layer, CraHandle newActiveState, float transitionTime)
        {
            Debug.Assert(stateMachine.IsValid());
            Debug.Assert(layer.IsValid());
            Debug.Assert(newActiveState.IsValid());
#if UNITY_EDITOR
            if (!StateMachineLayerStates[stateMachine][layer.Index].Contains(newActiveState))
            {
                Debug.LogError($"Tried to set layer {layer.Index} of state machine {stateMachine.Index} to active state {newActiveState.Index}, which does not reside in said layer!");
                return;
            }
#endif
            var machine = StateMachines.Get(stateMachine.Index);
            CraHandle oldActiveState = new CraHandle(machine.ActiveState[layer.Index]);
            if (oldActiveState.IsValid())
            {
                CraStateData data = States.Get(oldActiveState.Index);
                if (data.Player.IsValid())
                {
                    Instance.Players.Player_Reset(data.Player);
                }
            }
            machine.Transitioning[layer.Index] = true;
            machine.ActiveState[layer.Index] = newActiveState.Index;
            StateMachines.Set(stateMachine.Index, machine);

            var state = States.Get(newActiveState.Index);
            if (state.Player.IsValid())
            {
                Instance.Players.Player_Play(state.Player, transitionTime);
            }

            CraWrite* write = &state.WriteOutputEnter0;
            for (int wi = 0; wi < state.WriteEnterCount; ++wi)
            {
                if (write[wi].Output.IsValid())
                {
                    Outputs.Set(write[wi].Output.Index, write[wi].Value);
                }
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
            state.PlaybackSpeedInput = CraHandle.Invalid;
            States.Set(h.Index, state);
#if UNITY_EDITOR
            StateMachineLayerStates[stateMachine][layerHandle.Index].Add(h);
            StateToMachine.Add(h, stateMachine);
            StateToLayer.Add(h, layerHandle);
#endif
            return h;
        }

        public unsafe void State_WriteOutputOnEnter(CraHandle stateHandle, CraHandle outputHandle, CraValueUnion value)
        {
            if (!stateHandle.IsValid())
            {
                Debug.LogError("Given state handle is invalid!");
                return;
            }
            if (!outputHandle.IsValid())
            {
                Debug.LogError("Given output handle is invalid!");
                return;
            }

            var state = States.Get(stateHandle.Index);
            Debug.Assert(state.WriteEnterCount >= 0);
            if (state.WriteEnterCount >= 16)
            {
                Debug.LogError("Maximum of 16 write outputs reached!");
                return;
            }

            CraWrite* write = &state.WriteOutputEnter0;
            for (int i = 0; i < state.WriteEnterCount; ++i)
            {
                if (write[i].Output == outputHandle)
                {
                    Debug.LogWarning($"You're writing to output {outputHandle.Index} more than once in state {stateHandle.Index}!");
                }
            }
            write[state.WriteEnterCount].Output = outputHandle;
            write[state.WriteEnterCount].Value = value;
            state.WriteEnterCount++;
            States.Set(stateHandle.Index, state);
        }

        public unsafe void State_WriteOutputOnLeave(CraHandle stateHandle, CraHandle outputHandle, CraValueUnion value)
        {
            if (!stateHandle.IsValid())
            {
                Debug.LogError("Given state handle is invalid!");
                return;
            }
            if (!outputHandle.IsValid())
            {
                Debug.LogError("Given output handle is invalid!");
                return;
            }

            var state = States.Get(stateHandle.Index);
            Debug.Assert(state.WriteLeaveCount >= 0);
            if (state.WriteLeaveCount >= 16)
            {
                Debug.LogError("Maximum of 16 write outputs reached!");
                return;
            }

            CraWrite* write = &state.WriteOutputLeave0;
            for (int i = 0; i < state.WriteLeaveCount; ++i)
            {
                if (write[i].Output == outputHandle)
                {
                    Debug.LogWarning($"You're writing to output {outputHandle.Index} more than once in state {stateHandle.Index}!");
                }
            }
            write[state.WriteLeaveCount].Output = outputHandle;
            write[state.WriteLeaveCount].Value = value;
            state.WriteLeaveCount++;
            States.Set(stateHandle.Index, state);
        }

        public void State_SetPlaybackSpeedInput(CraHandle stateHandle, CraHandle inputHandle)
        {
            if (!stateHandle.IsValid())
            {
                Debug.LogError("Given state handle is invalid!");
                return;
            }
            if (!inputHandle.IsValid())
            {
                Debug.LogError("Given input handle is invalid!");
                return;
            }
            var state = States.Get(stateHandle.Index);
            state.PlaybackSpeedInput = inputHandle;
            States.Set(stateHandle.Index, in state);
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

        public void State_SetSyncState(CraHandle stateHandle, CraHandle syncStateHandle)
        {
            Debug.Assert(stateHandle.IsValid());
            Debug.Assert(syncStateHandle.IsValid());

            var state = States.Get(stateHandle.Index);
            state.SyncToState = syncStateHandle;
            States.Set(stateHandle.Index, state);
        }

        public CraHandle Transition_New(CraHandle stateHandle, in CraTransitionData transition)
        {
            Debug.Assert(transition.Target.IsValid());

#if UNITY_EDITOR
            {
                CraHandle stateMachine = StateToMachine[stateHandle];
                if (stateMachine != StateToMachine[transition.Target.Handle])
                {
                    Debug.LogError($"Tried to transition from state machine {stateMachine.Index} to other state machine {transition.Target.Handle.Index}!");
                    return CraHandle.Invalid;
                }

                CraHandle layer = StateToLayer[stateHandle];
                if (!StateMachineLayerStates[stateMachine][layer.Index].Contains(transition.Target.Handle))
                {
                    Debug.LogError($"Tried to transition from layer {layer.Index} to layer {StateToLayer[transition.Target.Handle].Index} of state machine {stateMachine.Index}!");
                    return CraHandle.Invalid;
                }
            }
#endif

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
        public string GetOutputName(CraHandle output)
        {
            if (OutputNames.TryGetValue(output, out string name))
            {
                return name;
            }
            return null;
        }
        public void SetOutputName(CraHandle output, string name)
        {
            if (OutputNames.ContainsKey(output))
            {
                OutputNames[output] = name;
            }
            else
            {
                OutputNames.Add(output, name);
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
            Instance.MachineJob.Outputs = Outputs.GetMemoryBuffer();
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

            // Reset all Input Triggers
            for (int i = 0; i < Inputs.GetNumAllocated(); ++i)
            {
                var data = Inputs.Get(i);
                if (data.Type == CraValueType.Trigger)
                {
                    data.ValueBool = false;
                }
                Inputs.Set(i, data);
            }
        }

        public void Clear()
        {
            StateMachineLayerStates.Clear();
            StateToMachine.Clear();
            StateToLayer.Clear();
            StateNames.Clear();
            InputNames.Clear();
            OutputNames.Clear();

            StateMachines.Clear();
            Inputs.Clear();
            Outputs.Clear();
            States.Clear();
            Transitions.Clear();
        }

        public void Destroy()
        {
            StateMachineLayerStates.Clear();
            StateToMachine.Clear();
            StateToLayer.Clear();
            StateNames.Clear();
            InputNames.Clear();
            OutputNames.Clear();

            StateMachines.Destroy();
            Inputs.Destroy();
            Outputs.Destroy();
            States.Destroy();
            Transitions.Destroy();
        }
    }

    struct CraStateMachineData
    {
        public bool Active;
        public int LayerCount;
        public fixed int ActiveState[CraSettings.MaxLayers];
        public fixed bool Transitioning[CraSettings.MaxLayers];

        public fixed int Inputs[CraSettings.MaxInputs];
        public int InputCount;

        public fixed int Outputs[CraSettings.MaxOutputs];
        public int OutputCount;
    }

    struct CraWrite
    {
        public CraHandle Output;
        public CraValueUnion Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CraStateData
    {
        public CraHandle Player;
        public CraHandle PlaybackSpeedInput; // optional
        public CraHandle SyncToState; // when entering this state, sync playback time to that state
        public int TransitionsCount;
        public fixed int Transitions[CraSettings.MaxTransitions];

        public int WriteEnterCount;
        public CraWrite WriteOutputEnter0;
        public CraWrite WriteOutputEnter1;
        public CraWrite WriteOutputEnter2;
        public CraWrite WriteOutputEnter3;
        public CraWrite WriteOutputEnter4;
        public CraWrite WriteOutputEnter5;
        public CraWrite WriteOutputEnter6;
        public CraWrite WriteOutputEnter7;
        public CraWrite WriteOutputEnter8;
        public CraWrite WriteOutputEnter9;
        public CraWrite WriteOutputEnter10;
        public CraWrite WriteOutputEnter11;
        public CraWrite WriteOutputEnter12;
        public CraWrite WriteOutputEnter13;
        public CraWrite WriteOutputEnter14;
        public CraWrite WriteOutputEnter15;

        public int WriteLeaveCount;
        public CraWrite WriteOutputLeave0;
        public CraWrite WriteOutputLeave1;
        public CraWrite WriteOutputLeave2;
        public CraWrite WriteOutputLeave3;
        public CraWrite WriteOutputLeave4;
        public CraWrite WriteOutputLeave5;
        public CraWrite WriteOutputLeave6;
        public CraWrite WriteOutputLeave7;
        public CraWrite WriteOutputLeave8;
        public CraWrite WriteOutputLeave9;
        public CraWrite WriteOutputLeave10;
        public CraWrite WriteOutputLeave11;
        public CraWrite WriteOutputLeave12;
        public CraWrite WriteOutputLeave13;
        public CraWrite WriteOutputLeave14;
        public CraWrite WriteOutputLeave15;
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

        // Read + Write
        [NativeDisableParallelForRestriction]
        public NativeArray<CraValueUnion> Outputs;

        [ReadOnly]
        public NativeArray<CraStateData> States;

        [ReadOnly]
        public NativeArray<CraTransitionData> Transitions;


        public void Execute(int index)
        {
            var machine = StateMachines[index];
            if (!machine.Active) return;

            //Debug.Log($"Executing state machine {index}!");

            // Maybe also parallelize layers
            for (int li = 0; li < machine.LayerCount; ++li)
            {
                var stateIdx = machine.ActiveState[li];
                if (stateIdx < 0 || machine.Transitioning[li])
                {
                    continue;
                }

                var state = States[stateIdx];
                if (!state.Player.IsValid())
                {
                    continue;
                }

                if (state.PlaybackSpeedInput.IsValid())
                {
                    var input = Inputs[state.PlaybackSpeedInput.Index];
                    CraPlaybackManager.Player_SetPlaybackSpeed(Players, state.Player, input.ValueFloat);
                }

                for (int ti = 0; ti < state.TransitionsCount; ++ti)
                {
                    var tranIdx = state.Transitions[ti];
                    var tran = Transitions[tranIdx];
                    bool transit =
                        CheckCondition(tran.Or0, state) ||
                        CheckCondition(tran.Or1, state) ||
                        CheckCondition(tran.Or2, state) ||
                        CheckCondition(tran.Or3, state) ||
                        CheckCondition(tran.Or4, state) ||
                        CheckCondition(tran.Or5, state) ||
                        CheckCondition(tran.Or6, state) ||
                        CheckCondition(tran.Or7, state) ||
                        CheckCondition(tran.Or8, state) ||
                        CheckCondition(tran.Or9, state);

                    if (transit)
                    {
                        machine.ActiveState[li] = tran.Target.Handle.Index;
                        machine.Transitioning[li] = true;

                        CraWrite* write = &state.WriteOutputLeave0;
                        for (int wi = 0; wi < state.WriteLeaveCount; ++wi)
                        {
                            if (write[wi].Output.IsValid())
                            {
                                Outputs[write[wi].Output.Index] = write[wi].Value;
                            }
                        }

                        CraStateData newState = States[machine.ActiveState[li]];
                        if (newState.Player.IsValid())
                        {
                            CraPlaybackManager.Player_Reset(Players, newState.Player);
                            if (newState.SyncToState.IsValid())
                            {
                                var syncState = States[newState.SyncToState.Index];
                                CraPlayerData pd = Players[newState.Player.Index];
                                pd.Playback = Players[syncState.Player.Index].Playback;
                                Players[newState.Player.Index] = pd;
                            }
                            CraPlaybackManager.Player_Play(Players, newState.Player, tran.TransitionTime);
                        }

                        write = &newState.WriteOutputEnter0;
                        for (int wi = 0; wi < newState.WriteEnterCount; ++wi)
                        {
                            if (write[wi].Output.IsValid())
                            {
                                Outputs[write[wi].Output.Index] = write[wi].Value;
                            }
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
                or.And3.Type != CraConditionType.None ||
                or.And4.Type != CraConditionType.None ||
                or.And5.Type != CraConditionType.None ||
                or.And6.Type != CraConditionType.None ||
                or.And7.Type != CraConditionType.None ||
                or.And8.Type != CraConditionType.None ||
                or.And9.Type != CraConditionType.None;
        }

        bool CheckCondition(in CraConditionOr or, in CraStateData state)
        {
            return
                HasAtLeastOneCondition(or) &&
                CheckCondition(or.And0, state) &&
                CheckCondition(or.And1, state) &&
                CheckCondition(or.And2, state) &&
                CheckCondition(or.And3, state) &&
                CheckCondition(or.And4, state) &&
                CheckCondition(or.And5, state) &&
                CheckCondition(or.And6, state) &&
                CheckCondition(or.And7, state) &&
                CheckCondition(or.And8, state) &&
                CheckCondition(or.And9, state);
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
                conditionMet = input.Type == CraValueType.Trigger && input.ValueBool;
                //if (conditionMet)
                //{
                //    Debug.Log($"Triggered by: {con.Input.GetName()}");
                //    input.ValueBool = false;
                //    Inputs[con.Input.Handle.Index] = input;
                //}
            }
            else if (con.Type == CraConditionType.IsFinished && state.Player.IsValid() && CraPlaybackManager.Player_IsFinished(Players, state.Player))
            {
                conditionMet = true;
            }

            return conditionMet;
        }
    }
}