using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.IMGUI.Controls;

public class CraRuntimeMonitor : EditorWindow
{
    [MenuItem("Cra/Runtime Monitor")]
    public static void OpenRuntimeMonitor()
    {
        CraRuntimeMonitor window = GetWindow<CraRuntimeMonitor>();
        window.Show();
    }


    string[] Abr = new string[] { "B", "KB", "MB", "GB", "TB" };
    Vector2 Scroll;


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

    void DisplayMeasure(string display, in CraMeasure measure)
    {
        EditorGUILayout.LabelField($"{display} Elements", $"{measure.CurrentElements:n0} / {measure.MaxElements:n0}");
        EditorGUILayout.LabelField($"{display} Memory", $"{FormatBytes(measure.CurrentBytes)} / {FormatBytes(measure.MaxBytes)}");
        EditorGUILayout.Space();
    }

    void OnGUI()
    {
        if (CraMain.Instance != null)
        {
            Scroll = EditorGUILayout.BeginScrollView(Scroll);
            CraStatistics stats = CraMain.Instance.Statistics;
            DisplayMeasure("Playback", in stats.PlayerData);
            DisplayMeasure("Clip", in stats.ClipData);
            DisplayMeasure("Baked", in stats.BakedClipTransforms);
            DisplayMeasure("Bone", in stats.BoneData);
            DisplayMeasure("Transforms", in stats.Bones);
            EditorGUILayout.Space();
            DisplayMeasure("StateMachines", in stats.StateMachines);
            DisplayMeasure("Inputs", in stats.Inputs);
            DisplayMeasure("States", in stats.States);
            DisplayMeasure("Transitions", in stats.Transitions);
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            ulong totalBytes =
                stats.PlayerData.CurrentBytes +
                stats.ClipData.CurrentBytes +
                stats.BakedClipTransforms.CurrentBytes +
                stats.BoneData.CurrentBytes +
                stats.Bones.CurrentBytes +
                stats.StateMachines.CurrentBytes +
                stats.Inputs.CurrentBytes +
                stats.States.CurrentBytes +
                stats.Transitions.CurrentBytes;
            ulong totalMaxBytes =
                stats.PlayerData.MaxBytes +
                stats.ClipData.MaxBytes +
                stats.BakedClipTransforms.MaxBytes +
                stats.BoneData.MaxBytes +
                stats.Bones.MaxBytes +
                stats.StateMachines.MaxBytes +
                stats.Inputs.MaxBytes +
                stats.States.MaxBytes +
                stats.Transitions.MaxBytes;
            EditorGUILayout.LabelField("Total", FormatBytes(totalBytes) + " / " + FormatBytes(totalMaxBytes));
            EditorGUILayout.EndScrollView();
        }
    }
}