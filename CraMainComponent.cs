using System;
using System.Collections.Generic;
using UnityEngine;

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

public class CraStatistics
{
    public CraMeasure PlayerData;
    public CraMeasure ClipData;
    public CraMeasure BakedClipTransforms;
    public CraMeasure BoneData;
    public CraMeasure Bones;
}

public static class CraSettings
{
    public const int STATE_NONE = -1;
    public const int MAX_PLAYERS = 16384;
    public const int MAX_LAYERS = 4096;
    public const int MAX_ANIMATORS = 2048;

    public const int MAX_PLAYER_DATA = MAX_PLAYERS / 4;
    public const int MAX_CLIP_DATA = 256;
    public const int MAX_BAKED_CLIP_TRANSFORMS = 65535 * 4;
    public const int MAX_BONE_DATA = 65535 * 4;
    public const int MAX_BONES = 65535 * 4;

    public static Func<string, int> BoneHashFunction;
}