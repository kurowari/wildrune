using UnityEngine;

/// <summary>
/// Central balance knobs. The editor creates Assets/Resources/GameTuning.asset on first open;
/// edit the asset in the Inspector instead of changing gameplay code for routine tuning.
/// </summary>
[CreateAssetMenu(menuName = "Wildrune/Game Tuning", fileName = "GameTuning")]
public sealed class GameTuning : ScriptableObject
{
    [HideInInspector] public int tuningSchemaVersion = 0;

    [Header("Timer and difficulty")]
    [Min(15)] public float normalTimeLimitSeconds = 180f;
    [Min(15)] public float hardTimeLimitSeconds = 120f;
    [Min(1)] public int coinsPerHeart = 7;

    [Header("Ground movement")]
    [Min(1)] public float walkSpeed = 5.8f;
    [Min(1)] public float runSpeed = 9.0f;
    [Min(1)] public float groundAcceleration = 38f;
    [Min(1)] public float groundDeceleration = 48f;
    [Min(0.1f)] public float airAcceleration = 18f;
    [Min(0.1f)] public float airDeceleration = 5f;
    [Min(1)] public float iceSpeedMultiplier = 1.65f;
    [Min(1)] public float iceAcceleration = 36f;
    [Min(0.1f)] public float iceDeceleration = 0.25f;
    [Min(1f)] public float iceTurnAcceleration = 12f;

    [Header("Jump and wall movement")]
    [Min(1)] public float gravityScale = 3.0f;
    [Min(1)] public float jumpVelocity = 14.5f;
    [Min(1)] public float doubleJumpVelocity = 12.8f;
    [Range(0.15f, 0.9f)] public float jumpReleaseMultiplier = 0.42f;
    [Min(1)] public float wallJumpHorizontalSpeed = 10.8f;
    [Min(1)] public float wallSlideMaxFallSpeed = 2.5f;

    [Header("Global character size and collision")]
    [Tooltip("Player body collider width in world units, before the visual scale is applied.")]
    [Min(0.2f)] public float playerColliderWidth = 0.60f;
    [Tooltip("Player body collider height in world units, before the visual scale is applied.")]
    [Min(0.3f)] public float playerColliderHeight = 1.22f;
    [Tooltip("Uniform scale of the player sprite and player collider.")]
    [Range(0.5f, 2.5f)] public float playerVisualScale = 1.0f;

    [Header("Projectile defaults")]
    [Tooltip("Horizontal distance from the center of the player sprite to the arrow's spawn point.")]
    [Min(0.05f)] public float arrowSpawnForwardOffset = 0.53f;
    [Tooltip("Vertical adjustment from the center of the player sprite. Negative values spawn lower; positive values spawn higher.")]
    public float arrowSpawnHeightOffset = 0f;
    [Min(0.1f)] public float projectileColliderWidth = 0.98f;
    [Min(0.05f)] public float projectileColliderHeight = 0.34f;
    [Tooltip("Physics circle-cast radius used for projectile hit detection, in world units. The runtime uses at least 0.22 for reliable hits.")]
    [Min(0.02f)] public float projectileHitRadius = 0.22f;
    [Range(0.5f, 3f)] public float projectileVisualScale = 1.0f;
    [Min(1f)] public float projectileSpeed = 13.5f;

    [Header("Platform collision and level editor defaults")]
    [Tooltip("Depth of platform collision below its top surface. Visible platform art remains one tile row.")]
    [Min(0.25f)] public float platformColliderDepth = 1.06f;
    [Tooltip("Total additional collider width beyond the platform width, shared across both ends.")]
    [Range(0f, 0.5f)] public float platformColliderExtraWidth = 0.12f;
    [Tooltip("Default width used when placing a large ground platform in the Workbench.")]
    [Min(2f)] public float groundPlatformDefaultWidth = 8f;
    [Tooltip("Default width used when placing a small floating/sky platform in the Workbench.")]
    [Min(1f)] public float skyPlatformDefaultWidth = 2.5f;
    [Tooltip("Suggested vertical rise when using Add Sky Step in the Workbench.")]
    [Range(0.75f, 4.5f)] public float recommendedSkyPlatformRise = 2.2f;
    [Tooltip("Suggested horizontal gap when using Add Sky Step in the Workbench.")]
    [Range(0f, 6f)] public float recommendedSkyPlatformGap = 1.25f;

    [Header("Combat")]
    [Min(1)] public int swordDamage = 2;
    [Min(1)] public int arrowDamage = 1;
    [Min(0.1f)] public float bowCooldown = 0.68f;
    [Min(0.1f)] public float swordCooldown = 0.38f;
    [Min(1)] public int finalBossHealth = 24;
    [Min(1)] public float finalBossScale = 1.38f;

    private static GameTuning cached;
    public static GameTuning Current
    {
        get
        {
            if (cached == null) cached = Resources.Load<GameTuning>("GameTuning");
            return cached;
        }
    }

    public static float TimeLimit(bool hard)
    {
        GameTuning tuning = Current;
        return tuning == null ? (hard ? 120f : 180f) : (hard ? tuning.hardTimeLimitSeconds : tuning.normalTimeLimitSeconds);
    }

    public static int CoinHeartThreshold
    {
        get { return Current == null ? 2 : Mathf.Max(1, Current.coinsPerHeart); }
    }
}
