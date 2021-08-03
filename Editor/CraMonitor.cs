using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class CraMonitor : EditorWindow
{
    int ViewLayer = 0;
    string[] ViewLayerNames;

    string[] Abr = new string[] { "Bytes", "KB", "MB", "GB", "TB" };

    GameObject MonitoredObject;
    CraAnimator? Monitored;

    [MenuItem("Cra/Runtime Monitor")]
    public static void OpenRuntimeMonitor()
    {
        CraMonitor window = GetWindow<CraMonitor>();
        window.Show();
    }

    void Update()
    {
        Repaint();
    }

    string FormatBytes(ulong numBytes)
    {
        double num = numBytes;
        int abrCounter = 0;
        while (num > 1000)
        {
            num /= 1000;
            abrCounter++;
        }
        return string.Format("{0:0.##} {1}", num, Abr[abrCounter]);
    }

    void DisplayMeasure(string display, ref CraMeasure measure)
    {
        EditorGUILayout.LabelField(display + " Elements", measure.CurrentElements + " / " + measure.MaxElements);
        EditorGUILayout.LabelField(display + " Memory", FormatBytes(measure.CurrentBytes) + " / " + FormatBytes(measure.MaxBytes));
        EditorGUILayout.Space();
    }

    void OnGUI()
    {
        if (CraPlaybackManager.Instance != null)
        {
            CraStatistics stats = CraPlaybackManager.Instance.Statistics;
            DisplayMeasure("Playback", ref stats.PlayerData);
            DisplayMeasure("Clip", ref stats.ClipData);
            DisplayMeasure("Baked", ref stats.BakedClipTransforms);
            DisplayMeasure("Bone", ref stats.BoneData);
            DisplayMeasure("Transforms", ref stats.Bones);
            EditorGUILayout.Space();
            ulong totalBytes =
                stats.PlayerData.CurrentBytes +
                stats.ClipData.CurrentBytes +
                stats.BakedClipTransforms.CurrentBytes +
                stats.BoneData.CurrentBytes +
                stats.Bones.CurrentBytes;
            ulong totalMaxBytes =
                stats.PlayerData.MaxBytes +
                stats.ClipData.MaxBytes +
                stats.BakedClipTransforms.MaxBytes +
                stats.BoneData.MaxBytes +
                stats.Bones.MaxBytes;
            EditorGUILayout.LabelField("Total", FormatBytes(totalBytes) + " / " + FormatBytes(totalMaxBytes));
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
                    Monitored = (comps[i] as ICraAnimated).GetAnimator();
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

            ViewLayerNames = new string[Monitored.Value.GetNumLayers()];
            for (int i = 0; i < ViewLayerNames.Length; ++i)
            {
                ViewLayerNames[i] = "Layer " + i;
            }
        }

        if (!Monitored.HasValue)
        {
            EditorGUILayout.LabelField("Selected GameObject is not animated by Cra!");
            return;
        }

        ViewLayer = EditorGUILayout.Popup(ViewLayer, ViewLayerNames);
        CraPlayer state = Monitored.Value.GetCurrentState(ViewLayer);

        if (state.IsValid())
        {
            EditorGUILayout.LabelField("Player Handle", state.Handle.Handle.ToString());
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("State Idx", Monitored.Value.GetCurrentStateIdx(ViewLayer).ToString());
            EditorGUILayout.Space();

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
            EditorGUILayout.Slider(state.GetPlayback(), 0f, state.GetDuration());
        }
        else
        {
            EditorGUILayout.LabelField("NO STATE");
        }
    }
}