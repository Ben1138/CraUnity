using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;
using Unity.Jobs;
using UnityEngine.Jobs;


public class CraMainComponent : MonoBehaviour
{
    CraPlaybackManager Players;
    CraAnimatorManager Animators;


    void Awake()
    {
        Players = CraPlaybackManager.Get();
        Animators = CraAnimatorManager.Get();
    }

    void Update()
    {
        Players.Tick();
        Animators.Tick();
    }

    void OnDestroy()
    {
        Players.Destroy();
        Players = null;

        Animators.Destroy();
        Animators = null;
    }
}

public class CraAnimatorManager
{
    static CraAnimatorManager Instance;

    //CraDataContainer<CraPlayer>   States;
    CraDataContainer<CraLayer>    Layers;
    CraDataContainer<CraAnimator> Animators;

    CraAnimatorManager()
    {
        //States    = new CraDataContainer<CraPlayer>(CraSettings.MAX_PLAYERS);
        Layers    = new CraDataContainer<CraLayer>(CraSettings.MAX_LAYERS);
        Animators = new CraDataContainer<CraAnimator>(CraSettings.MAX_ANIMATORS);
    }

    public static CraAnimatorManager Get()
    {
        if (Instance != null)
        {
            return Instance;
        }

        Instance = new CraAnimatorManager();
        return Instance;
    }

    public CraLayer LayerNew()
    {

    }

    public void Clear()
    {
        //States.Clear();
        Layers.Clear();
        Animators.Clear();
    }

    public void Destroy()
    {
        //States.Destroy();
        Layers.Destroy();
        Animators.Destroy();
    }

    public void Tick()
    {
        
    }
}
