using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Runtime entry point and UI/game-state owner. The playable world is assembled from reusable
/// platform/enemy specs so the included Main scene stays lightweight and easy to extend.
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class GameBootstrap : MonoBehaviour
{
    public static GameBootstrap Instance { get; private set; }

    public PlayerController CurrentPlayer { get; private set; }
    public int CurrentLevelIndex { get; private set; }
    public int Score { get; private set; }
    public bool IsPaused => isPaused;
    public string CurrentLevelTitle => LevelBuilder.GetTitle(CurrentLevelIndex);

    private GameObject worldRoot;
    private Camera mainCamera;
    private CameraFollow cameraFollow;
    private Canvas canvas;
    private Transform uiContent;
    private Font uiFont;
    private Text hudCoins;
    private readonly List<Image> hudHeartIcons = new List<Image>();
    private Text hudTimer;
    private Text hudToast;
    private RectTransform hudSafeAreaRect;
    private Rect lastHudSafeArea = new Rect(-1f, -1f, -1f, -1f);
    private int lastHudScreenWidth = -1;
    private int lastHudScreenHeight = -1;
    private float toastUntil;
    private float timerRemaining;
    private int totalCoins;
    private int bankedHearts;
    private bool HardDifficulty => PlayerPrefs.GetInt("Wildrune_HardDifficulty", 0) != 0;
    private bool isPaused;
    private bool isInGame;
    private bool levelHasEnded;
    private bool isControlsMenu;
    private bool isSettingsMenu;
    private bool settingsReturnToPause;
    private bool controlsReturnToPause;
    private bool waitingForRebind;
    private GameControls.Action pendingRebind;
    private string controlsStatus = "CLICK A KEY BINDING TO CHANGE IT";
    private static readonly Dictionary<string, Texture2D> PixelTextCache = new Dictionary<string, Texture2D>();

    // Quick UI tuning knobs. All generated menu/HUD labels and button captions use this scale.
    // Change to 1.0 for original sizes, or 1.15 for a stronger readability boost.
    private const float UI_FONT_SCALE = 1.12f;
    // Screen-pixel gap between MENU's right edge and the rendered timer text's left edge.
    private const float TIMER_MENU_GAP_PIXELS = 24f;

    private readonly Color ink = new Color32(15, 23, 39, 255);
    private readonly Color panelColor = new Color32(13, 21, 37, 236);
    private readonly Color gold = new Color32(248, 201, 103, 255);
    private readonly Color pale = new Color32(237, 242, 235, 255);
    private readonly Color muted = new Color32(177, 194, 204, 255);
    private readonly Color teal = new Color32(47, 143, 138, 255);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureBootstrapped()
    {
        if (UnityEngine.Object.FindFirstObjectByType<GameBootstrap>() != null) return;
        GameObject bootstrap = new GameObject("Wildrune Runtime");
        bootstrap.AddComponent<GameBootstrap>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Time.timeScale = 1f;
        CreateWorldRoot();
        SetupCamera();
        SetupCanvas();
        if (GetComponent<WildruneAudioManager>() == null) gameObject.AddComponent<WildruneAudioManager>();
        WildruneAudioManager.Instance?.PlayTitleMusic();
#if UNITY_EDITOR
        // Wildrune Workbench can bypass the title screen and start directly in a selected level.
        int quickTestLevel = PlayerPrefs.GetInt("Wildrune_EditorQuickTestLevel", -1);
        PlayerPrefs.DeleteKey("Wildrune_EditorQuickTestLevel");
        PlayerPrefs.Save();
        if (quickTestLevel >= 0) StartLevel(quickTestLevel);
        else ShowMainMenu();
#else
        ShowMainMenu();
#endif
    }

    private void Update()
    {
        UpdateHudSafeArea();
        if (waitingForRebind)
        {
            ProcessRebindInput();
        }
        else if (isInGame && !levelHasEnded && Input.GetKeyDown(KeyCode.Escape))
        {
            if (isControlsMenu) ShowSettingsMenu(controlsReturnToPause);
            else if (isSettingsMenu) ShowPauseOverlay();
            else TogglePause();
        }

        if (isInGame && !isPaused && !levelHasEnded)
        {
            timerRemaining -= Time.deltaTime;
            if (timerRemaining <= 0f)
            {
                timerRemaining = 0f;
                TimeExpired();
                return;
            }

            if (CurrentPlayer != null)
            {
                for (int i = 0; i < hudHeartIcons.Count; i++)
                {
                    if (hudHeartIcons[i] == null) continue;
                    bool filled = i < CurrentPlayer.Health;
                    hudHeartIcons[i].color = filled ? Color.white : new Color32(92, 104, 119, 180);
                }
            }
            if (hudCoins != null) hudCoins.text = totalCoins.ToString("000");
            if (hudTimer != null)
            {
                int wholeSeconds = Mathf.CeilToInt(timerRemaining);
                hudTimer.text = (wholeSeconds / 60).ToString("00") + ":" + (wholeSeconds % 60).ToString("00");
                hudTimer.color = timerRemaining <= 20f ? new Color32(255, 105, 92, 255) : pale;
            }
        }

        if (hudToast != null)
        {
            bool visible = Time.unscaledTime < toastUntil;
            hudToast.gameObject.SetActive(visible);
        }
    }

    private void ProcessRebindInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            waitingForRebind = false;
            controlsStatus = "REMAP CANCELLED";
            ShowControlsMenu();
            return;
        }

        Array values = Enum.GetValues(typeof(KeyCode));
        foreach (KeyCode key in values)
        {
            if (key == KeyCode.None || key == KeyCode.Escape || key == KeyCode.Return || key == KeyCode.KeypadEnter)
                continue;
            string name = key.ToString();
            if (name.StartsWith("Mouse", StringComparison.Ordinal) || name.StartsWith("Joystick", StringComparison.Ordinal))
                continue;
            if (!Input.GetKeyDown(key)) continue;

            string message;
            bool success = GameControls.TryRebind(pendingRebind, key, out message);
            waitingForRebind = false;
            controlsStatus = message;
            ShowControlsMenu();
            return;
        }
    }

    private void BeginRebinding(GameControls.Action action)
    {
        pendingRebind = action;
        waitingForRebind = true;
        controlsStatus = "PRESS A KEY  -  ESC TO CANCEL";
        ShowControlsMenu();
    }

    private void UpdateHudSafeArea()
    {
        if (hudSafeAreaRect == null || Screen.width <= 0 || Screen.height <= 0) return;
        Rect safe = Screen.safeArea;
        if (safe == lastHudSafeArea && Screen.width == lastHudScreenWidth && Screen.height == lastHudScreenHeight) return;
        lastHudSafeArea = safe;
        lastHudScreenWidth = Screen.width;
        lastHudScreenHeight = Screen.height;
        hudSafeAreaRect.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
        hudSafeAreaRect.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
        hudSafeAreaRect.offsetMin = Vector2.zero;
        hudSafeAreaRect.offsetMax = Vector2.zero;
    }

    private void OnApplicationPause(bool paused)
    {
        if (!Application.isMobilePlatform) return;
        if (paused)
        {
            AudioListener.pause = true;
            if (isInGame && !isPaused && !levelHasEnded) TogglePause();
        }
        else
        {
            AudioListener.pause = false;
            // A finger may have been held while the OS suspended the game. Require a fresh touch.
            GameControls.ClearTouchInput();
        }
    }

    private void CreateWorldRoot()
    {
        worldRoot = new GameObject("World Root");
        worldRoot.transform.SetParent(transform, false);
    }

    private void SetupCamera()
    {
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.tag = "MainCamera";
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
        }
        mainCamera.orthographic = true;
        mainCamera.orthographicSize = 6f;
        mainCamera.backgroundColor = new Color32(15, 26, 43, 255);
        mainCamera.transform.position = new Vector3(8f, 4.8f, -10f);
        cameraFollow = mainCamera.GetComponent<CameraFollow>();
        if (cameraFollow == null) cameraFollow = mainCamera.gameObject.AddComponent<CameraFollow>();
    }

    private void SetupCanvas()
    {
        GameObject canvasObject = new GameObject("Wildrune UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1600f, 900f);
        scaler.matchWidthOrHeight = 0.5f;
        uiContent = new GameObject("UI Content", typeof(RectTransform)).transform;
        uiContent.SetParent(canvasObject.transform, false);
        SetStretch(uiContent as RectTransform);
        // Unity 2022+ no longer supports the built-in Arial.ttf name and throws
        // before a fallback can run. Use the supported legacy runtime font directly.
        // Use the user's included M5x7 pixel font; the supported legacy runtime font remains fallback.
        uiFont = Resources.Load<Font>("Art/m5x7");
        if (uiFont == null) uiFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (uiFont == null) Debug.LogError("Wildrune could not load the m5x7 or runtime fallback font.");

        // Never use StandaloneInputModule here: it reads Submit/Cancel axes and can throw
        // before it handles mouse clicks if a project has incomplete legacy Input Manager axes.
        EventSystem eventSystem = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
        if (eventSystem == null)
        {
            GameObject eventSystemObject = new GameObject("Wildrune EventSystem", typeof(EventSystem));
            eventSystemObject.transform.SetParent(transform, false);
            eventSystem = eventSystemObject.GetComponent<EventSystem>();
        }
        eventSystem.sendNavigationEvents = false;
        BaseInputModule[] inputModules = eventSystem.GetComponents<BaseInputModule>();
        for (int i = 0; i < inputModules.Length; i++)
        {
            if (inputModules[i] is WildrunePointerInputModule) continue;
            inputModules[i].enabled = false;
            Destroy(inputModules[i]);
        }
        if (eventSystem.GetComponent<WildrunePointerInputModule>() == null)
            eventSystem.gameObject.AddComponent<WildrunePointerInputModule>();
    }

    public void StartLevel(int index)
    {
        LoadLevel(index, true);
    }

    private void LoadLevel(int index, bool resetScore)
    {
        Time.timeScale = 1f;
        isPaused = false;
        isInGame = true;
        levelHasEnded = false;
        isControlsMenu = false;
        isSettingsMenu = false;
        waitingForRebind = false;
        CurrentLevelIndex = Mathf.Clamp(index, 0, 4);
        WildruneAudioManager.Instance?.PlayLevelMusic(CurrentLevelIndex);
        timerRemaining = GameTuning.TimeLimit(HardDifficulty);
        totalCoins = 0;
        bankedHearts = 0;
        if (resetScore) Score = 0;

        for (int i = worldRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(worldRoot.transform.GetChild(i).gameObject);

        CurrentPlayer = LevelBuilder.Build(this, worldRoot.transform, CurrentLevelIndex);
        LevelBuilder.LevelInfo info = LevelBuilder.GetInfo(CurrentLevelIndex);
        if (cameraFollow != null) cameraFollow.Configure(CurrentPlayer.transform, info.goalX, info.cameraCeiling);
        ShowHud();
    }

    public void RestartLevel()
    {
        LoadLevel(CurrentLevelIndex, false);
    }

    public void TogglePause()
    {
        if (!isInGame || levelHasEnded) return;
        if (isPaused) ResumeGame();
        else
        {
            isPaused = true;
            Time.timeScale = 0f;
            WildruneAudioManager.Instance?.Play(WildruneSfx.Pause);
            ShowPauseOverlay();
        }
    }

    public void ResumeGame()
    {
        if (levelHasEnded) return;
        isPaused = false;
        Time.timeScale = 1f;
        WildruneAudioManager.Instance?.Play(WildruneSfx.Resume);
        ShowHud();
    }

    public void CompleteLevel()
    {
        if (levelHasEnded) return;
        levelHasEnded = true;
        isPaused = true;
        Time.timeScale = 0f;
        WildruneAudioManager.Instance?.Play(WildruneSfx.LevelComplete);
        int unlocked = PlayerPrefs.GetInt("Wildrune_BestFrontier", 0);
        PlayerPrefs.SetInt("Wildrune_BestFrontier", Mathf.Max(unlocked, CurrentLevelIndex + 1));
        PlayerPrefs.Save();
        ShowCompletionOverlay();
    }

    public void PlayerDied()
    {
        if (levelHasEnded) return;
        levelHasEnded = true;
        isPaused = true;
        Time.timeScale = 0f;
        WildruneAudioManager.Instance?.Play(WildruneSfx.PlayerDeath);
        ShowDeathOverlay();
    }

    public void AddScore(int amount)
    {
        Score = Mathf.Max(0, Score + amount);
    }

    public void RefreshHud()
    {
        // The HUD is refreshed continuously while playing. This method remains as a convenient event hook.
    }

    public void ShowToast(string message)
    {
        if (hudToast == null) return;
        hudToast.text = message;
        toastUntil = Time.unscaledTime + 1.7f;
        hudToast.gameObject.SetActive(true);
    }

    public void ReturnToMainMenu()
    {
        Time.timeScale = 1f;
        isPaused = false;
        isInGame = false;
        levelHasEnded = false;
        isControlsMenu = false;
        isSettingsMenu = false;
        waitingForRebind = false;
        CurrentPlayer = null;
        ClearWorld();
        WildruneAudioManager.Instance?.PlayTitleMusic();
        ShowMainMenu();
    }

    public void ReturnToLevelSelect()
    {
        Time.timeScale = 1f;
        isPaused = false;
        isInGame = false;
        levelHasEnded = false;
        isControlsMenu = false;
        isSettingsMenu = false;
        waitingForRebind = false;
        CurrentPlayer = null;
        ClearWorld();
        if (cameraFollow != null) cameraFollow.target = null;
        WildruneAudioManager.Instance?.PlayTitleMusic();
        ShowLevelSelect();
    }

    private void ClearWorld()
    {
        for (int i = worldRoot.transform.childCount - 1; i >= 0; i--)
            Destroy(worldRoot.transform.GetChild(i).gameObject);
    }

    private void ShowMainMenu()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        waitingForRebind = false;
        ClearUi();
        AddBackgroundArt("title");
        AddPixelArtLabel(uiContent, "WILDRUNE", gold,
            new Vector2(0.22f, 0.64f), new Vector2(0.78f, 0.79f), Vector2.zero, Vector2.zero);
        AddPixelButton(uiContent, "PLAY", new Vector2(0.405f, 0.37f), new Vector2(0.595f, 0.475f),
            new Color32(28, 72, 62, 255), () => ShowLevelSelect());
        AddButton(uiContent, "SETTINGS", 16, pale, new Vector2(0.425f, 0.285f), new Vector2(0.575f, 0.34f),
            Vector2.zero, Vector2.zero, new Color32(31, 54, 71, 238), () => ShowSettingsMenu(false));
    }

    private void ShowHowToPlay()
    {
        ClearUi();
        AddBackgroundArt("meadow");
        AddOverlay(new Color32(7, 14, 28, 185));
        AddScreenTitle(uiContent, "CONTROLS", gold);
        RectTransform guide = AddPanel(uiContent, new Vector2(0.18f, 0.30f), new Vector2(0.82f, 0.76f), Vector2.zero, Vector2.zero, panelColor);
        AddLabel(guide, "MOVE  A / D OR ARROWS\nJUMP  SPACE / W\nSWORD  J / LEFT CLICK\nBOW  K / RIGHT CLICK\n\nHOLD JUMP FOR A HIGHER ARC", 28, pale, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.92f), Vector2.zero, Vector2.zero);
        AddButton(uiContent, "BACK", 22, pale, new Vector2(0.40f, 0.10f), new Vector2(0.60f, 0.18f),
            Vector2.zero, Vector2.zero, teal, () => ShowMainMenu());
    }

    private void ShowLevelSelect()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        ClearUi();
        AddBackgroundArt("ruins");
        AddOverlay(new Color32(8, 14, 29, 178));
        AddScreenTitle(uiContent, "LEVEL SELECT", gold);
        AddButton(uiContent, DifficultyButtonText(), 18, gold,
            new Vector2(0.36f, 0.675f), new Vector2(0.64f, 0.735f),
            Vector2.zero, Vector2.zero, new Color32(31, 54, 71, 245), () => ToggleDifficulty());

        string[] names = { "GREENWARD\nPATH", "CINDERKEEP", "FROSTBITE\nCROSSING", "MOONLEAF\nCANOPY", "THE BROKEN\nCROWN" };
        Color[] colors = {
            new Color32(47, 107, 68, 245), new Color32(130, 65, 49, 245), new Color32(48, 103, 138, 245),
            new Color32(35, 104, 78, 245), new Color32(79, 61, 112, 245)
        };
        for (int i = 0; i < names.Length; i++)
        {
            int levelIndex = i;
            float px = (i - 2) * 258f;
            AddCenteredButton(uiContent, names[i], 27, pale, new Vector2(px, -48f),
                new Vector2(232f, 270f), colors[i], () => StartLevel(levelIndex));
        }
        AddButton(uiContent, "TITLE", 19, pale, new Vector2(0.44f, 0.075f), new Vector2(0.56f, 0.145f),
            Vector2.zero, Vector2.zero, new Color32(40, 55, 73, 255), () => ShowMainMenu());
    }

    private void ShowSettingsMenu(bool returnToPause)
    {
        settingsReturnToPause = returnToPause && isInGame;
        isSettingsMenu = true;
        isControlsMenu = false;
        waitingForRebind = false;
        ClearUi();
        AddBackgroundArt(isInGame ? LevelBuilder.GetInfo(CurrentLevelIndex).biome : "title");
        AddOverlay(new Color32(5, 12, 24, 205));
        AddScreenTitle(uiContent, "SETTINGS", gold);

        bool touch = GameControls.TouchscreenMode;
        AddLabel(uiContent, "ACTIVE: " + (touch ? "TOUCHSCREEN" : "KEYBOARD"), 20, gold,
            FontStyle.Bold, TextAnchor.MiddleCenter, new Vector2(0.28f, 0.64f), new Vector2(0.72f, 0.70f), Vector2.zero, Vector2.zero);
        AddButton(uiContent, "KEYBOARD", 22, pale, new Vector2(0.28f, 0.49f), new Vector2(0.49f, 0.58f),
            Vector2.zero, Vector2.zero, touch ? new Color32(35, 49, 65, 245) : new Color32(28, 92, 73, 255),
            () => { GameControls.SetScheme(GameControls.ControlScheme.Keyboard); ShowSettingsMenu(settingsReturnToPause); });
        AddButton(uiContent, "TOUCHSCREEN", 22, pale, new Vector2(0.51f, 0.49f), new Vector2(0.72f, 0.58f),
            Vector2.zero, Vector2.zero, touch ? new Color32(28, 92, 73, 255) : new Color32(35, 49, 65, 245),
            () => { GameControls.SetScheme(GameControls.ControlScheme.Touchscreen); ShowSettingsMenu(settingsReturnToPause); });
        AddButton(uiContent, "KEY BINDINGS", 19, pale, new Vector2(0.36f, 0.36f), new Vector2(0.64f, 0.43f),
            Vector2.zero, Vector2.zero, new Color32(34, 58, 77, 255), () =>
            {
                controlsReturnToPause = settingsReturnToPause;
                ShowControlsMenu();
            });
        AddButton(uiContent, "BACK", 19, pale, new Vector2(0.40f, 0.22f), new Vector2(0.60f, 0.29f),
            Vector2.zero, Vector2.zero, teal, () =>
            {
                if (settingsReturnToPause) ShowPauseOverlay();
                else ShowMainMenu();
            });
    }

    private void ShowHud()
    {
        ClearUi();
        isInGame = true;
        isControlsMenu = false;
        isSettingsMenu = false;
        AddRectImage(uiContent, new Vector2(0f, 1f), new Vector2(1f, 1f),
            new Vector2(0f, -64f), Vector2.zero, new Color32(8, 15, 27, 150));
        Transform hudRoot = uiContent;
        if (GameControls.TouchscreenMode || Application.isMobilePlatform)
        {
            GameObject safeHud = new GameObject("HUD Safe Area", typeof(RectTransform));
            safeHud.transform.SetParent(uiContent, false);
            RectTransform safeRect = safeHud.GetComponent<RectTransform>();
            Rect safe = Screen.safeArea;
            safeRect.anchorMin = new Vector2(safe.xMin / Mathf.Max(1f, Screen.width), safe.yMin / Mathf.Max(1f, Screen.height));
            safeRect.anchorMax = new Vector2(safe.xMax / Mathf.Max(1f, Screen.width), safe.yMax / Mathf.Max(1f, Screen.height));
            safeRect.offsetMin = Vector2.zero;
            safeRect.offsetMax = Vector2.zero;
            hudSafeAreaRect = safeRect;
            lastHudSafeArea = safe;
            lastHudScreenWidth = Screen.width;
            lastHudScreenHeight = Screen.height;
            hudRoot = safeHud.transform;
        }

        hudHeartIcons.Clear();
        for (int i = 0; i < (CurrentPlayer != null ? CurrentPlayer.MaxHealth : 5); i++)
        {
            GameObject heartObject = new GameObject("HUD Heart " + (i + 1), typeof(RectTransform), typeof(Image));
            heartObject.transform.SetParent(hudRoot, false);
            RectTransform heartRect = heartObject.GetComponent<RectTransform>();
            heartRect.anchorMin = new Vector2(0f, 1f);
            heartRect.anchorMax = new Vector2(0f, 1f);
            heartRect.offsetMin = new Vector2(28f + i * 34f, -53f);
            heartRect.offsetMax = new Vector2(56f + i * 34f, -25f);
            Image heart = heartObject.GetComponent<Image>();
            heart.sprite = GameArt.Get("icon_heart");
            heart.preserveAspect = true;
            heart.raycastTarget = false;
            hudHeartIcons.Add(heart);
        }
        GameObject coinIconObject = new GameObject("HUD Coin Icon", typeof(RectTransform), typeof(Image));
        coinIconObject.transform.SetParent(hudRoot, false);
        RectTransform coinRect = coinIconObject.GetComponent<RectTransform>();
        coinRect.anchorMin = new Vector2(0f, 1f);
        coinRect.anchorMax = new Vector2(0f, 1f);
        coinRect.offsetMin = new Vector2(226f, -51f);
        coinRect.offsetMax = new Vector2(252f, -25f);
        Image coinIcon = coinIconObject.GetComponent<Image>();
        coinIcon.sprite = GameArt.Get("pickup_shard");
        coinIcon.preserveAspect = true;
        coinIcon.raycastTarget = false;
        hudCoins = AddLabel(hudRoot, totalCoins.ToString("000"), 25, gold, FontStyle.Bold, TextAnchor.MiddleLeft,
            new Vector2(0.16f, 0.925f), new Vector2(0.24f, 0.995f), Vector2.zero, Vector2.zero);
        hudTimer = AddLabel(hudRoot, "03:00", 30, pale, FontStyle.Bold, TextAnchor.MiddleRight,
            new Vector2(0.83f, 0.925f), new Vector2(0.975f, 0.995f), Vector2.zero, Vector2.zero);
        hudToast = AddLabel(hudRoot, "", 24, gold, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.28f, 0.73f), new Vector2(0.72f, 0.81f), Vector2.zero, Vector2.zero);
        hudToast.gameObject.SetActive(false);
        toastUntil = 0f;

        if (GameControls.TouchscreenMode || Application.isMobilePlatform)
        {
            float hudScale = Mathf.Max(0.1f, canvas != null ? canvas.scaleFactor : 1f);
            Vector2 menuButtonSize = new Vector2(Mathf.Max(72f, 64f / hudScale), Mathf.Max(42f, 36f / hudScale));
            Rect safe = Screen.safeArea;
            float safeWidth = Mathf.Max(1f, safe.width);
            float safeHeight = Mathf.Max(1f, safe.height);
            // Position MENU relative to the rendered timer text (not the timer's much wider
            // alignment rectangle), so the button appears immediately to the text's left.
            float timerRightEdge = 0.975f * safeWidth;
            float timerTextWidth = hudTimer != null ? hudTimer.preferredWidth * hudScale : 88f * hudScale;
            float timerTextLeftEdge = timerRightEdge - timerTextWidth;
            float menuButtonWidthPixels = menuButtonSize.x * hudScale;
            float menuX = (timerTextLeftEdge - TIMER_MENU_GAP_PIXELS - menuButtonWidthPixels * 0.5f) / safeWidth;
            menuX = Mathf.Clamp(menuX, (menuButtonWidthPixels * 0.5f + 4f) / safeWidth, 0.94f);
            float menuY = Mathf.Min(0.96f, 1f - (menuButtonSize.y * hudScale * 0.5f + 2f) / safeHeight);
            Vector2 menuAnchor = new Vector2(menuX, menuY);
            AddButton(hudRoot, "MENU", Mathf.CeilToInt(Mathf.Max(14f, 16f / hudScale)), pale,
                menuAnchor, menuAnchor, -menuButtonSize * 0.5f, menuButtonSize * 0.5f,
                new Color32(31, 54, 71, 230), () => TogglePause());
            if (GameControls.TouchscreenMode) MobileControlsOverlay.Create(uiContent);
        }
    }

    private void ShowPauseOverlay()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        waitingForRebind = false;
        ClearUi();
        AddBackgroundArt(LevelBuilder.GetInfo(CurrentLevelIndex).biome);
        AddOverlay(new Color32(7, 12, 23, 190));
        AddScreenTitle(uiContent, "PAUSED", gold);
        AddButton(uiContent, "RESUME", 23, pale, new Vector2(0.37f, 0.61f), new Vector2(0.63f, 0.68f),
            Vector2.zero, Vector2.zero, new Color32(28, 92, 73, 255), () => ResumeGame());
        AddButton(uiContent, "SETTINGS", 19, pale, new Vector2(0.37f, 0.515f), new Vector2(0.63f, 0.585f),
            Vector2.zero, Vector2.zero, new Color32(34, 58, 77, 255), () => ShowSettingsMenu(true));
        AddButton(uiContent, DifficultyButtonText(), 17, pale, new Vector2(0.37f, 0.42f), new Vector2(0.63f, 0.49f),
            Vector2.zero, Vector2.zero, new Color32(57, 63, 78, 255), () => ToggleDifficulty());
        AddButton(uiContent, "RESTART", 19, pale, new Vector2(0.37f, 0.325f), new Vector2(0.63f, 0.395f),
            Vector2.zero, Vector2.zero, new Color32(34, 58, 77, 255), () => RestartLevel());
        AddButton(uiContent, "LEVEL SELECT", 18, pale, new Vector2(0.37f, 0.23f), new Vector2(0.63f, 0.30f),
            Vector2.zero, Vector2.zero, new Color32(34, 58, 77, 255), () => ReturnToLevelSelect());
    }

    private void ShowControlsMenu()
    {
        isControlsMenu = true;
        isSettingsMenu = false;
        ClearUi();
        AddBackgroundArt(LevelBuilder.GetInfo(CurrentLevelIndex).biome);
        AddOverlay(new Color32(5, 12, 24, 214));
        AddScreenTitle(uiContent, "KEY BINDINGS", gold);
        AddLabel(uiContent, "CURRENT KEY  /  DEFAULT KEY", 16, pale, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.25f, 0.77f), new Vector2(0.75f, 0.815f), Vector2.zero, Vector2.zero);

        GameControls.Action[] actions = GameControls.AllActions;
        for (int i = 0; i < actions.Length; i++)
        {
            GameControls.Action action = actions[i];
            float top = 0.735f - i * 0.067f;
            AddRectImage(uiContent, new Vector2(0.20f, top - 0.052f), new Vector2(0.80f, top),
                Vector2.zero, Vector2.zero, new Color32(12, 25, 38, 225));
            AddLabel(uiContent, GameControls.ActionName(action), 17, pale, FontStyle.Bold, TextAnchor.MiddleLeft,
                new Vector2(0.225f, top - 0.052f), new Vector2(0.49f, top), new Vector2(8f, 0f), new Vector2(0f, 0f));

            string buttonText = waitingForRebind && pendingRebind == action
                ? "PRESS A KEY..."
                : GameControls.DisplayName(GameControls.Get(action)) + "   /   DEFAULT " + GameControls.DisplayName(GameControls.DefaultFor(action));
            Color buttonColor = waitingForRebind && pendingRebind == action
                ? new Color32(141, 94, 38, 255)
                : new Color32(39, 72, 88, 255);
            AddButton(uiContent, buttonText, 14, gold,
                new Vector2(0.50f, top - 0.052f), new Vector2(0.785f, top),
                Vector2.zero, Vector2.zero, buttonColor, () => BeginRebinding(action));
        }

        AddLabel(uiContent, controlsStatus, 15, gold, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.17f, 0.205f), new Vector2(0.83f, 0.25f), Vector2.zero, Vector2.zero);
        AddButton(uiContent, "RESET DEFAULTS", 15, pale, new Vector2(0.27f, 0.125f), new Vector2(0.48f, 0.18f),
            Vector2.zero, Vector2.zero, new Color32(74, 57, 44, 255), () =>
            {
                GameControls.ResetDefaults();
                waitingForRebind = false;
                controlsStatus = "DEFAULT KEYS RESTORED";
                ShowControlsMenu();
            });
        AddButton(uiContent, "BACK", 16, pale, new Vector2(0.52f, 0.125f), new Vector2(0.73f, 0.18f),
            Vector2.zero, Vector2.zero, new Color32(28, 92, 73, 255), () =>
            {
                // Key bindings are nested inside Settings, even when Settings was opened from Pause.
                ShowSettingsMenu(controlsReturnToPause);
            });
    }

    private string DifficultyButtonText()
    {
        return "DIFFICULTY: " + (HardDifficulty ? "HARD" : "NORMAL");
    }

    private void ToggleDifficulty()
    {
        bool hard = !HardDifficulty;
        PlayerPrefs.SetInt("Wildrune_HardDifficulty", hard ? 1 : 0);
        PlayerPrefs.Save();
        if (isInGame && isPaused && !levelHasEnded)
        {
            // Selecting a difficulty restarts this level's time budget at the chosen setting.
            timerRemaining = GameTuning.TimeLimit(hard);
            ShowPauseOverlay();
        }
        else
        {
            ShowLevelSelect();
        }
    }

    public void CollectCoin()
    {
        WildruneAudioManager.Instance?.Play(WildruneSfx.CoinPickup);
        totalCoins++;
        int threshold = GameTuning.CoinHeartThreshold;
        if (totalCoins % threshold == 0)
        {
            if (CurrentPlayer != null && CurrentPlayer.Health < CurrentPlayer.MaxHealth)
            {
                CurrentPlayer.Heal(1);
                ShowToast("HEALTH RESTORED");
            }
            else
            {
                // At full health this acts as a stored extra heart, redeemed on the next hit.
                bankedHearts++;
                ShowToast("STORED HEART");
            }
        }
        if (hudCoins != null) hudCoins.text = totalCoins.ToString("000");
    }

    /// <summary>Spend a banked coin-earned heart after any hit, refilling one heart immediately.</summary>
    public bool TryRedeemCoinHeart(PlayerController player)
    {
        if (player == null || bankedHearts <= 0 || player.Health >= player.MaxHealth) return false;
        bankedHearts--;
        player.Heal(1);
        ShowToast("STORED HEART RESTORED");
        return true;
    }

    private void TimeExpired()
    {
        if (levelHasEnded) return;
        WildruneAudioManager.Instance?.Play(WildruneSfx.TimeUp);
        levelHasEnded = true;
        isPaused = true;
        Time.timeScale = 0f;
        ShowTimeUpOverlay();
    }

    private void ShowTimeUpOverlay()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        ClearUi();
        AddBackgroundArt(LevelBuilder.GetInfo(CurrentLevelIndex).biome);
        AddOverlay(new Color32(14, 7, 19, 205));
        AddScreenTitle(uiContent, "TIME UP", new Color32(255, 118, 101, 255));
        AddButton(uiContent, "RESTART", 24, pale, new Vector2(0.36f, 0.39f), new Vector2(0.64f, 0.49f),
            Vector2.zero, Vector2.zero, teal, () => RestartLevel());
    }

    private void ShowCompletionOverlay()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        ClearUi();
        AddBackgroundArt(LevelBuilder.GetInfo(CurrentLevelIndex).biome);
        AddOverlay(new Color32(7, 14, 28, 178));
        string title = CurrentLevelIndex == 4 ? "CROWN BROKEN" : "LEVEL CLEARED";
        AddScreenTitle(uiContent, title, gold);
        AddLabel(uiContent, "SCORE  " + Score.ToString("0000"), 24, pale, FontStyle.Bold, TextAnchor.MiddleCenter,
            new Vector2(0.25f, 0.53f), new Vector2(0.75f, 0.61f), Vector2.zero, Vector2.zero);
        string nextLabel = CurrentLevelIndex < 4 ? "NEXT LEVEL" : "PLAY AGAIN";
        AddButton(uiContent, nextLabel, 24, pale, new Vector2(0.36f, 0.40f), new Vector2(0.64f, 0.49f),
            Vector2.zero, Vector2.zero, teal, () => LoadLevel(CurrentLevelIndex < 4 ? CurrentLevelIndex + 1 : 0, false));
        AddButton(uiContent, "LEVEL SELECT", 19, pale, new Vector2(0.36f, 0.30f), new Vector2(0.64f, 0.38f),
            Vector2.zero, Vector2.zero, new Color32(54, 69, 89, 255), () => ReturnToLevelSelect());
        AddButton(uiContent, "TITLE", 19, pale, new Vector2(0.40f, 0.20f), new Vector2(0.60f, 0.27f),
            Vector2.zero, Vector2.zero, new Color32(54, 69, 89, 255), () => ReturnToMainMenu());
    }

    private void ShowDeathOverlay()
    {
        isControlsMenu = false;
        isSettingsMenu = false;
        ClearUi();
        AddBackgroundArt(LevelBuilder.GetInfo(CurrentLevelIndex).biome);
        AddOverlay(new Color32(14, 7, 19, 205));
        AddScreenTitle(uiContent, "GAME OVER", new Color32(241, 133, 116, 255));
        AddButton(uiContent, "RESTART", 24, pale, new Vector2(0.36f, 0.39f), new Vector2(0.64f, 0.49f),
            Vector2.zero, Vector2.zero, teal, () => RestartLevel());
    }

    private void ClearUi()
    {
        GameControls.ClearTouchInput();
        hudSafeAreaRect = null;
        lastHudSafeArea = new Rect(-1f, -1f, -1f, -1f);
        lastHudScreenWidth = -1;
        lastHudScreenHeight = -1;
        hudCoins = null;
        hudTimer = null;
        hudToast = null;
        hudHeartIcons.Clear();
        if (uiContent == null) return;
        for (int i = uiContent.childCount - 1; i >= 0; i--)
            Destroy(uiContent.GetChild(i).gameObject);
    }

    private void AddBackgroundArt(string biome)
    {
        GameObject background = new GameObject("Menu Panorama", typeof(RectTransform), typeof(Image));
        background.transform.SetParent(uiContent, false);
        RectTransform rect = background.GetComponent<RectTransform>();
        SetStretch(rect);
        Image image = background.GetComponent<Image>();
        image.sprite = GameArt.Get(biome == "title" ? "bg_title" : "bg_" + biome);
        image.color = Color.white;
        image.raycastTarget = false;
    }

    private void AddOverlay(Color color)
    {
        AddRectImage(uiContent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, color);
    }

    private RectTransform AddPanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private Image AddRectImage(Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, Color color)
    {
        GameObject go = new GameObject("UI Shade", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private Text AddLabel(Transform parent, string content, int size, Color color, FontStyle style, TextAnchor alignment,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        GameObject go = new GameObject("Text - " + (string.IsNullOrEmpty(content) ? "Label" : content.Split('\n')[0]), typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Text text = go.GetComponent<Text>();
        text.text = content;
        text.font = uiFont;
        text.fontSize = Mathf.CeilToInt(size * UI_FONT_SCALE);
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
        Outline outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(1f, -1f);
        return text;
    }

    private static void PlayButtonSound(string label)
    {
        // ResumeGame plays its own unpause cue; avoid layering a generic click over it.
        if (string.Equals(label, "RESUME", StringComparison.OrdinalIgnoreCase)) return;
        bool backAction = !string.IsNullOrEmpty(label) &&
            (label.IndexOf("BACK", StringComparison.OrdinalIgnoreCase) >= 0 ||
             label.IndexOf("CANCEL", StringComparison.OrdinalIgnoreCase) >= 0);
        WildruneAudioManager.Instance?.Play(backAction ? WildruneSfx.UiBack : WildruneSfx.UiConfirm);
    }

    private static void AddButtonHoverAudio(GameObject buttonObject)
    {
        EventTrigger trigger = buttonObject.AddComponent<EventTrigger>();
        EventTrigger.Entry hover = new EventTrigger.Entry();
        hover.eventID = EventTriggerType.PointerEnter;
        hover.callback.AddListener(delegate(BaseEventData eventData)
        {
            WildruneAudioManager.Instance?.Play(WildruneSfx.UiHover, 0.45f);
        });
        trigger.triggers.Add(hover);
    }

    private Button AddButton(Transform parent, string label, int size, Color textColor,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax,
        Color background, Action action)
    {
        GameObject go = new GameObject("Button - " + (string.IsNullOrEmpty(label) ? "Level Card" : label), typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        Image image = go.GetComponent<Image>();
        image.color = background;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, Color.white, 0.15f);
        colors.pressedColor = Color.Lerp(background, gold, 0.22f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        button.transition = Selectable.Transition.ColorTint;
        Outline frame = go.AddComponent<Outline>();
        frame.effectColor = new Color32(221, 174, 79, 230);
        frame.effectDistance = new Vector2(2f, -2f);
        button.onClick.AddListener(() =>
        {
            PlayButtonSound(label);
            action?.Invoke();
        });
        AddButtonHoverAudio(go);

        GameObject textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        RectTransform textRect = textGo.GetComponent<RectTransform>();
        SetStretch(textRect);
        textRect.offsetMin = new Vector2(9f, 5f);
        textRect.offsetMax = new Vector2(-9f, -5f);
        Text text = textGo.GetComponent<Text>();
        text.text = label;
        text.font = uiFont;
        text.fontSize = Mathf.CeilToInt(size * UI_FONT_SCALE);
        text.fontStyle = FontStyle.Bold;
        text.color = textColor;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.supportRichText = false;
        text.raycastTarget = false;
        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.48f);
        outline.effectDistance = new Vector2(1f, -1f);
        return button;
    }

    // Shared screen heading: every menu/result title uses the same pixel-glyph scale and baseline.
    // The title width follows its text length rather than stretching short and long words differently.
    private void AddScreenTitle(Transform parent, string text, Color color)
    {
        Texture2D texture = GetPixelTextTexture(text);
        const float pixelScale = 9f;
        Vector2 anchor = new Vector2(0.5f, 0.84f);
        Vector2 size = new Vector2(texture.width * pixelScale, texture.height * pixelScale);

        GameObject shadow = new GameObject("Screen Title Shadow - " + text, typeof(RectTransform), typeof(RawImage));
        shadow.transform.SetParent(parent, false);
        RectTransform shadowRect = shadow.GetComponent<RectTransform>();
        shadowRect.anchorMin = shadowRect.anchorMax = anchor;
        shadowRect.pivot = new Vector2(0.5f, 0.5f);
        shadowRect.sizeDelta = size;
        shadowRect.anchoredPosition = new Vector2(4f, -4f);
        RawImage shadowImage = shadow.GetComponent<RawImage>();
        shadowImage.texture = texture;
        shadowImage.color = new Color32(5, 13, 27, 225);
        shadowImage.raycastTarget = false;

        GameObject titleObject = new GameObject("Screen Title - " + text, typeof(RectTransform), typeof(RawImage));
        titleObject.transform.SetParent(parent, false);
        RectTransform titleRect = titleObject.GetComponent<RectTransform>();
        titleRect.anchorMin = titleRect.anchorMax = anchor;
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.sizeDelta = size;
        titleRect.anchoredPosition = Vector2.zero;
        RawImage titleImage = titleObject.GetComponent<RawImage>();
        titleImage.texture = texture;
        titleImage.color = color;
        titleImage.raycastTarget = false;
    }

    private void AddPixelArtLabel(Transform parent, string text, Color color,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        Texture2D texture = GetPixelTextTexture(text);
        GameObject shadow = new GameObject("Pixel Text Shadow - " + text, typeof(RectTransform), typeof(RawImage));
        shadow.transform.SetParent(parent, false);
        RectTransform shadowRect = shadow.GetComponent<RectTransform>();
        shadowRect.anchorMin = anchorMin;
        shadowRect.anchorMax = anchorMax;
        shadowRect.offsetMin = offsetMin + new Vector2(4f, -4f);
        shadowRect.offsetMax = offsetMax + new Vector2(4f, -4f);
        RawImage shadowImage = shadow.GetComponent<RawImage>();
        shadowImage.texture = texture;
        shadowImage.color = new Color32(5, 13, 27, 225);
        shadowImage.raycastTarget = false;

        GameObject go = new GameObject("Pixel Text - " + text, typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
        RawImage raw = go.GetComponent<RawImage>();
        raw.texture = texture;
        raw.color = color;
        raw.raycastTarget = false;
    }

    private Button AddPixelButton(Transform parent, string label, Vector2 anchorMin, Vector2 anchorMax,
        Color background, Action action)
    {
        GameObject go = new GameObject("Pixel Button - " + label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        RectTransform rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = true;
        Button button = go.GetComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = background;
        colors.highlightedColor = Color.Lerp(background, new Color32(248, 201, 103, 255), 0.24f);
        colors.pressedColor = Color.Lerp(background, Color.white, 0.24f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.06f;
        button.colors = colors;
        Outline frame = go.AddComponent<Outline>();
        frame.effectColor = new Color32(255, 220, 133, 255);
        frame.effectDistance = new Vector2(4f, -4f);
        button.onClick.AddListener(() =>
        {
            PlayButtonSound(label);
            action?.Invoke();
        });
        AddButtonHoverAudio(go);

        GameObject labelObject = new GameObject("Pixel Label - " + label, typeof(RectTransform), typeof(RawImage));
        labelObject.transform.SetParent(go.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        SetStretch(labelRect);
        labelRect.offsetMin = new Vector2(18f, 8f);
        labelRect.offsetMax = new Vector2(-18f, -8f);
        RawImage raw = labelObject.GetComponent<RawImage>();
        raw.texture = GetPixelTextTexture(label);
        raw.color = new Color32(255, 235, 184, 255);
        raw.raycastTarget = false;
        return button;
    }

    private static Texture2D GetPixelTextTexture(string text)
    {
        if (PixelTextCache.TryGetValue(text, out Texture2D cached) && cached != null) return cached;
        int width = Mathf.Max(5, text.Length * 6 - 1);
        const int height = 7;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
        texture.name = "Pixel Text - " + text;
        texture.filterMode = FilterMode.Point;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[width * height];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 0);
        for (int c = 0; c < text.Length; c++)
        {
            string[] rows = GetPixelGlyph(char.ToUpperInvariant(text[c]));
            for (int y = 0; y < height; y++)
                for (int x = 0; x < 5; x++)
                    if (rows[y][x] == '1') pixels[(height - 1 - y) * width + c * 6 + x] = new Color32(255, 255, 255, 255);
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        PixelTextCache[text] = texture;
        return texture;
    }

    private static string[] GetPixelGlyph(char c)
    {
        switch (c)
        {
            case 'A': return new[] { "01110", "10001", "10001", "11111", "10001", "10001", "10001" };
            case 'B': return new[] { "11110", "10001", "10001", "11110", "10001", "10001", "11110" };
            case 'C': return new[] { "01111", "10000", "10000", "10000", "10000", "10000", "01111" };
            case 'D': return new[] { "11110", "10001", "10001", "10001", "10001", "10001", "11110" };
            case 'E': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "11111" };
            case 'F': return new[] { "11111", "10000", "10000", "11110", "10000", "10000", "10000" };
            case 'G': return new[] { "01111", "10000", "10000", "10111", "10001", "10001", "01111" };
            case 'H': return new[] { "10001", "10001", "10001", "11111", "10001", "10001", "10001" };
            case 'I': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "11111" };
            case 'J': return new[] { "00111", "00010", "00010", "00010", "10010", "10010", "01100" };
            case 'K': return new[] { "10001", "10010", "10100", "11000", "10100", "10010", "10001" };
            case 'L': return new[] { "10000", "10000", "10000", "10000", "10000", "10000", "11111" };
            case 'M': return new[] { "10001", "11011", "10101", "10101", "10001", "10001", "10001" };
            case 'N': return new[] { "10001", "11001", "10101", "10011", "10001", "10001", "10001" };
            case 'O': return new[] { "01110", "10001", "10001", "10001", "10001", "10001", "01110" };
            case 'P': return new[] { "11110", "10001", "10001", "11110", "10000", "10000", "10000" };
            case 'Q': return new[] { "01110", "10001", "10001", "10001", "10101", "10010", "01101" };
            case 'R': return new[] { "11110", "10001", "10001", "11110", "10100", "10010", "10001" };
            case 'S': return new[] { "01111", "10000", "10000", "01110", "00001", "00001", "11110" };
            case 'T': return new[] { "11111", "00100", "00100", "00100", "00100", "00100", "00100" };
            case 'U': return new[] { "10001", "10001", "10001", "10001", "10001", "10001", "01110" };
            case 'V': return new[] { "10001", "10001", "10001", "10001", "10001", "01010", "00100" };
            case 'W': return new[] { "10001", "10001", "10001", "10101", "10101", "10101", "01010" };
            case 'X': return new[] { "10001", "10001", "01010", "00100", "01010", "10001", "10001" };
            case 'Y': return new[] { "10001", "10001", "01010", "00100", "00100", "00100", "00100" };
            case 'Z': return new[] { "11111", "00001", "00010", "00100", "01000", "10000", "11111" };
            default: return new[] { "00000", "00000", "00000", "00000", "00000", "00000", "00000" };
        }
    }

    private Button AddCenteredButton(Transform parent, string label, int size, Color textColor,
        Vector2 center, Vector2 dimensions, Color background, Action action)
    {
        Vector2 anchor = new Vector2(0.5f, 0.47f);
        return AddButton(parent, label, size, textColor, anchor, anchor,
            center - dimensions * 0.5f, center + dimensions * 0.5f, background, action);
    }

    private Text AddCenteredLabel(Transform parent, string content, int size, Color color, FontStyle style,
        TextAnchor alignment, Vector2 center, Vector2 dimensions)
    {
        Vector2 anchor = new Vector2(0.5f, 0.47f);
        return AddLabel(parent, content, size, color, style, alignment, anchor, anchor,
            center - dimensions * 0.5f, center + dimensions * 0.5f);
    }

    private static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
