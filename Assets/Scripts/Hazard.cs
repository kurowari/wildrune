using UnityEngine;

public sealed class Hazard : MonoBehaviour
{
    public int damage = 1;
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player != null) player.TakeDamage(damage, transform.position);
    }
}
