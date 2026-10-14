using UnityEngine;

/// <summary>Collectible rune shard that advances the coin-heart system.</summary>
public sealed class CoinPickup : MonoBehaviour
{
    private float bobOffset;
    private Vector3 basePosition;
    private bool collected;

    private void Start()
    {
        basePosition = transform.position;
        bobOffset = Random.Range(0f, 6.28f);
    }

    private void Update()
    {
        if (!collected)
            transform.position = basePosition + Vector3.up * (Mathf.Sin(Time.time * 3f + bobOffset) * 0.08f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (collected) return;
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null) return;
        collected = true;
        GameBootstrap.Instance?.CollectCoin();
        Destroy(gameObject);
    }
}
