using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>AI archetype used by an enemy profile, independent from its visual kind.</summary>
public enum EnemyBehaviourStyle
{
    PatrolWalker,
    MeleeChaser,
    RangedKiter,
    FlyingHunter,
    Hopper,
    Boss
}

/// <summary>
/// All editable defaults for one enemy kind. Sprites are Texture2D assets so they can be
/// replaced by dragging any texture into the Inspector; GameArt turns them into crisp runtime sprites.
/// </summary>
[Serializable]
public sealed class EnemyTypeDefaults
{
    [Header("Identity and appearance")]
    [HideInInspector] public EnemyKind kind = EnemyKind.Melee;
    public string displayName = "Melee Goblin";
    public Texture2D frame0;
    public Texture2D frame1;
    [Min(8f)] public float pixelsPerUnit = 48f;
    [Range(0.5f, 3f)] public float visualScale = 1f;
    public Color tint = Color.white;
    [Min(0.05f)] public float animationFrameInterval = 0.31f;

    [Header("Health, movement and score")]
    [Min(1)] public int health = 3;
    [Min(0.1f)] public float moveSpeed = 2.1f;
    [Min(0)] public int touchDamage = 1;
    [Min(0)] public int scoreValue = 100;
    [Min(0f)] public float gravityScale = 3.2f;

    [Header("Collider")]
    [Tooltip("BoxCollider2D width and height in local world units.")] public Vector2 colliderSize = new Vector2(0.68f, 0.8f);
    public Vector2 colliderOffset = new Vector2(0f, 0.4f);

    [Header("AI style and melee detection")]
    public EnemyBehaviourStyle behaviour = EnemyBehaviourStyle.MeleeChaser;
    [Min(0.1f)] public float detectionRange = 5f;
    [Min(0.1f)] public float detectionVerticalRange = 3.2f;
    [Min(0f)] public float chaseStopDistance = 1.2f;
    [Min(0.1f)] public float contactRange = 0.78f;
    [Min(0.1f)] public float contactVerticalRange = 1.1f;
    [Min(0.1f)] public float contactCooldown = 1.05f;
    [Range(0.05f, 1f)] public float closeRangeSpeedMultiplier = 0.35f;

    [Header("Ranged behaviour")]
    [Min(0.1f)] public float rangedRetreatDistance = 4.5f;
    [Min(0.1f)] public float rangedPreferredDistance = 8.5f;
    [Min(0.1f)] public float rangedApproachLimit = 13f;
    [Min(0.1f)] public float rangedAttackRange = 12f;
    [Min(0.1f)] public float rangedVerticalRange = 4f;
    [Min(0.1f)] public float rangedCooldownMin = 1.55f;
    [Min(0.1f)] public float rangedCooldownMax = 2.2f;
    [Min(0.1f)] public float projectileSpeed = 9.3f;
    [Min(0)] public int projectileDamage = 1;
    [Tooltip("Used when projectileTexture is empty; key under Resources/Art, without extension.")]
    public string projectileSpriteKey = "projectile_enemy_arrow";
    public Texture2D projectileTexture;
    [Min(8f)] public float projectilePixelsPerUnit = 32f;
    [Min(0.1f)] public float projectileOriginHeight = 0.63f;

    [Header("Boss volley behaviour")]
    [Min(0.1f)] public float bossVolleyRange = 4.5f;
    [Min(0.1f)] public float bossVolleyCooldown = 2.1f;
    [Range(0f, 45f)] public float bossVolleySpreadDegrees = 9f;
    [Range(1, 7)] public int bossVolleyCount = 3;
    [Min(0.1f)] public float bossProjectileSpeed = 10.5f;
    [Min(0)] public int bossProjectileDamage = 1;
    [Min(0.1f)] public float bossProjectileOriginHeight = 0.86f;

    [Header("Hopper behaviour")]
    [Min(0.1f)] public float hopVelocity = 6f;
    [Min(0.1f)] public float hopIntervalMin = 1.1f;
    [Min(0.1f)] public float hopIntervalMax = 1.8f;
    [Min(0.1f)] public float firstHopDelayMin = 0.5f;
    [Min(0.1f)] public float firstHopDelayMax = 1.4f;

    [Header("Flying behaviour")]
    [Min(0.1f)] public float flyingChaseRange = 7f;
    public float flyingTargetYOffset = 0.7f;
    [Min(0f)] public float flyingHoverAmplitude = 0.55f;
    [Min(0.1f)] public float flyingHoverFrequency = 2.8f;

    public Sprite GetFrame(int frame)
    {
        Texture2D texture = frame == 0 ? frame0 : (frame1 != null ? frame1 : frame0);
        if (texture == null) return null;
        return GameArt.GetFromTexture(texture, "enemy-profile-" + kind + "-" + frame, pixelsPerUnit, true);
    }

    public Sprite GetProjectileSprite()
    {
        if (projectileTexture != null)
            return GameArt.GetFromTexture(projectileTexture, "enemy-projectile-profile-" + kind, projectilePixelsPerUnit, false);
        return GameArt.Get(projectileSpriteKey);
    }
}

/// <summary>
/// Central edit point for enemy defaults. Level assets only choose an EnemyKind; runtime stats,
/// art, collider, and behavior are resolved here so enemy setup is not scattered across scripts.
/// </summary>
[CreateAssetMenu(menuName = "Wildrune/Enemy Defaults", fileName = "EnemyDefaults")]
public sealed class EnemyDefaults : ScriptableObject
{
    public List<EnemyTypeDefaults> profiles = new List<EnemyTypeDefaults>();

    private static EnemyDefaults cached;
    public static EnemyDefaults Current
    {
        get
        {
            if (cached == null) cached = Resources.Load<EnemyDefaults>("EnemyDefaults");
            return cached;
        }
    }

    public static EnemyTypeDefaults GetProfile(EnemyKind kind)
    {
        EnemyDefaults asset = Current;
        return asset != null ? asset.Find(kind) : null;
    }

    public EnemyTypeDefaults Find(EnemyKind kind)
    {
        if (profiles == null) return null;
        for (int i = 0; i < profiles.Count; i++)
            if (profiles[i] != null && profiles[i].kind == kind) return profiles[i];
        return null;
    }

    public static string DisplayName(EnemyKind kind)
    {
        EnemyTypeDefaults profile = GetProfile(kind);
        return profile != null && !string.IsNullOrWhiteSpace(profile.displayName)
            ? profile.displayName : kind.ToString();
    }

    public static List<EnemyTypeDefaults> CreateDefaultProfiles()
    {
        return new List<EnemyTypeDefaults>
        {
            CreateDefaultProfile(EnemyKind.Slime),
            CreateDefaultProfile(EnemyKind.Melee),
            CreateDefaultProfile(EnemyKind.Archer),
            CreateDefaultProfile(EnemyKind.Bat),
            CreateDefaultProfile(EnemyKind.Brute),
            CreateDefaultProfile(EnemyKind.Boss)
        };
    }

    public static EnemyTypeDefaults CreateDefaultProfile(EnemyKind kind)
    {
        EnemyTypeDefaults p = new EnemyTypeDefaults { kind = kind };
        switch (kind)
        {
            case EnemyKind.Slime:
                p.displayName = "Hopping Slime"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_slime_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_slime_1");
                p.pixelsPerUnit = 32f; p.visualScale = 1f; p.behaviour = EnemyBehaviourStyle.Hopper; p.health = 2; p.moveSpeed = 1.7f; p.touchDamage = 1; p.scoreValue = 75;
                p.colliderSize = new Vector2(0.68f, 0.55f); p.colliderOffset = new Vector2(0f, 0.28f); p.gravityScale = 3.2f; p.hopVelocity = 6f;
                break;
            case EnemyKind.Melee:
                p.displayName = "Goblin Swordsman"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_goblin_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_goblin_1");
                p.pixelsPerUnit = 48f; p.behaviour = EnemyBehaviourStyle.MeleeChaser; p.health = 3; p.moveSpeed = 2.1f; p.touchDamage = 1; p.scoreValue = 100;
                p.colliderSize = new Vector2(0.68f, 0.8f); p.colliderOffset = new Vector2(0f, 0.4f); p.gravityScale = 3.2f;
                break;
            case EnemyKind.Archer:
                // The supplied mushroom reads as a thrower; the supplied skeleton actually carries a sword/shield.
                p.displayName = "Mushroom Slinger"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_brute_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_brute_1");
                p.pixelsPerUnit = 48f; p.visualScale = 1f; p.behaviour = EnemyBehaviourStyle.RangedKiter; p.health = 2; p.moveSpeed = 1f; p.touchDamage = 1; p.scoreValue = 125;
                p.colliderSize = new Vector2(0.68f, 0.8f); p.colliderOffset = new Vector2(0f, 0.4f); p.gravityScale = 3.2f;
                p.projectileTexture = Resources.Load<Texture2D>("Art/projectile_mushroom"); p.projectilePixelsPerUnit = 24f; p.projectileSpriteKey = "projectile_enemy_arrow";
                p.projectileSpeed = 9.3f; p.projectileDamage = 1; p.projectileOriginHeight = 0.63f;
                break;
            case EnemyKind.Bat:
                p.displayName = "Flying Eye"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_bat_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_bat_1");
                p.pixelsPerUnit = 48f; p.behaviour = EnemyBehaviourStyle.FlyingHunter; p.health = 1; p.moveSpeed = 2.5f; p.touchDamage = 1; p.scoreValue = 100; p.gravityScale = 0f;
                p.colliderSize = new Vector2(0.72f, 0.58f); p.colliderOffset = new Vector2(0f, 0.48f); p.flyingChaseRange = 7f;
                break;
            case EnemyKind.Brute:
                p.displayName = "Skeleton Heavy"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_archer_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_archer_1");
                p.pixelsPerUnit = 48f; p.behaviour = EnemyBehaviourStyle.MeleeChaser; p.health = 5; p.moveSpeed = 1.25f; p.touchDamage = 2; p.scoreValue = 200; p.gravityScale = 3.4f;
                p.colliderSize = new Vector2(0.82f, 1.12f); p.colliderOffset = new Vector2(0f, 0.56f); p.detectionRange = 5f;
                break;
            case EnemyKind.Boss:
                p.displayName = "Crown Warden"; p.frame0 = Resources.Load<Texture2D>("Art/enemy_boss_0"); p.frame1 = Resources.Load<Texture2D>("Art/enemy_boss_1");
                p.pixelsPerUnit = 48f; p.visualScale = 1f; p.behaviour = EnemyBehaviourStyle.Boss; p.health = 24; p.moveSpeed = 1.25f; p.touchDamage = 2; p.scoreValue = 1500; p.gravityScale = 3.4f;
                p.colliderSize = new Vector2(1.18f, 1.42f); p.colliderOffset = new Vector2(0f, 0.68f); p.detectionRange = 13f;
                p.bossVolleyRange = 4.5f; p.bossVolleyCooldown = 2.1f; p.bossVolleySpreadDegrees = 9f; p.bossVolleyCount = 3; p.bossProjectileSpeed = 10.5f; p.bossProjectileDamage = 1; p.bossProjectileOriginHeight = 0.86f;
                break;
        }
        return p;
    }
}
