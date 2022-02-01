using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Main root instance of Cra. Singleton. 
// You can either place a CraMainComponent into your scene, or create an instance of this yourself.
public class CraMain
{
    public static CraMain Instance { get; private set; }
    public CraPlaybackManager Players { get; private set; }
    public CraStateMachineManager StateMachines { get; private set; }
    public CraSettings Settings { get; private set; }


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

        Settings = settings;
        Players = new CraPlaybackManager();
        StateMachines = new CraStateMachineManager();
        Instance = this;

#if UNITY_EDITOR
        EditorApplication.quitting += Destroy;
#endif
    }

    public void Tick()
    {
        Players.Tick();
        StateMachines.Tick();
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