using System;
using Random = UnityEngine.Random;
using UnityEngine;

public enum EnemyKind
{
    Slime,
    Melee,
    Archer,
    Bat,
    Brute,
    Boss
}

[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D), typeof(SpriteRenderer))]
public sealed class EnemyController : MonoBehaviour
{
    public EnemyKind kind = EnemyKind.Melee;
    public int health = 3;
    public float moveSpeed = 2.0f;
    public int touchDamage = 1;

    private Rigidbody2D body;
    private BoxCollider2D hitbox;
    private SpriteRenderer art;
    private PlayerController player;
    private EnemyTypeDefaults profile;
    private EnemyBehaviourStyle behaviour = EnemyBehaviourStyle.MeleeChaser;
    private float patrolMin;
    private float patrolMax;
    private float homeY;
    private float direction = -1f;
    private float facingDirection = -1f;
    private float nextProjectileAttackAt;
    private float nextContactAttackAt;
    private float nextHopAt;
    private float hurtTintUntil;
    private int scoreValue = 100;
    private bool dying;
    private int animFrame;
    private float animClock;
    private readonly int groundMask = 1 << 8;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        hitbox = GetComponent<BoxCollider2D>();
        art = GetComponent<SpriteRenderer>();
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        body.gravityScale = 3.2f;
        hitbox.size = new Vector2(0.68f, 0.80f);
        hitbox.offset = new Vector2(0f, 0.40f);
    }

    public void Configure(EnemyKind enemyKind, float minX, float maxX)
    {
        kind = enemyKind;
        profile = EnemyDefaults.GetProfile(kind);
        patrolMin = Mathf.Min(minX, maxX);
        patrolMax = Mathf.Max(minX, maxX);
        homeY = transform.position.y;

        if (profile != null)
        {
            health = Mathf.Max(1, profile.health);
            moveSpeed = Mathf.Max(0.1f, profile.moveSpeed);
            touchDamage = Mathf.Max(0, profile.touchDamage);
            scoreValue = Mathf.Max(0, profile.scoreValue);
            behaviour = profile.behaviour;
            body.gravityScale = Mathf.Max(0f, profile.gravityScale);
            hitbox.size = new Vector2(Mathf.Max(0.1f, profile.colliderSize.x), Mathf.Max(0.1f, profile.colliderSize.y));
            hitbox.offset = profile.colliderOffset;
            float bossScale = kind == EnemyKind.Boss && GameTuning.Current != null ? GameTuning.Current.finalBossScale : 1f;
            transform.localScale = Vector3.one * Mathf.Max(0.1f, profile.visualScale) * bossScale;
        }
        else
        {
            ApplyLegacyFallback(kind);
        }

        // GameTuning remains a fallback for old projects without EnemyDefaults.asset.
        if (profile == null && kind == EnemyKind.Boss && GameTuning.Current != null)
            health = GameTuning.Current.finalBossHealth;
        if (art != null)
        {
            Sprite sprite = GetFrame(0);
            if (sprite != null) art.sprite = sprite;
            art.color = profile != null ? profile.tint : Color.white;
        }
    }

    private void ApplyLegacyFallback(EnemyKind enemyKind)
    {
        switch (enemyKind)
        {
            case EnemyKind.Slime: health = 2; moveSpeed = 1.7f; touchDamage = 1; scoreValue = 75; behaviour = EnemyBehaviourStyle.Hopper; break;
            case EnemyKind.Melee: health = 3; moveSpeed = 2.1f; touchDamage = 1; scoreValue = 100; behaviour = EnemyBehaviourStyle.MeleeChaser; break;
            case EnemyKind.Archer: health = 2; moveSpeed = 1.0f; touchDamage = 1; scoreValue = 125; behaviour = EnemyBehaviourStyle.RangedKiter; break;
            case EnemyKind.Bat: health = 1; moveSpeed = 2.5f; touchDamage = 1; scoreValue = 100; behaviour = EnemyBehaviourStyle.FlyingHunter; break;
            case EnemyKind.Brute: health = 5; moveSpeed = 1.25f; touchDamage = 2; scoreValue = 200; behaviour = EnemyBehaviourStyle.MeleeChaser; break;
            case EnemyKind.Boss: health = 24; moveSpeed = 1.25f; touchDamage = 2; scoreValue = 1500; behaviour = EnemyBehaviourStyle.Boss; break;
        }
        if (enemyKind == EnemyKind.Bat)
        {
            body.gravityScale = 0f;
            hitbox.size = new Vector2(0.72f, 0.58f);
            hitbox.offset = new Vector2(0f, 0.48f);
        }
        else if (enemyKind == EnemyKind.Brute || enemyKind == EnemyKind.Boss)
        {
            body.gravityScale = 3.4f;
            hitbox.size = new Vector2(0.82f, 1.12f);
            hitbox.offset = new Vector2(0f, 0.56f);
        }
        if (enemyKind == EnemyKind.Boss)
        {
            float bossScale = GameTuning.Current != null ? GameTuning.Current.finalBossScale : 1.38f;
            transform.localScale = Vector3.one * bossScale;
            hitbox.size = new Vector2(1.18f, 1.42f);
            hitbox.offset = new Vector2(0f, 0.68f);
        }
    }

    private void Start()
    {
        player = GameBootstrap.Instance != null ? GameBootstrap.Instance.CurrentPlayer : null;
        homeY = transform.position.y;
        if (patrolMax <= patrolMin) { patrolMin = transform.position.x - 2.0f; patrolMax = transform.position.x + 2.0f; }
        Sprite first = GetFrame(0);
        if (first != null) art.sprite = first;
        art.sortingOrder = 5;
        art.color = profile != null ? profile.tint : Color.white;
        float firstHopMin = profile != null ? profile.firstHopDelayMin : 0.5f;
        float firstHopMax = profile != null ? profile.firstHopDelayMax : 1.4f;
        nextHopAt = Time.time + Random.Range(Mathf.Min(firstHopMin, firstHopMax), Mathf.Max(firstHopMin, firstHopMax));
    }

    private void Update()
    {
        if (dying || GameBootstrap.Instance == null || GameBootstrap.Instance.IsPaused) return;
        if (player == null) player = GameBootstrap.Instance.CurrentPlayer;

        animClock += Time.deltaTime;
        float frameInterval = profile != null ? profile.animationFrameInterval : 0.31f;
        if (animClock > Mathf.Max(0.05f, frameInterval))
        {
            animClock = 0f;
            animFrame = (animFrame + 1) % 2;
            Sprite next = GetFrame(animFrame);
            if (next != null) art.sprite = next;
        }
        art.color = Time.time < hurtTintUntil ? new Color(1f, 0.55f, 0.47f, 1f) : (profile != null ? profile.tint : Color.white);

        float dx = player != null ? player.transform.position.x - transform.position.x : 0f;
        float dy = player != null ? player.transform.position.y - transform.position.y : 0f;
        float distanceX = Mathf.Abs(dx);

        // Only the melee goblin, skeleton/brute, and slime are strictly patrol-only.
        // Other archetypes retain their distinct AI: archers kite and fire, fliers chase,
        // and the boss pursues/faces the player while respecting its assigned X bounds.
        if (kind == EnemyKind.Melee || kind == EnemyKind.Brute || kind == EnemyKind.Slime)
        {
            WalkerBehaviour(dx, dy, distanceX);
        }
        else
        {
            switch (behaviour)
            {
                case EnemyBehaviourStyle.RangedKiter: RangedBehaviour(dx, dy, distanceX); break;
                case EnemyBehaviourStyle.FlyingHunter: FlyingBehaviour(dx, dy, distanceX); break;
                case EnemyBehaviourStyle.Boss: BossBehaviour(dx, dy, distanceX); break;
                default: WalkerBehaviour(dx, dy, distanceX); break;
            }
        }

        // Facing is independent from movement for target-aware enemies. A short dead zone
        // prevents direction flicker if the player is exactly aligned horizontally.
        if (player != null && (behaviour == EnemyBehaviourStyle.RangedKiter || behaviour == EnemyBehaviourStyle.Boss))
        {
            if (Mathf.Abs(dx) > 0.05f) facingDirection = Mathf.Sign(dx);
            art.flipX = facingDirection < 0f;
        }
        else
        {
            art.flipX = direction < 0f;
        }

        float contactRange = profile != null ? profile.contactRange : (kind == EnemyKind.Brute || kind == EnemyKind.Boss ? 1.15f : 0.78f);
        float contactHeight = profile != null ? profile.contactVerticalRange : 1.1f;
        float contactCooldown = profile != null ? profile.contactCooldown : (kind == EnemyKind.Boss ? 0.85f : 1.05f);
        if (player != null && distanceX < contactRange && Mathf.Abs(dy) < contactHeight && Time.time >= nextContactAttackAt)
        {
            nextContactAttackAt = Time.time + Mathf.Max(0.1f, contactCooldown);
            player.TakeDamage(touchDamage, transform.position);
        }
    }

    private void FixedUpdate()
    {
        if (dying || GameBootstrap.Instance == null || GameBootstrap.Instance.IsPaused) return;
        if (behaviour == EnemyBehaviourStyle.FlyingHunter) return;
        if (behaviour != EnemyBehaviourStyle.Hopper || Time.time < nextHopAt || !IsStandingOnGround()) return;

        float hopVelocity = profile != null ? profile.hopVelocity : 6f;
        float delayMin = profile != null ? profile.hopIntervalMin : 1.1f;
        float delayMax = profile != null ? profile.hopIntervalMax : 1.8f;
        body.linearVelocity = new Vector2(body.linearVelocity.x, hopVelocity);
        nextHopAt = Time.time + Random.Range(Mathf.Min(delayMin, delayMax), Mathf.Max(delayMin, delayMax));
    }

    // The melee goblin, skeleton/brute, and slime patrol their exact assigned X bounds.
    // Player position never redirects these archetypes, preventing rapid flip-flopping.
    private void WalkerBehaviour(float dx, float dy, float distanceX)
    {
        PatrolGroundMovement();
    }

    // The boss retains the original target-aware movement and volley attack. Its movement
    // still respects its assigned arena bounds; sprite-facing is handled separately from motion.
    private void BossBehaviour(float dx, float dy, float distanceX)
    {
        float wanted = direction;
        bool atLeftLimit = body.position.x <= patrolMin;
        bool atRightLimit = body.position.x >= patrolMax;
        float detectionRange = profile != null ? profile.detectionRange : 13f;
        float detectionHeight = profile != null ? profile.detectionVerticalRange : 3.2f;
        float stopDistance = profile != null ? profile.chaseStopDistance : 1.2f;

        if (atLeftLimit) wanted = 1f;
        else if (atRightLimit) wanted = -1f;
        else if (player != null && distanceX < detectionRange && Mathf.Abs(dy) < detectionHeight)
        {
            // Preserve the boss's original close-in movement; the separate facing direction
            // below ensures that visual facing remains player-directed even at an arena edge.
            if (Mathf.Abs(dx) > 0.001f) wanted = Mathf.Sign(dx);
            float volleyRange = GetProfileFloat(p => p.bossVolleyRange, 4.5f);
            if (distanceX < volleyRange && Time.time >= nextProjectileAttackAt)
            {
                FireBossVolley();
                nextProjectileAttackAt = Time.time + GetProfileFloat(p => p.bossVolleyCooldown, 2.1f);
            }
        }

        direction = Mathf.Abs(wanted) < 0.1f ? direction : Mathf.Sign(wanted);
        float speedFactor = 1f;
        float closeDistance = profile != null ? profile.chaseStopDistance : stopDistance;
        float closeSpeed = profile != null ? profile.closeRangeSpeedMultiplier : 0.35f;
        if (player != null && distanceX < closeDistance && Mathf.Abs(dy) < 1.1f)
            speedFactor = closeSpeed;

        float x = body.position.x;
        if (x < patrolMin || x > patrolMax)
            body.position = new Vector2(Mathf.Clamp(x, patrolMin, patrolMax), body.position.y);
        body.linearVelocity = new Vector2(direction * moveSpeed * speedFactor, body.linearVelocity.y);
    }

    private void PatrolGroundMovement()
    {
        // Turn slightly before the physics step could carry the collider across an endpoint.
        // The direction check means each endpoint causes one turn, not frame-by-frame flipping.
        float x = body.position.x;
        float turnBuffer = Mathf.Max(0.02f, moveSpeed * Time.fixedDeltaTime);

        if (direction < 0f && x <= patrolMin + turnBuffer)
            direction = 1f;
        else if (direction > 0f && x >= patrolMax - turnBuffer)
            direction = -1f;

        // Correct any tiny physics overshoot so the patrol remains inside the assigned bounds.
        if (x < patrolMin || x > patrolMax)
            body.position = new Vector2(Mathf.Clamp(x, patrolMin, patrolMax), body.position.y);

        body.linearVelocity = new Vector2(direction * moveSpeed, body.linearVelocity.y);
    }

    private void RangedBehaviour(float dx, float dy, float distanceX)
    {
        if (player == null) return;

        // Preserve the archer's original kite behavior: retreat when crowded, close distance
        // when too far away, and otherwise hold ground. Facing remains player-directed.
        if (Mathf.Abs(dx) > 0.05f) direction = Mathf.Sign(dx);
        float retreatDistance = GetProfileFloat(p => p.rangedRetreatDistance, 4.5f);
        float preferredDistance = GetProfileFloat(p => p.rangedPreferredDistance, 8.5f);
        float approachLimit = GetProfileFloat(p => p.rangedApproachLimit, 13f);
        if (distanceX < retreatDistance)
            body.linearVelocity = new Vector2(-direction * moveSpeed, body.linearVelocity.y);
        else if (distanceX > preferredDistance && distanceX < approachLimit)
            body.linearVelocity = new Vector2(direction * moveSpeed * 0.65f, body.linearVelocity.y);
        else
            body.linearVelocity = new Vector2(Mathf.MoveTowards(body.linearVelocity.x, 0f, 7f * Time.deltaTime), body.linearVelocity.y);

        // Keep the archer inside its designer-authored patrol bounds without changing its aim.
        float x = body.position.x;
        if (x <= patrolMin && body.linearVelocity.x < 0f)
            body.linearVelocity = new Vector2(moveSpeed, body.linearVelocity.y);
        else if (x >= patrolMax && body.linearVelocity.x > 0f)
            body.linearVelocity = new Vector2(-moveSpeed, body.linearVelocity.y);
        if (x < patrolMin || x > patrolMax)
            body.position = new Vector2(Mathf.Clamp(x, patrolMin, patrolMax), body.position.y);

        // Attack whenever player is within the configured range/height and the normal cooldown is ready.
        if (distanceX < GetProfileFloat(p => p.rangedAttackRange, 12f) &&
            Mathf.Abs(dy) < GetProfileFloat(p => p.rangedVerticalRange, 4f) && Time.time >= nextProjectileAttackAt)
        {
            FireAtPlayer(0f, GetProfileFloat(p => p.projectileSpeed, 9.3f), GetProfileInt(p => p.projectileDamage, 1));
            float min = GetProfileFloat(p => p.rangedCooldownMin, 1.55f);
            float max = GetProfileFloat(p => p.rangedCooldownMax, 2.2f);
            nextProjectileAttackAt = Time.time + Random.Range(Mathf.Min(min, max), Mathf.Max(min, max));
        }
    }

    private void FlyingBehaviour(float dx, float dy, float distanceX)
    {
        // Fliers retain their original hunter behavior: chase the player in X/Y inside detection
        // range, then return to their hovering patrol route when the player gets far enough away.
        float chaseRange = GetProfileFloat(p => p.flyingChaseRange, 7f);
        float offsetY = GetProfileFloat(p => p.flyingTargetYOffset, 0.7f);
        float hoverAmplitude = GetProfileFloat(p => p.flyingHoverAmplitude, 0.55f);
        float hoverFrequency = GetProfileFloat(p => p.flyingHoverFrequency, 2.8f);
        if (player != null && distanceX < chaseRange)
        {
            Vector2 target = player.transform.position + Vector3.up * offsetY;
            Vector2 next = Vector2.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);
            transform.position = new Vector3(next.x, next.y, transform.position.z);
            if (Mathf.Abs(dx) > 0.05f) direction = dx > 0f ? 1f : -1f;
        }
        else
        {
            float nextX = transform.position.x + direction * moveSpeed * Time.deltaTime;
            if (nextX < patrolMin) { nextX = patrolMin; direction = 1f; }
            if (nextX > patrolMax) { nextX = patrolMax; direction = -1f; }
            float nextY = homeY + Mathf.Sin(Time.time * hoverFrequency + transform.position.x) * hoverAmplitude;
            transform.position = new Vector3(nextX, nextY, transform.position.z);
        }
    }

    private void FireBossVolley()
    {
        int count = Mathf.Max(1, GetProfileInt(p => p.bossVolleyCount, 3));
        float spread = GetProfileFloat(p => p.bossVolleySpreadDegrees, 9f) * Mathf.Deg2Rad;
        float speed = GetProfileFloat(p => p.bossProjectileSpeed, 10.5f);
        int damage = GetProfileInt(p => p.bossProjectileDamage, 1);
        for (int i = 0; i < count; i++)
        {
            float centeredIndex = i - (count - 1) * 0.5f;
            FireAtPlayer(centeredIndex * spread, speed, damage);
        }
    }

    private bool IsStandingOnGround()
    {
        if (body.linearVelocity.y > 0.15f) return false;
        return Physics2D.OverlapCircle(transform.position + Vector3.up * 0.03f, 0.12f, groundMask) != null;
    }

    private void FireAtPlayer(float angleOffset, float speed, int damage)
    {
        if (player == null) return;
        float originHeight = behaviour == EnemyBehaviourStyle.Boss
            ? GetProfileFloat(p => p.bossProjectileOriginHeight, 0.86f)
            : GetProfileFloat(p => p.projectileOriginHeight, 0.63f);
        float muzzleFacing = direction;
        if ((behaviour == EnemyBehaviourStyle.RangedKiter || behaviour == EnemyBehaviourStyle.Boss) &&
            Mathf.Abs(player.transform.position.x - transform.position.x) > 0.05f)
            muzzleFacing = Mathf.Sign(player.transform.position.x - transform.position.x);
        Vector2 origin = (Vector2)transform.position + new Vector2(muzzleFacing * 0.35f, originHeight);
        Vector2 heading = ((Vector2)player.transform.position + Vector2.up * 0.58f - origin).normalized;
        float angle = Mathf.Atan2(heading.y, heading.x) + angleOffset;
        heading = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        GameObject projectileObject = new GameObject(behaviour == EnemyBehaviourStyle.Boss ? "Crownfire Bolt" : "Enemy Projectile");
        int projectileLayer = LayerMask.NameToLayer("Projectile");
        if (projectileLayer >= 0) projectileObject.layer = projectileLayer;
        projectileObject.transform.position = origin;
        SpriteRenderer renderer = projectileObject.AddComponent<SpriteRenderer>();
        Sprite projectileSprite = profile != null ? profile.GetProjectileSprite() : null;
        renderer.sprite = projectileSprite != null ? projectileSprite : GameArt.Get("projectile_enemy_arrow");
        renderer.sortingOrder = 13;
        Rigidbody2D projectileBody = projectileObject.AddComponent<Rigidbody2D>();
        projectileBody.gravityScale = 0f;
        projectileBody.freezeRotation = true;
        BoxCollider2D collider = projectileObject.AddComponent<BoxCollider2D>();
        GameTuning tuning = GameTuning.Current;
        collider.size = tuning == null ? new Vector2(0.98f, 0.34f) : new Vector2(tuning.projectileColliderWidth, tuning.projectileColliderHeight);
        Projectile projectile = projectileObject.AddComponent<Projectile>();
        projectile.Launch(heading, false, damage, speed);
    }

    private Sprite GetFrame(int frame)
    {
        if (profile != null)
        {
            Sprite custom = profile.GetFrame(frame);
            if (custom != null) return custom;
        }
        return GameArt.Get(LegacySpriteName(frame));
    }

    private string LegacySpriteName(int frame)
    {
        switch (kind)
        {
            case EnemyKind.Slime: return "enemy_slime_" + frame;
            case EnemyKind.Melee: return "enemy_goblin_" + frame;
            case EnemyKind.Archer: return "enemy_archer_" + frame;
            case EnemyKind.Bat: return "enemy_bat_" + frame;
            case EnemyKind.Brute: return "enemy_brute_" + frame;
            case EnemyKind.Boss: return "enemy_boss_" + frame;
            default: return "enemy_goblin_0";
        }
    }

    private float GetProfileFloat(Func<EnemyTypeDefaults, float> selector, float fallback)
    {
        return profile != null ? selector(profile) : fallback;
    }

    private int GetProfileInt(Func<EnemyTypeDefaults, int> selector, int fallback)
    {
        return profile != null ? selector(profile) : fallback;
    }

    public void TakeDamage(int amount, Vector2 impactDirection)
    {
        if (dying || amount <= 0) return;
        health -= amount;
        WildruneAudioManager.Instance?.Play(WildruneSfx.EnemyHit, 0.72f);
        hurtTintUntil = Time.time + 0.12f;
        if (behaviour != EnemyBehaviourStyle.FlyingHunter)
        {
            body.linearVelocity = new Vector2(Mathf.Sign(impactDirection.x == 0 ? direction : impactDirection.x) * 2.0f,
                Mathf.Max(body.linearVelocity.y, 1.5f));
        }
        if (health <= 0) Die();
    }

    private void Die()
    {
        if (dying) return;
        dying = true;
        WildruneAudioManager.Instance?.Play(WildruneSfx.EnemyDeath);
        GameBootstrap.Instance?.AddScore(scoreValue);
        if (Random.value < (kind == EnemyKind.Boss ? 1f : 0.35f)) DropShard();
        Destroy(gameObject);
    }

    private void DropShard()
    {
        GameObject pickup = new GameObject("Rune Shard");
        pickup.transform.SetParent(transform.parent, true);
        pickup.transform.position = transform.position + Vector3.up * 0.25f;
        SpriteRenderer sr = pickup.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("pickup_shard");
        sr.sortingOrder = 8;
        CircleCollider2D col = pickup.AddComponent<CircleCollider2D>();
        col.radius = 0.33f;
        col.isTrigger = true;
        pickup.AddComponent<CoinPickup>();
    }
}
