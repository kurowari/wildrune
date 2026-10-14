using UnityEngine;

public sealed class Checkpoint : MonoBehaviour
{
    private bool activated;
    private SpriteRenderer flag;
    private void Awake() { flag = GetComponent<SpriteRenderer>(); }
    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerController player = other.GetComponent<PlayerController>();
        if (player == null || activated) return;
        activated = true;
        player.SetCheckpoint(new Vector3(transform.position.x, transform.position.y - 0.82f, 0f));
        WildruneAudioManager.Instance?.Play(WildruneSfx.Checkpoint);
        if (flag != null) flag.color = new Color(1f, 0.96f, 0.68f, 1f);
    }
}
