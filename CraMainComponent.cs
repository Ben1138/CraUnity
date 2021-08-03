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
        Cra = new CraMain();
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