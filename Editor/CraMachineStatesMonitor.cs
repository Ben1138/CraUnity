using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class CraMachineStatesMonitor : EditorWindow
{
    [MenuItem("Cra/Machine States")]
    public static void OpenRuntimeMonitor()
    {
        CraMachineStatesMonitor window = CreateInstance<CraMachineStatesMonitor>();
        window.Show();
    }

    GameObject MonitoredObject;
    CraStateMachine? Monitored;

    CraMachineStatesTreeView StateMachineTree;

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

            StateMachineTree = CraMachineStatesTreeView.Create(Monitored.Value);
        }

        if (!Monitored.HasValue)
        {
            EditorGUILayout.LabelField("Selected GameObject is not animated by Cra!");
            return;
        }

        if (StateMachineTree != null)
        {
            StateMachineTree.OnGUI(new Rect(0f, 0f, position.width, position.height));
        }
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
            switch (con.Compare.Type)
            {
                case CraValueType.Int:
                    EditorGUILayout.LabelField("          Value:", $"{con.Compare.ValueInt} (int)");
                    break;
                case CraValueType.Float:
                    EditorGUILayout.LabelField("          Value:", $"{con.Compare.ValueFloat} (float)");
                    break;
                case CraValueType.Bool:
                    EditorGUILayout.LabelField("          Value:", $"{con.Compare.ValueBool} (bool)");
                    break;
                default:
                    EditorGUILayout.LabelField("          Value:", "UNHANDLED TYPE");
                    break;
            }
        }
    }
}