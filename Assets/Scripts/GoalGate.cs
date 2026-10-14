using UnityEngine;

public sealed class GoalGate : MonoBehaviour
{
    [Tooltip("If enabled, this exit only activates after every living Boss enemy is defeated.")]
    public bool requiresBossDefeated;
    private bool triggered;
    private bool playerInside;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return;
        playerInside = true;
        TryComplete();
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() == null) return;
        playerInside = true;
        TryComplete();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponent<PlayerController>() != null) playerInside = false;
    }

    private void TryComplete()
    {
        if (triggered || !playerInside) return;
        if (requiresBossDefeated)
        {
            // The gate is only checked while the player is in its trigger, avoiding a global
            // enemy scan during normal gameplay. Boss health reaches zero immediately on defeat.
            EnemyController[] enemies = FindObjectsByType<EnemyController>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
                if (enemies[i] != null && enemies[i].kind == EnemyKind.Boss && enemies[i].health > 0) return;
        }
        triggered = true;
        GameBootstrap.Instance?.CompleteLevel();
    }
}
