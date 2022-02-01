using System;
using UnityEngine;
using UnityEngine.Jobs;
using Unity.Jobs;

#if UNITY_EDITOR
using UnityEditor;
#endif

// Main root instance of Cra. Singleton. 
// You can either place a CraMainComponent into your scene, or create an instance of this yourself.
public unsafe partial class CraMain
{
    public static CraMain Instance { get; private set; }
    public CraPlaybackManager Players { get; private set; }
    public CraStateMachineManager StateMachines { get; private set; }
    public CraSettings Settings { get; private set; }

    public object Lock { get; private set; }

#if UNITY_EDITOR
    public CraStatistics Statistics { get; private set; } = new CraStatistics();
#endif

    // Jobs
    CraPlayJob PlayerJob;
    CraBoneEvalJob BoneJob;
    CraStateMachineJob MachineJob;

    CraBuffer<CraPlayerData> PlayerData;
    int PlayerCounter;

    public CraMain(CraSettings settings)
    {
        if (Instance != null)
        {
            throw new ArgumentException("Tried to instantiate more than one CraMain!");
        }
        if (settings.Players.Capacity % 4 != 0)
        {
            throw new ArgumentException("The capacity of players must be a multiple of 4!");
        }
        Instance = this;
        Lock = new object();
        Settings = settings;

        PlayerData = new CraBuffer<CraPlayerData>(Settings.Players);
        Players = new CraPlaybackManager();
        StateMachines = new CraStateMachineManager();

#if UNITY_EDITOR
        EditorApplication.quitting += Destroy;
#endif
    }

    public void Tick()
    {
        lock (Lock)
        {
            JobHandle playerJob = Players.Schedule();
            StateMachines.Schedule(playerJob);
        }

#if UNITY_EDITOR
        Players.UpdateStatistics();
#endif
    }

    public void Clear()
    {
        Players.Clear();
        StateMachines.Clear();
    }

    public void Destroy()
    {
        Players.Destroy();
        Players = null;

        StateMachines.Destroy();
        StateMachines = null;
    }
}