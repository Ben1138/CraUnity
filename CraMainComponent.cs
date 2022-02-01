using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Add this component to your scene, or make an instance of CraMain yourself somewhere and call the 'Tick' and 'Destroy' methods accordingly.
/// </summary>
public class CraMainComponent : MonoBehaviour
{
    CraMain Cra;

    void Awake()
    {
        Cra = new CraMain(new CraSettings
        {
            Players        = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            Clips          = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            ClipTransforms = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            Bones          = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },

            StateMachines = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            Inputs        = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            States        = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },
            Transitions   = new CraBufferSettings { Capacity = 1024, GrowFactor = 1.5f },

            MaxBones = 65535,

            BoneHashFunction = (string input) => { return input.GetHashCode(); }
        });
    }

    void Update()
    {
        Cra.Tick();
    }

    void OnDestroy()
    {
        Cra.Destroy();
    }
}