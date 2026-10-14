using UnityEngine;

/// <summary>Subtle atmospheric tint on the biome panorama.</summary>
public sealed class BiomeTint : MonoBehaviour
{
    public void SetBiome(string biome)
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr == null) return;
        switch (biome)
        {
            case "ember": sr.color = new Color(1f, 0.78f, 0.72f, 1f); break;
            case "ice": sr.color = new Color(0.78f, 0.91f, 1f, 1f); break;
            case "canopy": sr.color = new Color(0.80f, 1f, 0.83f, 1f); break;
            case "ruins": sr.color = new Color(0.91f, 0.82f, 1f, 1f); break;
            default: sr.color = Color.white; break;
        }
    }
}
