using UnityEngine;

/// <summary>
/// Inspector-editable sound library. Assign or swap clips here without changing gameplay code.
/// A null clip simply means that cue stays silent until you assign an audio asset.
/// </summary>
[CreateAssetMenu(menuName = "Wildrune/Audio Config", fileName = "WildruneAudioConfig")]
public sealed class WildruneAudioConfig : ScriptableObject
{
    [Header("Music — all music sources loop; per-level slots are optional")]
    public AudioClip titleMusic;
    public AudioClip meadowMusic;
    public AudioClip emberMusic;
    public AudioClip iceMusic;
    public AudioClip canopyMusic;
    public AudioClip finaleMusic;

    [Header("Menu UI")]
    public AudioClip uiConfirm;
    public AudioClip uiHover;
    public AudioClip uiBack;
    public AudioClip pause;
    public AudioClip resume;

    [Header("Movement")]
    public AudioClip jump;
    public AudioClip doubleJump;
    public AudioClip wallJump;
    public AudioClip landing;
    public AudioClip grassFootstep;
    public AudioClip stoneFootstep;
    public AudioClip iceFootstep;

    [Header("Combat — Bow Shot and Arrow Impact are intentionally easy-to-swap slots")]
    public AudioClip swordSwing;
    public AudioClip bowShot;
    public AudioClip arrowImpact;
    public AudioClip enemyHit;
    public AudioClip enemyDeath;

    [Header("Player, pickups and level events")]
    public AudioClip playerHurt;
    public AudioClip playerDeath;
    public AudioClip coinPickup;
    public AudioClip heal;
    public AudioClip checkpoint;
    public AudioClip levelComplete;
    public AudioClip timeUp;

    [Header("Volumes")]
    [Range(0f, 1f)] public float musicVolume = 0.32f;
    [Range(0f, 1f)] public float sfxVolume = 0.78f;
    [Range(0f, 1f)] public float uiVolume = 0.68f;
}
