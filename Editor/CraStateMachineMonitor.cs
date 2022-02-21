using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class CraStateMachineMonitor : EditorWindow
{
    [MenuItem("Cra/State Machine Monitor")]
    public static void OpenRuntimeMonitor()
    {
        CraStateMachineMonitor window = GetWindow<CraStateMachineMonitor>();
        window.Show();
    }

    GameObject MonitoredObject;
    CraStateMachine? Monitored;
    int ViewLayer = 0;
    string[] ViewLayerNames;
    CraInput[] Inputs;
    CraOutput[] Outputs;
    CraLayer[] Layers;
    CraState[] States;
    CraTransition[][] StateTransitions;
    bool[] StatesFoldout;

    Vector2 ScrollPos;
    bool ShowPlayer;


    void Update()
    {
        Repaint();
    }

    void OnGUI()
    {
        if (CraMain.Instance == null || CraMain.Instance.StateMachines == null)
        {
            EditorGUILayout.LabelField("Cra not running");
            return;
        }

        if (Selection.activeGameObject == null)
        {
            EditorGUILayout.LabelField("Select a GameObject in Hierarchy!");
            return;
        }

        if (MonitoredObject != Selection.activeGameObject)
        {
            Monitored = null;
            MonitoredObject = Selection.activeGameObject;

            Component[] comps = MonitoredObject.GetComponents<Component>();
            for (int i = 0; i < comps.Length; ++i)
            {
                if (comps[i] is ICraAnimated)
                {
                    Monitored = (comps[i] as ICraAnimated).GetStateMachine();
                    break;
                }
            }

            if (!Monitored.HasValue)
            {
                EditorGUILayout.LabelField("Selected GameObject is not animated by Cra!");
                return;
            }

            if (!Monitored.Value.IsValid())
            {
                EditorGUILayout.LabelField("Selected GameObject is animated by Cra, but returned no valid CraAnimator!");
                return;
            }

            Inputs = Monitored.Value.GetInputs();
            Outputs = Monitored.Value.GetOutputs();
            Layers = Monitored.Value.GetLayers();
            ViewLayerNames = new string[Layers.Length];
            for (int i = 0; i < ViewLayerNames.Length; ++i)
            {
                Debug.Assert(Layers[i].IsValid());
                ViewLayerNames[i] = "Layer " + i;
            }

            if (ViewLayer >= Layers.Length)
            {
                ViewLayer = 0;
            }
        }

        if (!Monitored.HasValue)
        {
            EditorGUILayout.LabelField("Selected GameObject is not animated by Cra!");
            return;
        }

        GUILayout.Label("Inputs:");
        if (Inputs.Length == 0)
        {
            EditorGUILayout.LabelField("State machine has no inputs!");
        }
        else
        {
            for (int i = 0; i < Inputs.Length; ++i)
            {
                CraValueUnion value = Inputs[i].GetValue();
                switch (value.Type)
                {
                    case CraValueType.Int:
                        EditorGUILayout.LabelField($"{Inputs[i].GetName()}:", $"{value.ValueInt}  (int)");
                        break;
                    case CraValueType.Float:
                        EditorGUILayout.LabelField($"{Inputs[i].GetName()}:", $"{value.ValueFloat:n2}  (float)");
                        break;
                    case CraValueType.Bool:
                        EditorGUILayout.LabelField($"{Inputs[i].GetName()}:", $"{value.ValueBool}  (bool)");
                        break;
                    case CraValueType.Trigger:
                        EditorGUILayout.LabelField($"{Inputs[i].GetName()}:", $"{value.ValueBool}  (trigger)");
                        break;
                    default:
                        EditorGUILayout.LabelField($"{Inputs[i].GetName()}:", "UNHANDLED TYPE");
                        break;
                }
            }
        }
        EditorGUILayout.Space();
        GUILayout.Label("Outputs:");
        if (Outputs.Length == 0)
        {
            EditorGUILayout.LabelField("State machine has no outputs!");
        }
        else
        {
            for (int i = 0; i < Outputs.Length; ++i)
            {
                CraValueUnion value = Outputs[i].GetValue();
                switch (value.Type)
                {
                    case CraValueType.Int:
                        EditorGUILayout.LabelField($"{Outputs[i].GetName()}:", $"{value.ValueInt}  (int)");
                        break;
                    case CraValueType.Float:
                        EditorGUILayout.LabelField($"{Outputs[i].GetName()}:", $"{value.ValueFloat:n2}  (float)");
                        break;
                    case CraValueType.Bool:
                        EditorGUILayout.LabelField($"{Outputs[i].GetName()}:", $"{value.ValueBool}  (bool)");
                        break;
                    default:
                        EditorGUILayout.LabelField($"{Outputs[i].GetName()}:", "UNHANDLED TYPE");
                        break;
                }
            }
        }
        EditorGUILayout.Space();

        int tmp = ViewLayer;
        ViewLayer = EditorGUILayout.Popup(ViewLayer, ViewLayerNames);
        EditorGUILayout.Space();

        CraLayer layer = Layers[ViewLayer];
        if (ViewLayer != tmp || States == null)
        {
            States = layer.GetAllStates();
            StatesFoldout = new bool[States.Length];
            StateTransitions = new CraTransition[States.Length][];
            for (int i = 0; i < States.Length; ++i)
            {
                Debug.Assert(States[i].IsValid());
                StateTransitions[i] = States[i].GetTransitions();
            }
        }

        if (States.Length == 0)
        {
            EditorGUILayout.LabelField("No States on Layer");
            return;
        }

        ScrollPos = EditorGUILayout.BeginScrollView(ScrollPos);

        CraState active = layer.GetActiveState();
        for (int si = 0; si < States.Length; ++si)
        {
            CraState state = States[si];
            Debug.Assert(state.IsValid());

            StatesFoldout[si] = EditorGUILayout.Foldout(StatesFoldout[si], $"{state.GetName()} {(state == active ? "[ACTIVE]" : "")}", true);
            if (StatesFoldout[si])
            {
                CraPlayer player = state.GetPlayer();
                if (player.IsValid())
                {
                    var range = player.GetPlayRange();
                    EditorGUILayout.LabelField("Play Range Time:", $"{range.MinTime} - {range.MaxTime}");
                    EditorGUILayout.LabelField("Assigned Bones:", player.GetAssignedBonesCount().ToString());
                }

                CraTransition[] transitions = StateTransitions[si];
                if (transitions.Length == 0)
                {
                    EditorGUILayout.LabelField("No Transitions");
                }
                else
                {
                    for (int ti = 0; ti < transitions.Length; ++ti)
                    {
                        CraTransition tran = transitions[ti];
                        Debug.Assert(tran.IsValid());

                        CraTransitionData data = tran.GetData();

                        EditorGUILayout.LabelField("Target State:", data.Target.GetName());
                        EditorGUILayout.LabelField("Transition Time:", $"{data.TransitionTime}");
                        EditorGUILayout.LabelField("Conditions:");

                        DrawConditions(data.Or0);
                        DrawConditions(data.Or1);
                        DrawConditions(data.Or2);
                        DrawConditions(data.Or3);
                    }
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.Space();
        EditorGUILayout.Space();

        ShowPlayer = EditorGUILayout.Foldout(ShowPlayer, "Playback", true);
        if (ShowPlayer)
        {
            if (!active.IsValid())
            {
                EditorGUILayout.LabelField("No active state in selected layer");
            }
            else
            {
                CraPlayer player = active.GetPlayer();
                if (player.IsValid())
                {
                    EditorGUILayout.LabelField("Playback Speed");
                    player.SetPlaybackSpeed(EditorGUILayout.Slider(player.GetPlaybackSpeed(), 0f, 10f));

                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Looping");
                    player.SetLooping(EditorGUILayout.Toggle(player.IsLooping()));

                    EditorGUILayout.Space();
                    if (GUILayout.Button(player.IsPlaying() ? "Stop" : "Play"))
                    {
                        if (player.IsPlaying())
                        {
                            player.Reset();
                        }
                        else
                        {
                            player.CaptureBones();
                            player.Play();
                        }
                    }

                    EditorGUILayout.Space();
                    EditorGUILayout.Slider(player.GetTime(), 0f, player.GetClip().GetDuration());
                    if (GUILayout.Button("Capture Bones"))
                    {
                        player.CaptureBones();
                    }
                }
            }
        }

        EditorGUILayout.EndScrollView();
    }

    int NumConditions(in CraConditionOr or)
    {
        int count = 0;
        if (or.And0.Type != CraConditionType.None) count++;
        if (or.And1.Type != CraConditionType.None) count++;
        if (or.And2.Type != CraConditionType.None) count++;
        if (or.And3.Type != CraConditionType.None) count++;
        return count;
    }

    void DrawConditions(in CraConditionOr Or)
    {
        int numConds = NumConditions(Or);
        if (numConds == 0)
        {
            return;
        }

        EditorGUILayout.LabelField("  Or");
        DrawCondition(Or.And0, numConds > 1);
        DrawCondition(Or.And1, numConds > 1);
        DrawCondition(Or.And2, numConds > 1);
        DrawCondition(Or.And3, numConds > 1);
    }

    void DrawCondition(in CraCondition con, bool drawAnd)
    {
        if (con.Type == CraConditionType.None)
        {
            return;
        }
        if (drawAnd)
        {
            EditorGUILayout.LabelField("      And");
        }
        EditorGUILayout.LabelField("          Input:", con.Input.GetName());
        EditorGUILayout.LabelField("          Condition:", $"{con.Type}");
        if (con.Type != CraConditionType.IsFinished)
        {
            switch (con.Value.Type)
            {
                case CraValueType.Int:
                    EditorGUILayout.LabelField("          Value:", $"{con.Value.ValueInt} (int)");
                    break;
                case CraValueType.Float:
                    EditorGUILayout.LabelField("          Value:", $"{con.Value.ValueFloat} (float)");
                    break;
                case CraValueType.Bool:
                    EditorGUILayout.LabelField("          Value:", $"{con.Value.ValueBool} (bool)");
                    break;
                default:
                    EditorGUILayout.LabelField("          Value:", "UNHANDLED TYPE");
                    break;
            }
        }
    }
}