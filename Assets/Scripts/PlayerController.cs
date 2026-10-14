using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
public sealed class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5.8f;
    public float runSpeed = 9.0f;
    public float jumpVelocity = 14.5f;
    public float doubleJumpVelocity = 12.8f;
    public float gravityScale = 3.0f;
    [Range(0.15f, 0.9f)] public float jumpReleaseMultiplier = 0.48f;
    public float wallJumpHorizontalSpeed = 10.8f;
    public float wallSlideMaxFallSpeed = 2.5f;

    [Header("Combat")]
    public int maxHealth = 5;
    public int health = 5;
    public int swordDamage = 2;
    public int arrowDamage = 1;
    public float bowCooldown = 0.58f;
    public float swordCooldown = 0.38f;

    public Rigidbody2D Body { get; private set; }
    public int Health => health;
    public int MaxHealth => maxHealth;
    public int Facing => facing;
    public bool IsGrounded => grounded;

    private SpriteRenderer spriteRenderer;
    private BoxCollider2D boxCollider;
    private readonly int groundMask = 1 << 8;
    private int facing = 1;
    private float horizontalInput;
    private bool running;
    private bool grounded;
    private bool slippery;
    private bool wallTouching;
    private bool wallSliding;
    private bool jumpHoldActive;
    private bool jumpCutApplied;
    private int airJumpsRemaining = 0;
    private float coyoteTimer;
    private float jumpBufferTimer;
    private float bowReadyAt;
    private float swordReadyAt;
    private float swordAnimationTimer;
    private float bowAnimationTimer;
    private float invulnerableUntil;
    private Vector3 checkpointPosition;
    private static PhysicsMaterial2D noFrictionMaterial;
    private float animationClock;
    private float footstepTimer;
    private string groundBiome = "meadow";
    private string currentAnimation = "idle";
    private int currentFrame = -1;
    private bool dead;

    private void Awake()
    {
        Body = GetComponent<Rigidbody2D>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        boxCollider = GetComponent<BoxCollider2D>();
        Body.gravityScale = gravityScale;
        Body.freezeRotation = true;
        Body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        Body.interpolation = RigidbodyInterpolation2D.Interpolate;
        ApplySizeTuning(GameTuning.Current);
        if (noFrictionMaterial == null)
            noFrictionMaterial = new PhysicsMaterial2D("Wildrune Player - No Wall Stick") { friction = 0f, bounciness = 0f };
        boxCollider.sharedMaterial = noFrictionMaterial;
        checkpointPosition = transform.position;
        health = Mathf.Clamp(health, 1, maxHealth);
    }

    private void Start()
    {
        spriteRenderer.sprite = GameArt.Get("player_idle_0");
    }

    public void ApplyTuning(GameTuning tuning)
    {
        if (tuning == null) return;
        walkSpeed = tuning.walkSpeed;
        runSpeed = tuning.runSpeed;
        jumpVelocity = tuning.jumpVelocity;
        doubleJumpVelocity = tuning.doubleJumpVelocity;
        gravityScale = tuning.gravityScale;
        jumpReleaseMultiplier = tuning.jumpReleaseMultiplier;
        wallJumpHorizontalSpeed = tuning.wallJumpHorizontalSpeed;
        wallSlideMaxFallSpeed = tuning.wallSlideMaxFallSpeed;
        swordDamage = tuning.swordDamage;
        arrowDamage = tuning.arrowDamage;
        bowCooldown = tuning.bowCooldown;
        swordCooldown = tuning.swordCooldown;
        ApplySizeTuning(tuning);
        if (Body != null) Body.gravityScale = gravityScale;
    }

    private void ApplySizeTuning(GameTuning tuning)
    {
        if (boxCollider == null) boxCollider = GetComponent<BoxCollider2D>();
        if (tuning == null || boxCollider == null) return;
        float width = Mathf.Max(0.2f, tuning.playerColliderWidth);
        float height = Mathf.Max(0.3f, tuning.playerColliderHeight);
        boxCollider.size = new Vector2(width, height);
        // The collider's lower edge stays at the transform position (the level spawn is feet-based).
        boxCollider.offset = new Vector2(0f, height * 0.5f);
        float scale = Mathf.Clamp(tuning.playerVisualScale, 0.5f, 2.5f);
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void Update()
    {
        if (dead || GameBootstrap.Instance == null || GameBootstrap.Instance.IsPaused) return;

        horizontalInput = GameControls.ReadHorizontal();
        running = GameControls.IsHeld(GameControls.Action.Run);
        if (Mathf.Abs(horizontalInput) > 0.01f)
        {
            facing = horizontalInput > 0f ? 1 : -1;
        }

        RefreshGrounding();
        UpdateFootsteps();
        HandleJumpInput();
        HandleVariableJumpHeight();
        HandleCombatInput();
        HandleWallSlide();
        UpdateTimers();
        Animate();

        if (transform.position.y < -11f)
        {
            FallBackToCheckpoint();
        }
    }

    private void FixedUpdate()
    {
        if (dead || GameBootstrap.Instance == null || GameBootstrap.Instance.IsPaused) return;

        Vector2 velocity = Body.linearVelocity;
        Body.gravityScale = gravityScale;
        GameTuning tuning = GameTuning.Current;
        float groundAccel = tuning != null ? tuning.groundAcceleration : 38f;
        float groundDecel = tuning != null ? tuning.groundDeceleration : 48f;
        float airAccel = tuning != null ? tuning.airAcceleration : 18f;
        float airDecel = tuning != null ? tuning.airDeceleration : 5f;
        float iceMultiplier = tuning != null ? tuning.iceSpeedMultiplier : 1.65f;
        float iceAccel = tuning != null ? tuning.iceAcceleration : 36f;
        float iceDecel = tuning != null ? tuning.iceDeceleration : 0.25f;
        float iceTurnAccel = tuning != null ? tuning.iceTurnAcceleration : 12f;

        float topSpeed = running ? runSpeed : walkSpeed;
        float acceleration;
        float deceleration;
        if (grounded && slippery)
        {
            // Ice is quicker than normal ground and retains momentum for a long time.
            topSpeed *= iceMultiplier;
            acceleration = iceAccel;
            deceleration = iceDecel;
        }
        else if (grounded)
        {
            acceleration = groundAccel;
            deceleration = groundDecel;
        }
        else
        {
            acceleration = airAccel;
            deceleration = airDecel;
        }

        float target = horizontalInput * topSpeed;
        float rate = Mathf.Abs(horizontalInput) > 0.01f ? acceleration : deceleration;
        // Ice keeps high top speed and momentum. Reversing against existing momentum takes
        // longer than accelerating in the current direction, so players must plan their turns.
        if (grounded && slippery && Mathf.Abs(horizontalInput) > 0.01f &&
            Mathf.Abs(velocity.x) > 0.5f && Mathf.Sign(velocity.x) != Mathf.Sign(horizontalInput))
            rate = iceTurnAccel;
        velocity.x = Mathf.MoveTowards(velocity.x, target, rate * Time.fixedDeltaTime);

        if (wallSliding && velocity.y < -wallSlideMaxFallSpeed) velocity.y = -wallSlideMaxFallSpeed;
        Body.linearVelocity = velocity;
    }

    private void RefreshGrounding()
    {
        Bounds bounds = boxCollider.bounds;
        Vector2 feetOrigin = new Vector2(bounds.center.x, bounds.min.y + 0.045f);
        Vector2 feetSize = new Vector2(Mathf.Max(0.16f, bounds.size.x * 0.82f), 0.08f);
        bool wasGrounded = grounded;
        RaycastHit2D groundHit = Physics2D.BoxCast(feetOrigin, feetSize, 0f, Vector2.down, 0.13f, groundMask);
        grounded = groundHit.collider != null && Body.linearVelocity.y <= 0.45f;
        PlatformSurface surface = grounded && groundHit.collider != null ? groundHit.collider.GetComponent<PlatformSurface>() : null;
        slippery = surface != null && surface.slippery;
        groundBiome = surface != null ? surface.biome : "meadow";
        if (grounded && !wasGrounded && Body.linearVelocity.y < -1.5f)
            WildruneAudioManager.Instance?.Play(WildruneSfx.Landing, 0.7f);

        if (grounded)
        {
            coyoteTimer = 0.12f;
            airJumpsRemaining = 1;
            jumpHoldActive = false;
            jumpCutApplied = false;
        }
        else
        {
            coyoteTimer = Mathf.Max(0f, coyoteTimer - Time.deltaTime);
        }

        // Probe immediately outside the actual collider sides. Starting these casts at the
        // player centre misses nearby walls entirely and makes wall-slide/jump feel inconsistent.
        Vector2 sideSize = new Vector2(0.05f, Mathf.Max(0.36f, bounds.size.y * 0.76f));
        Vector2 leftOrigin = new Vector2(bounds.min.x + 0.075f, bounds.center.y);
        Vector2 rightOrigin = new Vector2(bounds.max.x - 0.075f, bounds.center.y);
        RaycastHit2D left = Physics2D.BoxCast(leftOrigin, sideSize, 0f, Vector2.left, 0.19f, groundMask);
        RaycastHit2D right = Physics2D.BoxCast(rightOrigin, sideSize, 0f, Vector2.right, 0.19f, groundMask);
        wallTouching = (left.collider != null && horizontalInput < -0.05f) ||
                       (right.collider != null && horizontalInput > 0.05f);
    }

    private void HandleJumpInput()
    {
        bool jumpPressed = GameControls.WasPressed(GameControls.Action.Jump);
        if (jumpPressed) jumpBufferTimer = 0.14f;
        if (jumpBufferTimer <= 0f) return;

        Vector2 velocity = Body.linearVelocity;
        if (grounded || coyoteTimer > 0f)
        {
            velocity.y = jumpVelocity;
            Body.linearVelocity = velocity;
            grounded = false;
            // No free double jump: an air jump is earned only by a successful wall jump.
            airJumpsRemaining = 0;
            coyoteTimer = 0f;
            jumpBufferTimer = 0f;
            jumpHoldActive = true;
            jumpCutApplied = false;
            WildruneAudioManager.Instance?.Play(WildruneSfx.Jump);
        }
        else if (wallTouching)
        {
            // A successful wall jump refreshes one earned air jump. Repeated wall jumps
            // refresh it again, enabling wall-jump chains without a free double jump.
            int wallDirection = horizontalInput < 0f ? -1 : 1;
            velocity.x = -wallDirection * wallJumpHorizontalSpeed;
            velocity.y = jumpVelocity * 0.92f;
            Body.linearVelocity = velocity;
            facing = -wallDirection;
            jumpBufferTimer = 0f;
            airJumpsRemaining = 1;
            wallTouching = false;
            jumpHoldActive = true;
            jumpCutApplied = false;
            WildruneAudioManager.Instance?.Play(WildruneSfx.WallJump);
        }
        else if (airJumpsRemaining > 0)
        {
            velocity.y = doubleJumpVelocity;
            Body.linearVelocity = velocity;
            airJumpsRemaining--;
            jumpBufferTimer = 0f;
            jumpHoldActive = true;
            jumpCutApplied = false;
            WildruneAudioManager.Instance?.Play(WildruneSfx.DoubleJump);
        }
    }

    private void HandleVariableJumpHeight()
    {
        // Releasing jump early cuts upward velocity. Holding jump retains a higher arc.
        if (!jumpHoldActive || jumpCutApplied || GameControls.IsHeld(GameControls.Action.Jump)) return;
        if (Body.linearVelocity.y > 0.1f)
        {
            Vector2 velocity = Body.linearVelocity;
            velocity.y *= jumpReleaseMultiplier;
            Body.linearVelocity = velocity;
        }
        jumpCutApplied = true;
        jumpHoldActive = false;
    }

    private void HandleCombatInput()
    {
        // Mouse combat remains available in the keyboard scheme. Touch mode routes attacks exclusively
        // through the mobile buttons so tapping UI controls cannot double-trigger an attack.
        bool allowMouseCombat = !GameControls.TouchscreenMode;
        bool swordPressed = GameControls.WasPressed(GameControls.Action.Sword) ||
                            (allowMouseCombat && Input.GetMouseButtonDown(0));
        bool bowPressed = GameControls.WasPressed(GameControls.Action.Bow) ||
                          (allowMouseCombat && Input.GetMouseButtonDown(1));

        if (swordPressed && Time.time >= swordReadyAt)
        {
            swordReadyAt = Time.time + swordCooldown;
            swordAnimationTimer = 0.34f;
            WildruneAudioManager.Instance?.Play(WildruneSfx.SwordSwing);
            PerformSwordSweep();
        }
        if (bowPressed && Time.time >= bowReadyAt)
        {
            bowReadyAt = Time.time + bowCooldown;
            bowAnimationTimer = 0.24f;
            WildruneAudioManager.Instance?.Play(WildruneSfx.BowShot);
            FireArrow();
        }
    }

    private void PerformSwordSweep()
    {
        Vector2 center = (Vector2)transform.position + new Vector2(facing * 0.78f, 0.58f);
        Collider2D[] hits = Physics2D.OverlapCircleAll(center, 0.82f);
        for (int i = 0; i < hits.Length; i++)
        {
            EnemyController enemy = hits[i].GetComponent<EnemyController>();
            if (enemy != null) enemy.TakeDamage(swordDamage, new Vector2(facing, 0.15f));
        }
    }

    private void FireArrow()
    {
        GameTuning tuning = GameTuning.Current;
        float forwardOffset = tuning != null && tuning.arrowSpawnForwardOffset > 0.05f
            ? tuning.arrowSpawnForwardOffset
            : 0.53f;
        float heightOffset = tuning != null ? tuning.arrowSpawnHeightOffset : 0f;
        // GameArt character sprites use a bottom pivot. Use the actual rendered sprite bounds
        // so arrows leave from the visual midpoint even when the player is scaled or resized.
        Vector2 spriteCenter = spriteRenderer != null
            ? (Vector2)spriteRenderer.bounds.center
            : (Vector2)transform.position + new Vector2(0f, 0.58f);
        Vector2 origin = spriteCenter + new Vector2(facing * forwardOffset, heightOffset);
        GameObject arrow = new GameObject("Hero Arrow");
        arrow.layer = LayerMask.NameToLayer("Projectile");
        arrow.transform.position = origin;
        SpriteRenderer sr = arrow.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("straight-arrow-modified") != null ? GameArt.Get("straight-arrow-modified") : GameArt.Get("projectile_arrow");
        sr.sortingOrder = 14;
        Rigidbody2D rb = arrow.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        BoxCollider2D col = arrow.AddComponent<BoxCollider2D>();
        col.size = tuning == null
            ? new Vector2(0.98f, 0.34f)
            : new Vector2(tuning.projectileColliderWidth, tuning.projectileColliderHeight);
        Projectile projectile = arrow.AddComponent<Projectile>();
        float arrowSpeed = tuning != null ? tuning.projectileSpeed : 13.5f;
        // Keep the shot level; even a tiny upward angle causes long-range arrows to rise above small enemies.
        projectile.Launch(new Vector2(facing, 0f), true, arrowDamage, arrowSpeed);
    }

    private void HandleWallSlide()
    {
        wallSliding = !grounded && wallTouching && Body.linearVelocity.y < -0.2f;
    }

    private void UpdateFootsteps()
    {
        if (!grounded || Mathf.Abs(Body.linearVelocity.x) < 1.35f || GameBootstrap.Instance == null || GameBootstrap.Instance.IsPaused)
        {
            footstepTimer = 0.04f;
            return;
        }
        footstepTimer -= Time.deltaTime;
        if (footstepTimer > 0f) return;
        bool onStone = groundBiome == "ember" || groundBiome == "stone" || groundBiome == "ruins";
        WildruneAudioManager.Instance?.PlayFootstep(slippery, onStone);
        float speedFraction = Mathf.Clamp01(Mathf.Abs(Body.linearVelocity.x) / Mathf.Max(1f, runSpeed));
        footstepTimer = Mathf.Lerp(0.42f, 0.25f, speedFraction);
    }

    private void UpdateTimers()
    {
        if (jumpBufferTimer > 0f) jumpBufferTimer -= Time.deltaTime;
        if (swordAnimationTimer > 0f) swordAnimationTimer -= Time.deltaTime;
        if (bowAnimationTimer > 0f) bowAnimationTimer -= Time.deltaTime;
    }

    private void Animate()
    {
        spriteRenderer.flipX = facing < 0;
        if (Time.time < invulnerableUntil)
        {
            Color blink = spriteRenderer.color;
            blink.a = (Mathf.Sin(Time.time * 34f) > 0f) ? 0.42f : 1f;
            spriteRenderer.color = blink;
        }
        else
        {
            spriteRenderer.color = Color.white;
        }

        string anim;
        int frame;
        if (swordAnimationTimer > 0f)
        {
            anim = "sword"; frame = Mathf.Clamp(Mathf.FloorToInt((0.34f - swordAnimationTimer) / 0.34f * 4f), 0, 3);
        }
        else if (bowAnimationTimer > 0f)
        {
            anim = "bow"; frame = Mathf.Clamp(Mathf.FloorToInt((0.24f - bowAnimationTimer) / 0.24f * 3f), 0, 2);
        }
        else if (!grounded)
        {
            anim = "jump"; frame = Body.linearVelocity.y > 0.1f ? 0 : 1;
        }
        else if (Mathf.Abs(Body.linearVelocity.x) > runSpeed * 0.78f)
        {
            anim = "run"; frame = Mathf.FloorToInt(Time.time * 13f) % 4;
        }
        else if (Mathf.Abs(Body.linearVelocity.x) > 0.4f)
        {
            anim = "walk"; frame = Mathf.FloorToInt(Time.time * 8f) % 4;
        }
        else
        {
            anim = "idle"; frame = Mathf.FloorToInt(Time.time * 2.4f) % 2;
        }
        if (anim != currentAnimation || frame != currentFrame)
        {
            currentAnimation = anim;
            currentFrame = frame;
            Sprite next = GameArt.Get("player_" + anim + "_" + frame);
            if (next != null) spriteRenderer.sprite = next;
        }
    }

    public void TakeDamage(int amount, Vector2 sourcePosition)
    {
        if (dead || amount <= 0 || Time.time < invulnerableUntil) return;
        health = Mathf.Max(0, health - amount);
        WildruneAudioManager.Instance?.Play(WildruneSfx.PlayerHurt);
        invulnerableUntil = Time.time + 0.95f;
        float push = transform.position.x >= sourcePosition.x ? 1f : -1f;
        Body.linearVelocity = new Vector2(push * 5.4f, 6.2f);
        bool savedByCoins = GameBootstrap.Instance != null && GameBootstrap.Instance.TryRedeemCoinHeart(this);
        GameBootstrap.Instance?.RefreshHud();
        if (health <= 0 && !savedByCoins)
        {
            dead = true;
            GameBootstrap.Instance?.PlayerDied();
        }
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        int previousHealth = health;
        health = Mathf.Min(maxHealth, health + amount);
        if (health > previousHealth) WildruneAudioManager.Instance?.Play(WildruneSfx.Heal);
        GameBootstrap.Instance?.RefreshHud();
    }

    public void SetCheckpoint(Vector3 position)
    {
        checkpointPosition = position;
        GameBootstrap.Instance?.ShowToast("CHECKPOINT REACHED");
    }

    private void FallBackToCheckpoint()
    {
        health = Mathf.Max(0, health - 1);
        bool savedByCoins = GameBootstrap.Instance != null && GameBootstrap.Instance.TryRedeemCoinHeart(this);
        if (health <= 0 && !savedByCoins)
        {
            dead = true;
            GameBootstrap.Instance?.PlayerDied();
            return;
        }
        transform.position = checkpointPosition;
        Body.linearVelocity = Vector2.zero;
        invulnerableUntil = Time.time + 1.0f;
        GameBootstrap.Instance?.RefreshHud();
    }
}
