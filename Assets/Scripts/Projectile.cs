using UnityEngine;

[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public sealed class Projectile : MonoBehaviour
{
    public bool fromPlayer;
    public int damage = 1;
    public float speed = 13f;
    public float lifetime = 2.2f;
    public Vector2 direction = Vector2.right;
    [Tooltip("Circle-cast hit radius. Slightly forgiving hit detection is intentional for fast pixel arrows.")]
    public float hitRadius = 0.22f;

    private Rigidbody2D body;
    private bool spent;
    private Vector2 previousPosition;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        body.interpolation = RigidbodyInterpolation2D.Interpolate;
        Collider2D collider = GetComponent<Collider2D>();
        collider.isTrigger = true;
        ApplyGlobalProjectileTuning();
        previousPosition = body.position;
    }

    public void Launch(Vector2 heading, bool friendly, int amount, float velocity)
    {
        direction = heading.sqrMagnitude > 0.01f ? heading.normalized : Vector2.right;
        fromPlayer = friendly;
        damage = amount;
        speed = velocity;
        ApplyGlobalProjectileTuning();
        transform.right = direction;
        if (body == null) body = GetComponent<Rigidbody2D>();
        body.linearVelocity = direction * speed;
        previousPosition = body.position;
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        if (renderer != null && direction.x < 0f) renderer.flipY = true;
    }

    private void ApplyGlobalProjectileTuning()
    {
        GameTuning tuning = GameTuning.Current;
        if (tuning == null) return;
        hitRadius = Mathf.Max(0.22f, tuning.projectileHitRadius);
        BoxCollider2D box = GetComponent<BoxCollider2D>();
        if (box != null)
            box.size = new Vector2(Mathf.Max(0.1f, tuning.projectileColliderWidth), Mathf.Max(0.05f, tuning.projectileColliderHeight));
        float scale = Mathf.Clamp(tuning.projectileVisualScale, 0.5f, 3f);
        transform.localScale = new Vector3(scale, scale, 1f);
    }

    private void Update()
    {
        lifetime -= Time.deltaTime;
        if (lifetime <= 0f) Destroy(gameObject);
    }

    private void FixedUpdate()
    {
        if (spent || body == null) return;
        Vector2 now = body.position;
        Vector2 travel = now - previousPosition;
        if (travel.sqrMagnitude > 0.000001f)
        {
            int mask = fromPlayer
                ? LayerMask.GetMask("Enemy", "Ground", "Hazard")
                : LayerMask.GetMask("Player", "Ground", "Hazard");
            RaycastHit2D hit = Physics2D.CircleCast(previousPosition, hitRadius, travel.normalized,
                travel.magnitude + 0.10f, mask);
            if (hit.collider != null) TryHit(hit.collider);
        }
        previousPosition = now;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryHit(other);
    }

    private void TryHit(Collider2D other)
    {
        if (spent || other == null) return;
        if (fromPlayer)
        {
            EnemyController enemy = other.GetComponentInParent<EnemyController>();
            if (enemy != null)
            {
                spent = true;
                WildruneAudioManager.Instance?.Play(WildruneSfx.ArrowImpact, 0.65f);
                enemy.TakeDamage(damage, direction);
                Destroy(gameObject);
                return;
            }
        }
        else
        {
            PlayerController player = other.GetComponentInParent<PlayerController>();
            if (player != null)
            {
                spent = true;
                player.TakeDamage(damage, transform.position);
                Destroy(gameObject);
                return;
            }
        }

        if (other.gameObject.layer == LayerMask.NameToLayer("Ground") ||
            other.GetComponentInParent<Hazard>() != null)
        {
            spent = true;
            Destroy(gameObject);
        }
    }
}
