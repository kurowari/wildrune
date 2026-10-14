using System.Collections.Generic;
using UnityEngine;

/// <summary>Loads crisp, point-filtered pixel art from Assets/Resources/Art.</summary>
public static class GameArt
{
    private static readonly Dictionary<string, Sprite> Cache = new Dictionary<string, Sprite>();

    public static Sprite Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        Texture2D texture = Resources.Load<Texture2D>("Art/" + key);
        if (texture == null)
        {
            Debug.LogWarning("Wildrune art not found: Art/" + key);
            return null;
        }

        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        float ppu = PixelsPerUnitFor(key);
        Vector2 pivot = IsCharacterArt(key) ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot, ppu,
            0, SpriteMeshType.FullRect);
        sprite.name = key;
        Cache[key] = sprite;
        return sprite;
    }


    /// <summary>Creates a crisp runtime sprite from a texture assigned in EnemyDefaults.</summary>
    public static Sprite GetFromTexture(Texture2D texture, string cacheKey, float pixelsPerUnit, bool bottomPivot)
    {
        if (texture == null) return null;
        string key = "__texture__" + cacheKey + "__" + texture.GetEntityId() + "__" + pixelsPerUnit + "__" + bottomPivot;
        if (Cache.TryGetValue(key, out Sprite cached) && cached != null) return cached;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Vector2 pivot = bottomPivot ? new Vector2(0.5f, 0f) : new Vector2(0.5f, 0.5f);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), pivot,
            Mathf.Max(1f, pixelsPerUnit), 0, SpriteMeshType.FullRect);
        sprite.name = key;
        Cache[key] = sprite;
        return sprite;
    }

    private static bool IsCharacterArt(string key)
    {
        return key.StartsWith("player_") || key.StartsWith("enemy_");
    }

    private static float PixelsPerUnitFor(string key)
    {
        // The supplied Adventurer frames are 50x37 cells and keep the existing player collider scale.
        if (key.StartsWith("player_")) return 32f;
        // The Brackeys slime frames are 24px cells; keep them chunky and readable.
        if (key.StartsWith("enemy_slime_")) return 32f;
        // The source mushroom is physically short but should visually read as a heavier brute.
        if (key.StartsWith("enemy_brute_")) return 32f;
        // Fantasy monster frames are cropped to a consistent 48x56 canvas (boss frames 64x64).
        if (key.StartsWith("enemy_")) return 48f;
        if (key.StartsWith("bg_")) return 32f;
        return 16f;
    }
}
