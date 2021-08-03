using System;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class CraMain
{
    CraPlaybackManager Players;
    CraAnimatorManager Animators;

    public CraMain()
    {
        Players = CraPlaybackManager.Get();
        Animators = CraAnimatorManager.Get();

#if UNITY_EDITOR
        EditorApplication.quitting += Destroy;
#endif
    }

    public void Tick()
    {
        Players.Tick();
        Animators.Tick();
    }

    public void Clear()
    {
        Players.Clear();
        Animators.Clear();
    }

    public void Destroy()
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