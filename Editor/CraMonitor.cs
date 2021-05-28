using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class CraMonitor : EditorWindow
{
    int ViewLayer = 0;
    string[] ViewLayerNames;
    
    [MenuItem("Cra/Runtime Monitor")]
    public static void OpenLuaEditor()
    {
        CraMonitor window = GetWindow<CraMonitor>();
        window.Show();
    }

    void Awake()
    {
        ViewLayerNames = new string[CraSettings.STATE_MAX_LAYERS];
        for (int i = 0; i < ViewLayerNames.Length; ++i)
        {
            ViewLayerNames[i] = "Layer " + i;
        }
    }

    void Update()
    {
        Repaint();
    }

    void OnGUI()
    {
        //string memStr = "";
        //if (CraClip.GlobalBakeMemoryConsumption >= 1000)
        //{
        //    memStr = (CraClip.GlobalBakeMemoryConsumption / 1000f).ToString() + " KB";
        //}
        //else if (CraClip.GlobalBakeMemoryConsumption >= 1000000)
        //{
        //    memStr = (CraClip.GlobalBakeMemoryConsumption / 1000000f).ToString() + " MB";
        //}
        //else
        //{
        //    memStr = CraClip.GlobalBakeMemoryConsumption.ToString() + " Bytes";
        //}
        //EditorGUILayout.LabelField("Animation Memory", memStr);
        //EditorGUILayout.Space();

        if (Selection.activeGameObject == null)
        {
            EditorGUILayout.LabelField("Select a GameObject in Hierarchy!");
            return;
        }

        CraAnimator anim = Selection.activeGameObject.GetComponent<CraAnimator>();
        if (anim == null)
        {
            EditorGUILayout.LabelField("Selected GameObject does not have a CraAnimator component!");
            return;
        }

        ViewLayer = EditorGUILayout.Popup(ViewLayer, ViewLayerNames);

        CraPlayer state = anim.GetCurrentState(ViewLayer);
        if (state != null)
        {
            EditorGUILayout.LabelField("Playback Speed");
            state.SetPlaybackSpeed(EditorGUILayout.Slider(state.GetPlaybackSpeed(), 0f, 10f));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Looping");
            state.SetLooping(EditorGUILayout.Toggle(state.IsLooping()));

            EditorGUILayout.Space();
            if (GUILayout.Button(state.IsPlaying() ? "Stop" : "Play"))
            {
                if (state.IsPlaying())
                {
                    state.Reset();
                }
                else
                {
                    state.Play();
                }
            }

            EditorGUILayout.Space();
            float pos = EditorGUILayout.Slider(state.GetPlayback(), 0f, state.GetDuration());
            if (pos != state.GetPlayback())
            {
                state.Reset();
                state.EvaluateFrame(pos);
            }
        }
        else
        {
            EditorGUILayout.LabelField("NO STATE");
        }
    }
}