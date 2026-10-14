using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class PlatformTypeProfile
{
    [Tooltip("Must match the style string stored on a level platform, e.g. meadow or ice.")]
    public string styleKey = "meadow";
    public string displayName = "Meadow";
    [Tooltip("Optional sprite override. If empty, Wildrune uses the built-in sprite key for this style.")]
    public Sprite tileSprite;
    [Tooltip("Optional sprite key in Resources/Art when no direct Sprite is assigned, e.g. tile_grass.")]
    public string fallbackSpriteKey = "tile_grass";
    public bool slippery;
    [Range(0f, 1f)] public float friction = 0.12f;
    [Range(0f, 1f)] public float bounciness;
    [Tooltip("Optional tint applied to the platform sprite.")]
    public Color tint = Color.white;
}

[CreateAssetMenu(menuName = "Wildrune/Platform Defaults", fileName = "PlatformDefaults")]
public sealed class PlatformDefaults : ScriptableObject
{
    public List<PlatformTypeProfile> profiles = new List<PlatformTypeProfile>();
    private static PlatformDefaults cached;
    public static PlatformDefaults Current
    {
        get { if (cached == null) cached = Resources.Load<PlatformDefaults>("PlatformDefaults"); return cached; }
    }

    public PlatformTypeProfile Find(string styleKey)
    {
        if (profiles == null) return null;
        for (int i = 0; i < profiles.Count; i++)
            if (profiles[i] != null && string.Equals(profiles[i].styleKey, styleKey, StringComparison.OrdinalIgnoreCase)) return profiles[i];
        return null;
    }

    public static List<PlatformTypeProfile> CreateBuiltInProfiles()
    {
        return new List<PlatformTypeProfile>
        {
            new PlatformTypeProfile { styleKey="meadow", displayName="Meadow Grass", fallbackSpriteKey="tile_grass", friction=0.12f },
            new PlatformTypeProfile { styleKey="ember", displayName="Ember Rock", fallbackSpriteKey="tile_ember", friction=0.16f },
            new PlatformTypeProfile { styleKey="ice", displayName="Ice", fallbackSpriteKey="tile_ice", slippery=true, friction=0f },
            new PlatformTypeProfile { styleKey="canopy", displayName="Canopy", fallbackSpriteKey="tile_canopy", friction=0.1f },
            new PlatformTypeProfile { styleKey="ruins", displayName="Ruins", fallbackSpriteKey="tile_ruins", friction=0.14f },
            new PlatformTypeProfile { styleKey="stone", displayName="Stone", fallbackSpriteKey="tile_stone", friction=0.16f },
            new PlatformTypeProfile { styleKey="snow", displayName="Snow", fallbackSpriteKey="tile_snow", friction=0.06f }
        };
    }
}
