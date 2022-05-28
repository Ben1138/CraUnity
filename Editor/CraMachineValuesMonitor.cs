using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public class CraMachineValuesMonitor : EditorWindow
{
    [MenuItem("Cra/Machine Values")]
    public static void OpenRuntimeMonitor()
    {
        CraMachineValuesMonitor window = CreateInstance<CraMachineValuesMonitor>();
        window.Show();
    }

    GameObject MonitoredObject;
    CraStateMachine? Monitored;

    CraMachineValueTreeView MachineValueTreeView;


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

            MachineValueTreeView = CraMachineValueTreeView.Create(Monitored.Value.GetMachineValues());
        }

        if (!Monitored.HasValue)
        {
            EditorGUILayout.LabelField("Selected GameObject is not animated by Cra!");
            return;
        }

        if (MachineValueTreeView != null)
        {
            MachineValueTreeView.OnGUI(new Rect(0f, 0f, position.width, position.height));
        }
    }
}