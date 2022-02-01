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
        if (CraMain.Instance != null)
        {
            CraStatistics stats = CraMain.Instance.Statistics;
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
    }
}