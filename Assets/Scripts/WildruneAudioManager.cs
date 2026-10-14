using UnityEngine;

public enum WildruneSfx
{
    UiConfirm, UiHover, UiBack, Pause, Resume,
    Jump, DoubleJump, WallJump, Landing, GrassFootstep, StoneFootstep, IceFootstep,
    SwordSwing, BowShot, ArrowImpact, EnemyHit, EnemyDeath,
    PlayerHurt, PlayerDeath, CoinPickup, Heal, Checkpoint, LevelComplete, TimeUp
}

/// <summary>
/// One persistent 2D audio manager. Clip assignments and volume levels live in
/// Resources/Audio/WildruneAudioConfig so audio can be changed in the Inspector.
/// </summary>
[DisallowMultipleComponent]
public sealed class WildruneAudioManager : MonoBehaviour
{
    public static WildruneAudioManager Instance { get; private set; }

    [Tooltip("Optional override. When empty, loads Resources/Audio/WildruneAudioConfig.")]
    public WildruneAudioConfig config;

    private AudioSource musicSource;
    private AudioSource sfxSource;
    private AudioSource uiSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        config = config != null ? config : Resources.Load<WildruneAudioConfig>("Audio/WildruneAudioConfig");

        musicSource = gameObject.AddComponent<AudioSource>();
        musicSource.playOnAwake = false;
        musicSource.loop = true;
        musicSource.spatialBlend = 0f;
        musicSource.dopplerLevel = 0f;
        musicSource.volume = config != null ? config.musicVolume : 0.32f;

        sfxSource = gameObject.AddComponent<AudioSource>();
        ConfigureOneShotSource(sfxSource, config != null ? config.sfxVolume : 0.78f);
        uiSource = gameObject.AddComponent<AudioSource>();
        ConfigureOneShotSource(uiSource, config != null ? config.uiVolume : 0.68f);

        if (config == null)
            Debug.LogWarning("Wildrune audio config not found. Create Assets/Resources/Audio/WildruneAudioConfig.asset or assign a config on WildruneAudioManager.");
    }

    private static void ConfigureOneShotSource(AudioSource source, float volume)
    {
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.volume = volume;
    }

    public void PlayTitleMusic()
    {
        if (config == null) return;
        PlayMusic(config.titleMusic != null ? config.titleMusic : config.meadowMusic);
    }

    public void PlayLevelMusic(int levelIndex)
    {
        if (config == null) return;
        AudioClip clip = null;
        switch (Mathf.Clamp(levelIndex, 0, 4))
        {
            case 0: clip = config.meadowMusic; break;
            case 1: clip = config.emberMusic; break;
            case 2: clip = config.iceMusic; break;
            case 3: clip = config.canopyMusic; break;
            default: clip = config.finaleMusic; break;
        }
        if (clip == null) clip = config.meadowMusic != null ? config.meadowMusic : config.titleMusic;
        PlayMusic(clip);
    }

    private void PlayMusic(AudioClip clip)
    {
        if (clip == null || musicSource == null) return;
        if (musicSource.clip == clip && musicSource.isPlaying) return;
        musicSource.Stop();
        musicSource.clip = clip;
        musicSource.loop = true;
        musicSource.volume = config != null ? config.musicVolume : 0.32f;
        musicSource.Play();
    }

    public void Play(WildruneSfx cue, float volumeScale = 1f)
    {
        if (config == null) return;
        AudioClip clip = ClipFor(cue);
        if (clip == null) return; // Deliberate placeholder slots are safe when left empty.
        AudioSource source = IsUiCue(cue) ? uiSource : sfxSource;
        float baseVolume = IsUiCue(cue) ? config.uiVolume : config.sfxVolume;
        source.PlayOneShot(clip, Mathf.Clamp01(volumeScale) * baseVolume);
    }

    public void PlayFootstep(bool onIce, bool onStone)
    {
        Play(onIce ? WildruneSfx.IceFootstep : onStone ? WildruneSfx.StoneFootstep : WildruneSfx.GrassFootstep,
            0.58f);
    }

    private bool IsUiCue(WildruneSfx cue)
    {
        return cue == WildruneSfx.UiConfirm || cue == WildruneSfx.UiHover || cue == WildruneSfx.UiBack ||
               cue == WildruneSfx.Pause || cue == WildruneSfx.Resume;
    }

    private AudioClip ClipFor(WildruneSfx cue)
    {
        switch (cue)
        {
            case WildruneSfx.UiConfirm: return config.uiConfirm;
            case WildruneSfx.UiHover: return config.uiHover;
            case WildruneSfx.UiBack: return config.uiBack;
            case WildruneSfx.Pause: return config.pause;
            case WildruneSfx.Resume: return config.resume;
            case WildruneSfx.Jump: return config.jump;
            case WildruneSfx.DoubleJump: return config.doubleJump != null ? config.doubleJump : config.jump;
            case WildruneSfx.WallJump: return config.wallJump != null ? config.wallJump : config.jump;
            case WildruneSfx.Landing: return config.landing;
            case WildruneSfx.GrassFootstep: return config.grassFootstep;
            case WildruneSfx.StoneFootstep: return config.stoneFootstep;
            case WildruneSfx.IceFootstep: return config.iceFootstep != null ? config.iceFootstep : config.stoneFootstep;
            case WildruneSfx.SwordSwing: return config.swordSwing;
            case WildruneSfx.BowShot: return config.bowShot;
            case WildruneSfx.ArrowImpact: return config.arrowImpact;
            case WildruneSfx.EnemyHit: return config.enemyHit;
            case WildruneSfx.EnemyDeath: return config.enemyDeath;
            case WildruneSfx.PlayerHurt: return config.playerHurt;
            case WildruneSfx.PlayerDeath: return config.playerDeath;
            case WildruneSfx.CoinPickup: return config.coinPickup;
            case WildruneSfx.Heal: return config.heal;
            case WildruneSfx.Checkpoint: return config.checkpoint;
            case WildruneSfx.LevelComplete: return config.levelComplete;
            case WildruneSfx.TimeUp: return config.timeUp;
            default: return null;
        }
    }
}
