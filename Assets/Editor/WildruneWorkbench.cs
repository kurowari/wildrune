#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// In-editor workbench for editing Wildrune's data-driven levels and quickly play-testing them.
/// Open via Tools > Wildrune Workbench. SceneView handles edit the selected LevelDefinition asset.
/// </summary>
public sealed class WildruneWorkbench : EditorWindow
{
    private enum Page { LevelEditor, Preview, Tuning, Enemies, Platforms, Audio }
    private enum SceneTool { Select, Platform, Wall, Enemy, Coin, Spike, Spawn, Goal, Checkpoint }
    private enum PlatformPreset { GroundLarge, SkySmall, Custom }
    private enum SelectedKind { None, Platform, Wall, Enemy, Pickup, Spike, Spawn, Goal, Checkpoint }

    private const string MainScenePath = "Assets/Scenes/Main.unity";
    private const string TuningPath = "Assets/Resources/GameTuning.asset";
    private const string AudioPath = "Assets/Resources/Audio/WildruneAudioConfig.asset";
    private const string EnemyDefaultsPath = "Assets/Resources/EnemyDefaults.asset";
    private const string PlatformDefaultsPath = "Assets/Resources/PlatformDefaults.asset";
    private const string LevelFolder = "Assets/Resources/Levels";

    private Page page;
    private SceneTool sceneTool = SceneTool.Select;
    private SelectedKind selectedKind;
    private int selectedIndex = -1;
    private LevelDefinition[] levels = Array.Empty<LevelDefinition>();
    private string[] levelNames = Array.Empty<string>();
    private int levelIndex;
    private LevelDefinition level;
    private GameTuning tuning;
    private WildruneAudioConfig audioConfig;
    private EnemyDefaults enemyDefaults;
    private PlatformDefaults platformDefaults;
    private UnityEditor.Editor platformDefaultsInspector;
    private UnityEditor.Editor enemyDefaultsInspector;
    private int selectedEnemyProfileIndex;
    private UnityEditor.Editor levelInspector;
    private UnityEditor.Editor tuningInspector;
    private UnityEditor.Editor audioInspector;
    private Vector2 scroll;
    private bool snapToGrid = true;
    private bool showCollisionOutlines = true;
    private float gridSize = 0.5f;
    private float newPlatformWidth = 8f;
    private float newWallHeight = 8f;
    private PlatformPreset platformPreset = PlatformPreset.GroundLarge;
    private bool platformWidthInitialized;
    private string newPlatformStyle = "meadow";
    private bool previewStartView;
    private Vector2 previewScroll;
    private bool saveScheduled;
    private double lastEditTime;
    private static readonly Dictionary<string, Texture2D> PreviewTextureCache = new Dictionary<string, Texture2D>();
    private EnemyKind newEnemyKind = EnemyKind.Melee;
    private bool testHardDifficulty;
    private string validationText = "Select Validate Layout to check this level.";
    private MessageType validationType = MessageType.Info;

    [MenuItem("Tools/Wildrune Workbench")]
    public static void Open()
    {
        WildruneWorkbench window = GetWindow<WildruneWorkbench>();
        window.titleContent = new GUIContent("Wildrune Workbench");
        window.minSize = new Vector2(370f, 540f);
        window.RefreshAssets();
        window.Show();
    }

    private void OnEnable()
    {
        titleContent = new GUIContent("Wildrune Workbench");
        SceneView.duringSceneGui += DuringSceneGUI;
        RefreshAssets();
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGUI;
        EditorApplication.update -= FlushPendingLevelSave;
        if (saveScheduled && level != null && EditorUtility.IsDirty(level)) AssetDatabase.SaveAssetIfDirty(level);
        saveScheduled = false;
        DestroyEditors();
    }

    private void DestroyEditors()
    {
        if (levelInspector != null) DestroyImmediate(levelInspector);
        if (tuningInspector != null) DestroyImmediate(tuningInspector);
        if (audioInspector != null) DestroyImmediate(audioInspector);
        if (enemyDefaultsInspector != null) DestroyImmediate(enemyDefaultsInspector);
        if (platformDefaultsInspector != null) DestroyImmediate(platformDefaultsInspector);
        levelInspector = null;
        tuningInspector = null;
        audioInspector = null;
        enemyDefaultsInspector = null;
        platformDefaultsInspector = null;
    }

    private void RefreshAssets()
    {
        levels = AssetDatabase.FindAssets("t:LevelDefinition", new[] { LevelFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<LevelDefinition>)
            .Where(item => item != null)
            .ToArray();
        levelNames = levels.Select(item => item.name + " — " + item.title).ToArray();
        if (levels.Length == 0)
        {
            level = null;
            levelIndex = 0;
        }
        else
        {
            levelIndex = Mathf.Clamp(levelIndex, 0, levels.Length - 1);
            level = levels[levelIndex];
        }

        tuning = AssetDatabase.LoadAssetAtPath<GameTuning>(TuningPath);
        audioConfig = AssetDatabase.LoadAssetAtPath<WildruneAudioConfig>(AudioPath);
        enemyDefaults = AssetDatabase.LoadAssetAtPath<EnemyDefaults>(EnemyDefaultsPath);
        platformDefaults = AssetDatabase.LoadAssetAtPath<PlatformDefaults>(PlatformDefaultsPath);
        if (enemyDefaults != null && enemyDefaults.profiles != null && enemyDefaults.profiles.Count > 0)
            selectedEnemyProfileIndex = Mathf.Clamp(selectedEnemyProfileIndex, 0, enemyDefaults.profiles.Count - 1);
        else selectedEnemyProfileIndex = 0;
        if (!platformWidthInitialized && tuning != null)
        {
            newPlatformWidth = tuning.groundPlatformDefaultWidth;
            platformWidthInitialized = true;
        }
        DestroyEditors();
        Repaint();
        SceneView.RepaintAll();
    }

    private void OnGUI()
    {
        DrawHeader();
        page = (Page)GUILayout.Toolbar((int)page, new[] { "Level Editor", "Visual Preview", "Global Defaults", "Enemy Defaults", "Platform Defaults", "Audio" }, GUILayout.Height(28));
        scroll = EditorGUILayout.BeginScrollView(scroll);
        switch (page)
        {
            case Page.LevelEditor: DrawLevelEditorPage(); break;
            case Page.Preview: DrawPreviewPage(); break;
            case Page.Tuning: DrawTuningPage(); break;
            case Page.Enemies: DrawEnemyDefaultsPage(); break;
            case Page.Platforms: DrawPlatformDefaultsPage(); break;
            case Page.Audio: DrawAudioPage(); break;
        }
        EditorGUILayout.EndScrollView();
        EditorGUILayout.Space(5);
        DrawFooter();
    }

    private void DrawHeader()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        GUILayout.Label("WILDRUNE WORKBENCH", EditorStyles.boldLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(65))) RefreshAssets();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("Edit data here, move objects in the Scene view, then Quick Test to start directly in the selected level. Changes are saved to the existing ScriptableObject assets.", MessageType.Info);

        EditorGUILayout.BeginHorizontal();
        GUI.enabled = levels.Length > 0;
        EditorGUILayout.LabelField("Level", GUILayout.Width(40));
        int nextIndex = EditorGUILayout.Popup(levelIndex, levelNames);
        if (nextIndex != levelIndex && nextIndex >= 0 && nextIndex < levels.Length)
        {
            levelIndex = nextIndex;
            level = levels[levelIndex];
            selectedKind = SelectedKind.None;
            selectedIndex = -1;
            DestroyEditors();
            validationText = "Select Validate Layout to check this level.";
            SceneView.RepaintAll();
        }
        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Quick Test Selected Level", GUILayout.Height(30))) QuickTestSelectedLevel();
        if (GUILayout.Button("Play Main Menu", GUILayout.Height(30))) PlayMainMenu();
        EditorGUILayout.EndHorizontal();
        testHardDifficulty = EditorGUILayout.ToggleLeft("Use Hard difficulty for Quick Test (shorter timer)", testHardDifficulty);
    }

    private void DrawLevelEditorPage()
    {
        if (level == null)
        {
            EditorGUILayout.HelpBox("No LevelDefinition assets were found under Assets/Resources/Levels. Check that folder, then click Refresh.", MessageType.Error);
            return;
        }

        EditorGUILayout.LabelField("Scene View Level Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Open the Scene tab. Choose a tool below, then click in the Scene view to place that item. Select mode lets you click, drag and resize existing objects. Select an object and press Delete to remove it.", MessageType.None);
        sceneTool = (SceneTool)EditorGUILayout.EnumPopup("Placement Tool", sceneTool);
        EditorGUILayout.BeginHorizontal();
        snapToGrid = EditorGUILayout.ToggleLeft("Snap to grid", snapToGrid, GUILayout.Width(105));
        using (new EditorGUI.DisabledScope(!snapToGrid))
            gridSize = EditorGUILayout.FloatField("Grid", gridSize);
        gridSize = Mathf.Clamp(gridSize, 0.1f, 5f);
        EditorGUILayout.EndHorizontal();
        showCollisionOutlines = EditorGUILayout.ToggleLeft("Show actual platform collider bounds", showCollisionOutlines);

        EditorGUI.BeginChangeCheck();
        PlatformPreset selectedPreset = (PlatformPreset)EditorGUILayout.EnumPopup("Platform size preset", platformPreset);
        if (EditorGUI.EndChangeCheck())
        {
            platformPreset = selectedPreset;
            if (platformPreset == PlatformPreset.GroundLarge)
                newPlatformWidth = tuning != null ? tuning.groundPlatformDefaultWidth : 8f;
            else if (platformPreset == PlatformPreset.SkySmall)
                newPlatformWidth = tuning != null ? tuning.skyPlatformDefaultWidth : 2.5f;
        }
        EditorGUI.BeginChangeCheck();
        float enteredPlatformWidth = EditorGUILayout.FloatField("New platform width", newPlatformWidth);
        if (EditorGUI.EndChangeCheck())
        {
            newPlatformWidth = Mathf.Max(1f, enteredPlatformWidth);
            platformPreset = PlatformPreset.Custom;
        }
        EditorGUILayout.LabelField(platformPreset == PlatformPreset.SkySmall
            ? "Small floating platform preset. You can still resize after placing it."
            : platformPreset == PlatformPreset.GroundLarge
                ? "Wide ground platform preset. Change its default in Global Defaults."
                : "Custom width; existing level platforms are not affected.", EditorStyles.miniLabel);
        string[] availableStyles = GetPlatformStyleKeys();
        int styleIndex = Mathf.Clamp(EditorGUILayout.Popup("New platform style", Mathf.Max(0, Array.IndexOf(availableStyles, newPlatformStyle)), availableStyles), 0, availableStyles.Length - 1);
        newPlatformStyle = availableStyles[styleIndex];
        newEnemyKind = DrawEnemyKindPopup("New enemy type", newEnemyKind);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Select / Move")) sceneTool = SceneTool.Select;
        if (GUILayout.Button("Place Platform")) sceneTool = SceneTool.Platform;
        if (GUILayout.Button("Place Enemy")) sceneTool = SceneTool.Enemy;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Place Coin")) sceneTool = SceneTool.Coin;
        if (GUILayout.Button("Place Spike")) sceneTool = SceneTool.Spike;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Place Wall")) sceneTool = SceneTool.Wall;
        newWallHeight = Mathf.Max(2f, EditorGUILayout.FloatField("Wall height", newWallHeight));
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Set Spawn")) sceneTool = SceneTool.Spawn;
        if (GUILayout.Button("Set Goal")) sceneTool = SceneTool.Goal;
        if (GUILayout.Button("Checkpoint")) sceneTool = SceneTool.Checkpoint;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Focus Level in Scene")) FocusLevel();
        if (GUILayout.Button("Validate Layout")) ValidateLayout();
        if (GUILayout.Button("Delete Selected")) DeleteSelected();
        EditorGUILayout.EndHorizontal();
        using (new EditorGUI.DisabledScope(level == null || selectedKind != SelectedKind.Platform || selectedIndex < 0 || selectedIndex >= level.platforms.Count))
        {
            if (GUILayout.Button("Add Sky Step from Selected Platform")) AddSkyStepFromSelected();
        }
        EditorGUILayout.HelpBox(validationText, validationType);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Level data (advanced / exact values)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("You can still edit every list and exact coordinate in this Inspector. Use the Scene view handles for quick visual edits.", MessageType.None);
        DrawAssetInspector(level, ref levelInspector);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Current content", EditorStyles.boldLabel);
        EditorGUILayout.LabelField($"Platforms: {level.platforms.Count}   Walls: {level.walls.Count}   Enemies: {level.enemies.Count}   Pickups: {level.pickups.Count}   Spikes: {level.spikes.Count + level.spikePositions.Count}");
    }

    private void DrawPreviewPage()
    {
        if (level == null)
        {
            EditorGUILayout.HelpBox("Select a level first, then open Visual Preview.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Visual Level Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("This preview uses the real game background, platform tiles, player/enemy sprites and pickups. Whole Level shows the layout; Start View frames the spawn area like a gameplay camera. It is an art/layout preview, not a live physics simulation—use Quick Test for exact runtime rendering and collisions.", MessageType.Info);
        previewStartView = GUILayout.Toggle(previewStartView, previewStartView ? "Preview: Player Start View" : "Preview: Whole Level", "Button", GUILayout.Height(26));
        previewScroll = EditorGUILayout.BeginScrollView(previewScroll, GUILayout.Height(385));
        Rect previewRect = GUILayoutUtility.GetRect(320f, 250f, GUILayout.ExpandWidth(true));
        if (Event.current.type == EventType.Repaint)
            DrawArtPreview(previewRect);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Focus Scene Editor")) { page = Page.LevelEditor; FocusLevel(); }
        if (GUILayout.Button("Quick Test This Level", GUILayout.Height(28))) QuickTestSelectedLevel();
        EditorGUILayout.EndHorizontal();
        EditorGUILayout.LabelField($"{level.title}  •  {level.platforms.Count} platforms  •  {level.enemies.Count} enemies  •  {level.pickups.Count} pickups", EditorStyles.miniLabel);
    }

    private void DrawArtPreview(Rect rect)
    {
        EditorGUI.DrawRect(rect, new Color(0.075f, 0.09f, 0.12f));
        Texture2D background = PreviewTexture("bg_" + level.biome);
        if (background != null) GUI.DrawTexture(rect, background, ScaleMode.ScaleAndCrop, false);

        float worldLeft, worldRight, worldBottom, worldTop;
        float aspect = Mathf.Max(1.1f, rect.width / Mathf.Max(1f, rect.height));
        if (previewStartView)
        {
            worldBottom = -1.6f;
            float worldHeight = 10.5f;
            float worldWidth = worldHeight * aspect;
            float centerX = level.playerSpawn.x + Mathf.Min(4f, worldWidth * 0.18f);
            worldLeft = centerX - worldWidth * 0.5f;
            worldRight = centerX + worldWidth * 0.5f;
            worldTop = worldBottom + worldHeight;
        }
        else
        {
            worldLeft = -5f;
            worldRight = Mathf.Max(level.goalX + 8f, 40f);
            worldBottom = -2f;
            worldTop = Mathf.Max(level.cameraCeiling + 2f, 10f);
        }
        float ppu = Mathf.Min(rect.width / Mathf.Max(1f, worldRight - worldLeft), rect.height / Mathf.Max(1f, worldTop - worldBottom));
        float contentWidth = (worldRight - worldLeft) * ppu;
        float contentHeight = (worldTop - worldBottom) * ppu;
        float offsetX = rect.x + (rect.width - contentWidth) * 0.5f;
        float offsetY = rect.y + (rect.height - contentHeight) * 0.5f;

        Rect WorldRect(float x, float y, float width, float height)
        {
            return new Rect(offsetX + (x - worldLeft) * ppu,
                offsetY + (worldTop - (y + height)) * ppu,
                Mathf.Max(1f, width * ppu), Mathf.Max(1f, height * ppu));
        }

        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry platform = level.platforms[i];
            Texture2D tile = PreviewTexture(TileKey(platform.style));
            if (tile == null) continue;
            int count = Mathf.CeilToInt(platform.width);
            for (int tileIndex = 0; tileIndex < count; tileIndex++)
            {
                float tileX = platform.x + tileIndex;
                float tileWidth = Mathf.Min(1f, platform.x + platform.width - tileX);
                if (tileWidth <= 0f) continue;
                GUI.DrawTexture(WorldRect(tileX, platform.topY - 1f, tileWidth, 1f), tile, ScaleMode.StretchToFill, false);
            }
            if (showCollisionOutlines)
            {
                float depth = tuning != null ? tuning.platformColliderDepth : 1.06f;
                float extra = tuning != null ? tuning.platformColliderExtraWidth : 0.12f;
                Rect collision = WorldRect(platform.x - extra * 0.5f, platform.topY - depth, platform.width + extra, depth);
                DrawPreviewOutline(collision, new Color(1f, 0.15f, 0.88f, 0.95f), 1f);
            }
        }

        for (int i = 0; i < level.walls.Count; i++)
        {
            LevelWallEntry wall = level.walls[i];
            Texture2D wallTile = PreviewTexture(TileKey(wall.style));
            if (wallTile == null) continue;
            int rows = Mathf.CeilToInt(wall.height);
            for (int r = 0; r < rows; r++)
            {
                float tileHeight = Mathf.Min(1f, wall.height - r);
                if (tileHeight > 0f) GUI.DrawTexture(WorldRect(wall.x, wall.bottomY + r, wall.width, tileHeight), wallTile, ScaleMode.StretchToFill, false);
            }
            if (showCollisionOutlines) DrawPreviewOutline(WorldRect(wall.x, wall.bottomY, wall.width, wall.height), new Color(1f, 0.15f, 0.88f, 0.95f), 1f);
        }

        for (int i = 0; i < level.spikes.Count; i++)
        {
            float x = level.spikes[i];
            Texture2D sprite = PreviewTexture("hazard_spikes");
            if (sprite != null) GUI.DrawTexture(WorldRect(x - 0.4f, SupportAt(x, 0f) - 0.15f, 0.8f, 0.58f), sprite, ScaleMode.StretchToFill, true);
        }
        for (int i = 0; i < level.spikePositions.Count; i++)
        {
            Vector2 point = level.spikePositions[i];
            Texture2D sprite = PreviewTexture("hazard_spikes");
            if (sprite != null) GUI.DrawTexture(WorldRect(point.x - 0.4f, point.y - 0.15f, 0.8f, 0.58f), sprite, ScaleMode.StretchToFill, true);
        }
        for (int i = 0; i < level.pickups.Count; i++)
        {
            LevelPickupEntry pickup = level.pickups[i];
            Texture2D sprite = PreviewTexture("pickup_shard");
            if (sprite != null) GUI.DrawTexture(WorldRect(pickup.x - 0.28f, pickup.y - 0.28f, 0.56f, 0.56f), sprite, ScaleMode.ScaleToFit, true);
        }
        if (level.checkpointX > -900f)
        {
            Texture2D flag = PreviewTexture("checkpoint");
            float checkpointTop = level.checkpointY > -900f ? level.checkpointY : SupportAt(level.checkpointX, 0f);
            if (flag != null) GUI.DrawTexture(WorldRect(level.checkpointX - 0.5f, checkpointTop, 1f, 1.8f), flag, ScaleMode.ScaleToFit, true);
        }
        Texture2D goal = PreviewTexture("goal_gate");
        if (goal != null)
        {
            float ground = level.goalY > -900f ? level.goalY : SupportAt(level.goalX, 0f);
            GUI.DrawTexture(WorldRect(level.goalX - 0.72f, ground + 0.175f, 1.44f, 2.65f), goal, ScaleMode.ScaleToFit, true);
        }
        for (int i = 0; i < level.enemies.Count; i++)
        {
            LevelEnemyEntry enemy = level.enemies[i];
            Texture2D sprite = EnemyPreviewTexture(enemy.kind);
            float enemyScale = EnemyVisualScale(enemy.kind);
            EnemyTypeDefaults enemyProfile = enemyDefaults != null ? enemyDefaults.Find(enemy.kind) : null;
            float enemyPpu = enemyProfile != null ? Mathf.Max(8f, enemyProfile.pixelsPerUnit) : 48f;
            Vector2 visualSize = sprite != null ? new Vector2(sprite.width / enemyPpu, sprite.height / enemyPpu) * enemyScale : EnemyPreviewColliderSize(enemy.kind) * enemyScale;
            Rect visualRect = WorldRect(enemy.x - visualSize.x * 0.5f, enemy.y, visualSize.x, visualSize.y);
            if (sprite != null) GUI.DrawTexture(visualRect, sprite, ScaleMode.ScaleToFit, true);
            if (showCollisionOutlines)
            {
                Vector2 colliderSize = EnemyPreviewColliderSize(enemy.kind) * enemyScale;
                Vector2 colliderOffset = EnemyPreviewColliderOffset(enemy.kind) * enemyScale;
                float colliderBottom = enemy.y + colliderOffset.y - colliderSize.y * 0.5f;
                Rect hitboxRect = WorldRect(enemy.x - colliderSize.x * 0.5f, colliderBottom, colliderSize.x, colliderSize.y);
                DrawPreviewOutline(hitboxRect, new Color(1f, 0.15f, 0.88f, 0.85f), 1f);
            }
        }
        Texture2D player = PreviewTexture("player_idle_0");
        float playerScale = tuning != null ? tuning.playerVisualScale : 1f;
        float playerWidth = tuning != null ? tuning.playerColliderWidth * playerScale : 0.6f;
        float playerHeight = tuning != null ? tuning.playerColliderHeight * playerScale : 1.22f;
        float playerSpriteWidth = player != null ? player.width / 32f * playerScale : playerWidth;
        float playerSpriteHeight = player != null ? player.height / 32f * playerScale : playerHeight;
        Rect playerSpriteRect = WorldRect(level.playerSpawn.x - playerSpriteWidth * 0.5f, level.playerSpawn.y, playerSpriteWidth, playerSpriteHeight);
        if (player != null) GUI.DrawTexture(playerSpriteRect, player, ScaleMode.ScaleToFit, true);
        Rect playerHitboxRect = WorldRect(level.playerSpawn.x - playerWidth * 0.5f, level.playerSpawn.y, playerWidth, playerHeight);
        if (showCollisionOutlines) DrawPreviewOutline(playerHitboxRect, new Color(1f, 0.86f, 0.16f, 0.95f), 1.2f);

        GUI.color = Color.white;
        GUI.Label(new Rect(rect.x + 7f, rect.y + 5f, rect.width - 14f, 18f), level.title + (previewStartView ? " • START VIEW" : " • WHOLE LEVEL"), EditorStyles.whiteMiniLabel);
        DrawPreviewOutline(rect, new Color(1f, 1f, 1f, 0.6f), 1f);
    }

    private static void DrawPreviewOutline(Rect r, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, thickness), color);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
        EditorGUI.DrawRect(new Rect(r.x, r.y, thickness, r.height), color);
        EditorGUI.DrawRect(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
    }

    private static Texture2D PreviewTexture(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        Texture2D texture;
        if (PreviewTextureCache.TryGetValue(key, out texture) && texture != null) return texture;
        texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Art/" + key + ".png");
        PreviewTextureCache[key] = texture;
        return texture;
    }

    private static string TileKey(string style)
    {
        switch (style)
        {
            case "ember": return "tile_ember";
            case "ice": return "tile_ice";
            case "canopy": return "tile_canopy";
            case "ruins": return "tile_ruins";
            case "stone": return "tile_stone";
            default: return "tile_grass";
        }
    }

    private static string EnemyPreviewKey(EnemyKind kind)
    {
        switch (kind)
        {
            case EnemyKind.Slime: return "enemy_slime_0";
            case EnemyKind.Archer: return "enemy_archer_0";
            case EnemyKind.Bat: return "enemy_bat_0";
            case EnemyKind.Brute: return "enemy_brute_0";
            case EnemyKind.Boss: return "enemy_boss_0";
            default: return "enemy_goblin_0";
        }
    }

    private Texture2D EnemyPreviewTexture(EnemyKind kind)
    {
        EnemyTypeDefaults profile = enemyDefaults != null ? enemyDefaults.Find(kind) : null;
        return profile != null && profile.frame0 != null ? profile.frame0 : PreviewTexture(EnemyPreviewKey(kind));
    }

    private Vector2 EnemyPreviewColliderSize(EnemyKind kind)
    {
        EnemyTypeDefaults profile = enemyDefaults != null ? enemyDefaults.Find(kind) : null;
        if (profile != null) return profile.colliderSize;
        switch (kind)
        {
            case EnemyKind.Bat: return new Vector2(0.72f, 0.58f);
            case EnemyKind.Brute: return new Vector2(0.82f, 1.12f);
            case EnemyKind.Boss: return new Vector2(1.18f, 1.42f);
            default: return new Vector2(0.68f, 0.80f);
        }
    }

    private Vector2 EnemyPreviewColliderOffset(EnemyKind kind)
    {
        EnemyTypeDefaults profile = enemyDefaults != null ? enemyDefaults.Find(kind) : null;
        if (profile != null) return profile.colliderOffset;
        switch (kind)
        {
            case EnemyKind.Bat: return new Vector2(0f, 0.48f);
            case EnemyKind.Brute: return new Vector2(0f, 0.56f);
            case EnemyKind.Boss: return new Vector2(0f, 0.68f);
            default: return new Vector2(0f, 0.40f);
        }
    }

    private float EnemyVisualScale(EnemyKind kind)
    {
        EnemyTypeDefaults profile = enemyDefaults != null ? enemyDefaults.Find(kind) : null;
        float perType = profile != null ? Mathf.Max(0.1f, profile.visualScale) : 1f;
        float bossScale = kind == EnemyKind.Boss ? (tuning != null ? tuning.finalBossScale : 1.38f) : 1f;
        return perType * bossScale;
    }

    private float EnemyCenterOffsetY(EnemyKind kind)
    {
        return EnemyPreviewColliderOffset(kind).y * EnemyVisualScale(kind);
    }

    private void AddSkyStepFromSelected()
    {
        if (level == null || !ValidIndex(level.platforms, selectedIndex)) return;
        LevelPlatformEntry source = level.platforms[selectedIndex];
        float width = tuning != null ? Mathf.Max(1f, tuning.skyPlatformDefaultWidth) : 2.5f;
        float rise = tuning != null ? Mathf.Max(0.75f, tuning.recommendedSkyPlatformRise) : 2.2f;
        float gap = tuning != null ? Mathf.Max(0f, tuning.recommendedSkyPlatformGap) : 1.25f;
        Undo.RecordObject(level, "Add sky platform step");
        level.platforms.Add(new LevelPlatformEntry
        {
            x = source.x + source.width + gap,
            topY = source.topY + rise,
            width = width,
            style = source.style
        });
        Select(SelectedKind.Platform, level.platforms.Count - 1);
        sceneTool = SceneTool.Select;
        DirtyLevel();
        FocusLevel();
    }

    private void DrawTuningPage()
    {
        EditorGUILayout.LabelField("Global Defaults (all levels)", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("These are shared defaults, not per-level overrides. Player size, projectile dimensions, platform collision depth/edge overhang, timer, movement, combat and the ground/sky platform presets are edited here. Existing level layout coordinates and widths only change when you edit those level assets.", MessageType.Info);
        if (tuning == null)
        {
            EditorGUILayout.HelpBox("GameTuning.asset not found at " + TuningPath, MessageType.Error);
            if (GUILayout.Button("Create Game Tuning Asset")) CreateTuningAsset();
            return;
        }
        DrawAssetInspector(tuning, ref tuningInspector);
    }

    private void DrawEnemyDefaultsPage()
    {
        EditorGUILayout.LabelField("Enemy Defaults — shared by every level", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each enemy kind has one default profile. Change its sprite textures, behavior style, health, movement speed, touch damage, collider, attack ranges, projectile art and cooldowns here. Levels only store the enemy kind and placement/patrol coordinates.", MessageType.Info);
        if (enemyDefaults == null)
        {
            EditorGUILayout.HelpBox("EnemyDefaults.asset was not found at " + EnemyDefaultsPath + ".", MessageType.Warning);
            if (GUILayout.Button("Create Enemy Defaults Asset")) CreateEnemyDefaultsAsset();
            return;
        }

        if (enemyDefaults.profiles == null) enemyDefaults.profiles = new List<EnemyTypeDefaults>();
        if (enemyDefaults.profiles.Count == 0)
        {
            EditorGUILayout.HelpBox("No profiles are defined yet.", MessageType.Warning);
            if (GUILayout.Button("Add default profiles")) AddMissingEnemyProfiles();
            return;
        }

        string[] labels = enemyDefaults.profiles.Select(p => p == null ? "Missing profile" : (string.IsNullOrEmpty(p.displayName) ? p.kind.ToString() : p.displayName)).ToArray();
        selectedEnemyProfileIndex = Mathf.Clamp(selectedEnemyProfileIndex, 0, labels.Length - 1);
        selectedEnemyProfileIndex = EditorGUILayout.Popup("Enemy type", selectedEnemyProfileIndex, labels);

        SerializedObject serialized = new SerializedObject(enemyDefaults);
        serialized.Update();
        SerializedProperty profiles = serialized.FindProperty("profiles");
        if (profiles != null && selectedEnemyProfileIndex < profiles.arraySize)
        {
            SerializedProperty selectedProfile = profiles.GetArrayElementAtIndex(selectedEnemyProfileIndex);
            if (selectedProfile != null)
            {
                selectedProfile.isExpanded = true;
                EnemyTypeDefaults profile = enemyDefaults.profiles[selectedEnemyProfileIndex];
                string title = profile != null && !string.IsNullOrEmpty(profile.displayName) ? profile.displayName : "Enemy Profile";
                EditorGUILayout.PropertyField(selectedProfile, new GUIContent(title), true);
            }
        }
        if (serialized.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(enemyDefaults);
            SceneView.RepaintAll();
            Repaint();
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset Selected Profile"))
        {
            EnemyTypeDefaults selected = enemyDefaults.profiles[selectedEnemyProfileIndex];
            if (selected != null && EditorUtility.DisplayDialog("Reset Enemy Defaults", "Restore the built-in defaults for " + selected.displayName + "?", "Reset", "Cancel"))
            {
                Undo.RecordObject(enemyDefaults, "Reset enemy profile");
                enemyDefaults.profiles[selectedEnemyProfileIndex] = EnemyDefaults.CreateDefaultProfile(selected.kind);
                EditorUtility.SetDirty(enemyDefaults);
                AssetDatabase.SaveAssets();
                DestroyEditors();
                SceneView.RepaintAll();
            }
        }
        if (GUILayout.Button("Add Missing Profiles")) AddMissingEnemyProfiles();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.HelpBox("Fixing the sword-soldier/ranged mismatch: the supplied skeleton frame is now assigned to the close-range Skeleton Heavy, while the Mushroom Slinger uses the mushroom artwork and a matching small red spore projectile. Change Frame 0 / Frame 1 by dragging any Texture2D from Resources/Art into the profile, and change Behaviour to switch between melee, ranged, flying, hopping, and boss-style AI.", MessageType.None);
        if (GUILayout.Button("Select EnemyDefaults.asset in Project")) EditorGUIUtility.PingObject(enemyDefaults);
    }

    private void CreateEnemyDefaultsAsset()
    {
        EnemyDefaults asset = CreateInstance<EnemyDefaults>();
        asset.profiles = EnemyDefaults.CreateDefaultProfiles();
        AssetDatabase.CreateAsset(asset, EnemyDefaultsPath);
        AssetDatabase.SaveAssets();
        RefreshAssets();
    }

    private void AddMissingEnemyProfiles()
    {
        if (enemyDefaults == null) return;
        Undo.RecordObject(enemyDefaults, "Add missing enemy profiles");
        foreach (EnemyKind kind in Enum.GetValues(typeof(EnemyKind)))
            if (enemyDefaults.Find(kind) == null) enemyDefaults.profiles.Add(EnemyDefaults.CreateDefaultProfile(kind));
        EditorUtility.SetDirty(enemyDefaults);
        AssetDatabase.SaveAssets();
        RefreshAssets();
    }

    private EnemyKind DrawEnemyKindPopup(string label, EnemyKind selected)
    {
        EnemyKind[] kinds = (EnemyKind[])Enum.GetValues(typeof(EnemyKind));
        string[] labels = kinds.Select(EnemyDefaults.DisplayName).ToArray();
        int index = Array.IndexOf(kinds, selected);
        int next = EditorGUILayout.Popup(label, Mathf.Max(0, index), labels);
        return kinds[Mathf.Clamp(next, 0, kinds.Length - 1)];
    }

    private void DrawPlatformDefaultsPage()
    {
        EditorGUILayout.LabelField("Platform Types — shared art and surface behavior", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each profile maps a style key (such as meadow or ice) to a Sprite, fallback GameArt key, tint, slipperiness, friction, and bounciness. Level layouts store only the style key, so changing a profile updates every platform/wall using that style.", MessageType.Info);
        if (platformDefaults == null)
        {
            EditorGUILayout.HelpBox("PlatformDefaults.asset has not been created yet.", MessageType.Warning);
            if (GUILayout.Button("Create Platform Defaults Asset")) CreatePlatformDefaultsAsset();
            return;
        }
        SerializedObject serialized = new SerializedObject(platformDefaults);
        serialized.Update();
        SerializedProperty profiles = serialized.FindProperty("profiles");
        EditorGUILayout.PropertyField(profiles, new GUIContent("Platform Profiles"), true);
        if (serialized.ApplyModifiedProperties())
        {
            EditorUtility.SetDirty(platformDefaults);
            SceneView.RepaintAll();
            Repaint();
        }
        EditorGUILayout.Space(5);
        if (GUILayout.Button("Add Custom Platform Type"))
        {
            Undo.RecordObject(platformDefaults, "Add platform type");
            platformDefaults.profiles.Add(new PlatformTypeProfile { styleKey = "custom_" + platformDefaults.profiles.Count, displayName = "Custom Platform", fallbackSpriteKey = "tile_grass", friction = 0.12f });
            EditorUtility.SetDirty(platformDefaults);
            AssetDatabase.SaveAssets();
        }
        if (GUILayout.Button("Select PlatformDefaults.asset in Project")) EditorGUIUtility.PingObject(platformDefaults);
        EditorGUILayout.HelpBox("To use a new type: add a profile, give it a unique style key, drag a Sprite into Tile Sprite (or enter a Resources/Art sprite key), then select that style when placing platforms. Existing platforms keep their old style until you change their style field in the Level Editor.", MessageType.None);
    }

    private void CreatePlatformDefaultsAsset()
    {
        PlatformDefaults asset = CreateInstance<PlatformDefaults>();
        asset.profiles = PlatformDefaults.CreateBuiltInProfiles();
        AssetDatabase.CreateAsset(asset, PlatformDefaultsPath);
        AssetDatabase.SaveAssets();
        RefreshAssets();
    }

    private string[] GetPlatformStyleKeys()
    {
        List<string> keys = new List<string>(PlatformStyles);
        if (platformDefaults != null && platformDefaults.profiles != null)
            foreach (PlatformTypeProfile profile in platformDefaults.profiles)
                if (profile != null && !string.IsNullOrWhiteSpace(profile.styleKey) && !keys.Contains(profile.styleKey)) keys.Add(profile.styleKey);
        return keys.ToArray();
    }

    private void DrawAudioPage()
    {
        EditorGUILayout.LabelField("Music and sound effects", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Drag audio clips into these fields to replace tracks or effects. Adjust the separate music, SFX and UI volumes here.", MessageType.Info);
        if (audioConfig == null)
        {
            EditorGUILayout.HelpBox("WildruneAudioConfig.asset not found at " + AudioPath, MessageType.Error);
            return;
        }
        DrawAssetInspector(audioConfig, ref audioInspector);
    }

    private void DrawAssetInspector(UnityEngine.Object target, ref UnityEditor.Editor cachedEditor)
    {
        if (target == null) return;
        if (cachedEditor == null || cachedEditor.target != target)
        {
            if (cachedEditor != null) DestroyImmediate(cachedEditor);
            cachedEditor = UnityEditor.Editor.CreateEditor(target);
        }
        cachedEditor.OnInspectorGUI();
        if (GUI.changed) EditorUtility.SetDirty(target);
    }

    private void DrawFooter()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
        GUILayout.Label("Tip: Save assets with Ctrl+S. Quick Test opens Main.unity and skips the title menu.", EditorStyles.miniLabel);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Save Assets", GUILayout.Width(85))) AssetDatabase.SaveAssets();
        if (GUILayout.Button("Open Main Scene", GUILayout.Width(115))) OpenMainScene();
        EditorGUILayout.EndHorizontal();
    }

    private static readonly string[] PlatformStyles = { "meadow", "ember", "ice", "canopy", "ruins", "stone" };

    private void DuringSceneGUI(SceneView view)
    {
        if (level == null) return;
        DrawGrid(view);
        DrawSceneOverlay(view);
        DrawPlatforms();
        DrawWalls();
        DrawEnemies();
        DrawPickups();
        DrawSpikes();
        DrawSpawn();
        DrawGoal();
        DrawCheckpoint();

        Event e = Event.current;
        if (sceneTool != SceneTool.Select && e.type == EventType.MouseDown && e.button == 0 && !e.alt && !e.control && !e.command)
        {
            Vector2 mouse = e.mousePosition;
            if (mouse.x > 280f || mouse.y > 80f)
            {
                Vector3 world;
                if (TryMouseWorld(view, mouse, out world))
                {
                    PlaceAt(world);
                    e.Use();
                }
            }
        }

        if (sceneTool == SceneTool.Select && e.type == EventType.KeyDown && e.keyCode == KeyCode.Delete && selectedKind != SelectedKind.None)
        {
            DeleteSelected();
            e.Use();
        }
    }

    private void DrawSceneOverlay(SceneView view)
    {
        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(8, 8, 270, 66), EditorStyles.helpBox);
        GUILayout.Label("WILDRUNE • " + level.name + " • " + sceneTool, EditorStyles.boldLabel);
        GUILayout.Label(sceneTool == SceneTool.Select ? "Click a marker to select; drag handles to edit." : "Click the Scene view to place: " + sceneTool, EditorStyles.miniLabel);
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    private void DrawGrid(SceneView view)
    {
        Camera cam = view.camera;
        if (cam == null) return;
        float halfH = cam.orthographic ? cam.orthographicSize : 10f;
        float halfW = halfH * cam.aspect;
        float left = Mathf.Max(-30f, cam.transform.position.x - halfW);
        float right = Mathf.Min(Mathf.Max(level.goalX + 10f, 40f), cam.transform.position.x + halfW);
        float bottom = Mathf.Max(-8f, cam.transform.position.y - halfH);
        float top = Mathf.Min(Mathf.Max(level.cameraCeiling + 6f, 12f), cam.transform.position.y + halfH);
        Handles.color = new Color(0.45f, 0.72f, 0.78f, 0.12f);
        for (float x = Mathf.Floor(left); x <= right; x += 1f)
            Handles.DrawLine(new Vector3(x, bottom, 0.1f), new Vector3(x, top, 0.1f));
        for (float y = Mathf.Floor(bottom); y <= top; y += 1f)
            Handles.DrawLine(new Vector3(left, y, 0.1f), new Vector3(right, y, 0.1f));
        Handles.color = new Color(0.95f, 0.78f, 0.38f, 0.48f);
        Handles.DrawLine(new Vector3(-5f, 0f, 0.08f), new Vector3(level.goalX + 5f, 0f, 0.08f));
    }

    private void DrawPlatforms()
    {
        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry p = level.platforms[i];
            Color c = StyleColor(p.style);
            Vector3 bl = new Vector3(p.x, p.topY - 1f, 0f);
            Vector3 tl = new Vector3(p.x, p.topY, 0f);
            Vector3 tr = new Vector3(p.x + p.width, p.topY, 0f);
            Vector3 br = new Vector3(p.x + p.width, p.topY - 1f, 0f);
            Handles.DrawSolidRectangleWithOutline(new[] { bl, tl, tr, br }, new Color(c.r, c.g, c.b, 0.23f), c);
            Handles.color = c;
            Handles.DrawLine(tl, tr, 3f);
            if (showCollisionOutlines)
            {
                Handles.color = new Color(1f, 0.25f, 0.9f, 0.9f);
                float colliderDepth = tuning != null ? tuning.platformColliderDepth : 1.06f;
                float extraWidth = tuning != null ? tuning.platformColliderExtraWidth : 0.12f;
                Vector3 cbl = new Vector3(p.x - extraWidth * 0.5f, p.topY - colliderDepth, 0f);
                Vector3 ctl = new Vector3(p.x - extraWidth * 0.5f, p.topY, 0f);
                Vector3 ctr = new Vector3(p.x + p.width + extraWidth * 0.5f, p.topY, 0f);
                Vector3 cbr = new Vector3(p.x + p.width + extraWidth * 0.5f, p.topY - colliderDepth, 0f);
                Handles.DrawDottedLine(cbl, ctl, 3f); Handles.DrawDottedLine(ctl, ctr, 3f);
                Handles.DrawDottedLine(ctr, cbr, 3f); Handles.DrawDottedLine(cbr, cbl, 3f);
            }
            Vector3 center = new Vector3(p.x + p.width * 0.5f, p.topY - 0.5f, 0f);
            Handles.Label(new Vector3(p.x + 0.08f, p.topY + 0.16f, 0f), $"P{i}  w:{p.width:0.##}  y:{p.topY:0.##}");
            if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Platform, i))
            {
                EditorGUI.BeginChangeCheck();
                var fmh_859_64_639275155214800190 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.11f, Vector3.one * (snapToGrid ? gridSize : 0f), Handles.RectangleHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Move platform");
                    p.x = moved.x - p.width * 0.5f;
                    p.topY = moved.y + 0.5f;
                    DirtyLevel();
                }
                Vector3 resize = new Vector3(p.x + p.width, p.topY - 0.5f, 0f);
                EditorGUI.BeginChangeCheck();
                var fmh_869_69_639275155214813932 = Quaternion.identity; Vector3 movedRight = Handles.FreeMoveHandle(resize, HandleUtility.GetHandleSize(resize) * 0.10f, new Vector3(snapToGrid ? gridSize : 0f, 0f, 0f), Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Resize platform");
                    p.width = Mathf.Max(1f, movedRight.x - p.x);
                    DirtyLevel();
                }
            }
            else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.12f, HandleUtility.GetHandleSize(center) * 0.12f, Handles.RectangleHandleCap)) Select(SelectedKind.Platform, i);
        }
    }

    private void DrawWalls()
    {
        for (int i = 0; i < level.walls.Count; i++)
        {
            LevelWallEntry w = level.walls[i];
            Color c = StyleColor(w.style);
            Vector3 bl = new Vector3(w.x, w.bottomY, 0f);
            Vector3 tl = new Vector3(w.x, w.bottomY + w.height, 0f);
            Vector3 tr = new Vector3(w.x + w.width, w.bottomY + w.height, 0f);
            Vector3 br = new Vector3(w.x + w.width, w.bottomY, 0f);
            Handles.DrawSolidRectangleWithOutline(new[] { bl, tl, tr, br }, new Color(c.r, c.g, c.b, 0.25f), c);
            if (showCollisionOutlines)
            {
                Handles.color = new Color(1f, 0.25f, 0.9f, 0.95f);
                Handles.DrawDottedLine(bl, tl, 3f); Handles.DrawDottedLine(tl, tr, 3f); Handles.DrawDottedLine(tr, br, 3f); Handles.DrawDottedLine(br, bl, 3f);
            }
            Vector3 center = new Vector3(w.x + w.width * 0.5f, w.bottomY + w.height * 0.5f, 0f);
            Handles.Label(new Vector3(w.x + 0.08f, w.bottomY + w.height + 0.18f, 0f), $"W{i}  h:{w.height:0.##}");
            if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Wall, i))
            {
                EditorGUI.BeginChangeCheck();
                var fmh_902_64_639275155214832424 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.11f, Vector3.one * (snapToGrid ? gridSize : 0f), Handles.RectangleHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Move wall"); w.x = moved.x - w.width * 0.5f; w.bottomY = moved.y - w.height * 0.5f; DirtyLevel();
                }
                Vector3 widthHandle = new Vector3(w.x + w.width, center.y, 0f);
                EditorGUI.BeginChangeCheck();
                var fmh_909_74_639275155214839743 = Quaternion.identity; Vector3 widthMoved = Handles.FreeMoveHandle(widthHandle, HandleUtility.GetHandleSize(widthHandle) * 0.09f, Vector3.right * (snapToGrid ? gridSize : 0f), Handles.RectangleHandleCap);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Resize wall width"); w.width = Mathf.Max(0.5f, widthMoved.x - w.x); DirtyLevel(); }
                Vector3 heightHandle = new Vector3(w.x + w.width * 0.5f, w.bottomY + w.height, 0f);
                EditorGUI.BeginChangeCheck();
                var fmh_913_76_639275155214847257 = Quaternion.identity; Vector3 heightMoved = Handles.FreeMoveHandle(heightHandle, HandleUtility.GetHandleSize(heightHandle) * 0.09f, Vector3.up * (snapToGrid ? gridSize : 0f), Handles.RectangleHandleCap);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Resize wall height"); w.height = Mathf.Max(2f, heightMoved.y - w.bottomY); DirtyLevel(); }
            }
            else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.11f, HandleUtility.GetHandleSize(center) * 0.11f, Handles.RectangleHandleCap)) Select(SelectedKind.Wall, i);
        }
    }

    private void DrawEnemies()
    {
        for (int i = 0; i < level.enemies.Count; i++)
        {
            LevelEnemyEntry enemy = level.enemies[i];
            float enemyScale = EnemyVisualScale(enemy.kind);
            Vector2 profileCollider = EnemyPreviewColliderSize(enemy.kind) * enemyScale;
            Vector2 profileOffset = EnemyPreviewColliderOffset(enemy.kind) * enemyScale;
            float hitWidth = profileCollider.x;
            float hitHeight = profileCollider.y;
            float hitCenterY = enemy.y + profileOffset.y;
            Vector3 center = new Vector3(enemy.x, hitCenterY, 0f);
            Handles.color = EnemyColor(enemy.kind);
            Vector3 hitBL = new Vector3(enemy.x - hitWidth * 0.5f, hitCenterY - hitHeight * 0.5f, 0f);
            Vector3 hitTL = new Vector3(enemy.x - hitWidth * 0.5f, hitCenterY + hitHeight * 0.5f, 0f);
            Vector3 hitTR = new Vector3(enemy.x + hitWidth * 0.5f, hitCenterY + hitHeight * 0.5f, 0f);
            Vector3 hitBR = new Vector3(enemy.x + hitWidth * 0.5f, hitCenterY - hitHeight * 0.5f, 0f);
            Handles.color = new Color(1f, 0.28f, 0.85f, 0.8f);
            Handles.DrawDottedLine(hitBL, hitTL, 3f); Handles.DrawDottedLine(hitTL, hitTR, 3f);
            Handles.DrawDottedLine(hitTR, hitBR, 3f); Handles.DrawDottedLine(hitBR, hitBL, 3f);
            Handles.color = EnemyColor(enemy.kind);
            Handles.DrawLine(new Vector3(enemy.patrolMin, enemy.y + 0.04f, 0f), new Vector3(enemy.patrolMax, enemy.y + 0.04f, 0f), 2f);
            Handles.DrawWireDisc(center, Vector3.forward, 0.32f);
            Handles.Label(center + Vector3.up * 0.48f, $"E{i} {EnemyDefaults.DisplayName(enemy.kind)}");
            if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Enemy, i))
            {
                EditorGUI.BeginChangeCheck();
                var fmh_947_64_639275155214855871 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.12f, new Vector3(snapToGrid ? gridSize : 0f, snapToGrid ? gridSize : 0f, 0f), Handles.CircleHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Move enemy");
                    enemy.x = moved.x;
                    enemy.y = moved.y - EnemyCenterOffsetY(enemy.kind);
                    DirtyLevel();
                }
                Vector3 minHandle = new Vector3(enemy.patrolMin, enemy.y + 0.04f, 0f);
                Vector3 maxHandle = new Vector3(enemy.patrolMax, enemy.y + 0.04f, 0f);
                EditorGUI.BeginChangeCheck(); var fmh_957_98_639275155214862237 = Quaternion.identity; Vector3 newMin = Handles.FreeMoveHandle(minHandle, HandleUtility.GetHandleSize(minHandle) * 0.08f, new Vector3(snapToGrid ? gridSize : 0f, 0f, 0f), Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Move enemy patrol minimum"); enemy.patrolMin = Mathf.Min(newMin.x, enemy.patrolMax - 0.5f); DirtyLevel(); }
                EditorGUI.BeginChangeCheck(); var fmh_959_98_639275155214868503 = Quaternion.identity; Vector3 newMax = Handles.FreeMoveHandle(maxHandle, HandleUtility.GetHandleSize(maxHandle) * 0.08f, new Vector3(snapToGrid ? gridSize : 0f, 0f, 0f), Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Move enemy patrol maximum"); enemy.patrolMax = Mathf.Max(newMax.x, enemy.patrolMin + 0.5f); DirtyLevel(); }
            }
            else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.13f, HandleUtility.GetHandleSize(center) * 0.13f, Handles.CircleHandleCap)) Select(SelectedKind.Enemy, i);
        }
    }

    private void DrawPickups()
    {
        for (int i = 0; i < level.pickups.Count; i++)
        {
            LevelPickupEntry pickup = level.pickups[i];
            Vector3 p = new Vector3(pickup.x, pickup.y, 0f);
            Handles.color = new Color(1f, 0.78f, 0.23f);
            Handles.DrawSolidDisc(p, Vector3.forward, 0.18f);
            if (showCollisionOutlines) Handles.DrawWireDisc(p, Vector3.forward, 0.38f);
            Handles.Label(p + Vector3.up * 0.2f, "Coin " + i);
            if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Pickup, i))
            {
                EditorGUI.BeginChangeCheck();
                var fmh_979_59_639275155214876671 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(p, HandleUtility.GetHandleSize(p) * 0.1f, Vector3.one * (snapToGrid ? gridSize : 0f), Handles.CircleHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Move pickup"); pickup.x = moved.x; pickup.y = moved.y; DirtyLevel();
                }
            }
            else if (sceneTool == SceneTool.Select && Handles.Button(p, Quaternion.identity, HandleUtility.GetHandleSize(p) * 0.11f, HandleUtility.GetHandleSize(p) * 0.11f, Handles.CircleHandleCap)) Select(SelectedKind.Pickup, i);
        }
    }

    private void DrawSpikes()
    {
        int total = level.spikes.Count + level.spikePositions.Count;
        for (int i = 0; i < total; i++)
        {
            bool explicitPoint = i >= level.spikes.Count;
            int actualIndex = explicitPoint ? i - level.spikes.Count : i;
            float x; float top;
            if (explicitPoint) { Vector2 point = level.spikePositions[actualIndex]; x = point.x; top = point.y; }
            else { x = level.spikes[actualIndex]; top = SupportAt(x, 0f); }
            float y = top + 0.14f;
            Vector3 marker = new Vector3(x, y, 0f);
            Handles.color = new Color(1f, 0.28f, 0.24f);
            Handles.DrawAAConvexPolygon(marker + new Vector3(-0.3f, -0.2f), marker + new Vector3(0f, 0.28f), marker + new Vector3(0.3f, -0.2f));
            if (showCollisionOutlines)
            {
                Vector3 sbl = new Vector3(x - 0.39f, y - 0.285f, 0f); Vector3 stl = new Vector3(x - 0.39f, y + 0.285f, 0f);
                Vector3 str = new Vector3(x + 0.39f, y + 0.285f, 0f); Vector3 sbr = new Vector3(x + 0.39f, y - 0.285f, 0f);
                Handles.color = new Color(1f, 0.25f, 0.9f, 0.95f);
                Handles.DrawDottedLine(sbl, stl, 3f); Handles.DrawDottedLine(stl, str, 3f); Handles.DrawDottedLine(str, sbr, 3f); Handles.DrawDottedLine(sbr, sbl, 3f);
            }
            Handles.Label(marker + Vector3.up * 0.36f, "Spike " + i);
            if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Spike, i))
            {
                EditorGUI.BeginChangeCheck();
                var fmh_1014_64_639275155214884650 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(marker, HandleUtility.GetHandleSize(marker) * 0.10f, new Vector3(snapToGrid ? gridSize : 0f, snapToGrid ? gridSize : 0f, 0f), Handles.ConeHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(level, "Move spike");
                    float support = SupportAt(moved.x, moved.y);
                    if (explicitPoint) level.spikePositions[actualIndex] = new Vector2(moved.x, support);
                    else level.spikes[actualIndex] = moved.x;
                    DirtyLevel();
                }
            }
            else if (sceneTool == SceneTool.Select && Handles.Button(marker, Quaternion.identity, HandleUtility.GetHandleSize(marker) * 0.11f, HandleUtility.GetHandleSize(marker) * 0.11f, Handles.ConeHandleCap)) Select(SelectedKind.Spike, i);
        }
    }

    private void DrawSpawn()
    {
        float spawnWidth = tuning != null ? tuning.playerColliderWidth * tuning.playerVisualScale : 0.60f;
        float spawnHeight = tuning != null ? tuning.playerColliderHeight * tuning.playerVisualScale : 1.22f;
        Vector3 center = level.playerSpawn + new Vector3(0f, spawnHeight * 0.5f, 0f);
        Handles.color = new Color(1f, 0.9f, 0.25f);
        Handles.DrawWireCube(center, new Vector3(spawnWidth, spawnHeight, 0.1f));
        Handles.Label(center + Vector3.up * 0.8f, "PLAYER SPAWN");
        if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Spawn, 0))
        {
            EditorGUI.BeginChangeCheck(); var fmh_1070_90_639275155214900381 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.12f, Vector3.one * (snapToGrid ? gridSize : 0f), Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Move player spawn"); level.playerSpawn = moved + Vector3.down * spawnHeight * 0.5f; level.playerSpawn.z = 0f; DirtyLevel(); }
        }
        else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.14f, HandleUtility.GetHandleSize(center) * 0.14f, Handles.RectangleHandleCap)) Select(SelectedKind.Spawn, 0);
    }

    private void DrawGoal()
    {
        float ground = level.goalY > -900f ? level.goalY : SupportAt(level.goalX, 0f);
        Vector3 center = new Vector3(level.goalX, ground + 1.5f, 0f);
        Handles.color = new Color(0.35f, 0.95f, 0.92f);
        Handles.DrawLine(new Vector3(level.goalX - 0.45f, ground, 0f), new Vector3(level.goalX - 0.45f, ground + 2.7f, 0f), 4f);
        Handles.DrawLine(new Vector3(level.goalX + 0.45f, ground, 0f), new Vector3(level.goalX + 0.45f, ground + 2.7f, 0f), 4f);
        Handles.DrawLine(new Vector3(level.goalX - 0.45f, ground + 2.7f, 0f), new Vector3(level.goalX + 0.45f, ground + 2.7f, 0f), 4f);
        if (showCollisionOutlines)
        {
            Vector3 gbl = new Vector3(level.goalX - 0.725f, ground + 1.5f - 1.325f, 0f);
            Vector3 gtl = new Vector3(level.goalX - 0.725f, ground + 1.5f + 1.325f, 0f);
            Vector3 gtr = new Vector3(level.goalX + 0.725f, ground + 1.5f + 1.325f, 0f);
            Vector3 gbr = new Vector3(level.goalX + 0.725f, ground + 1.5f - 1.325f, 0f);
            Handles.color = new Color(1f, 0.25f, 0.9f, 0.95f);
            Handles.DrawDottedLine(gbl, gtl, 3f); Handles.DrawDottedLine(gtl, gtr, 3f); Handles.DrawDottedLine(gtr, gbr, 3f); Handles.DrawDottedLine(gbr, gbl, 3f);
        }
        Handles.Label(center + Vector3.up * 1.6f, "LEVEL GOAL");
        if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Goal, 0))
        {
            EditorGUI.BeginChangeCheck(); var fmh_1096_90_639275155214908173 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.11f, new Vector3(snapToGrid ? gridSize : 0f, 0f, 0f), Handles.CubeHandleCap);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Move goal"); level.goalX = moved.x; level.goalY = SupportAt(level.goalX, ground); DirtyLevel(); }
        }
        else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.12f, HandleUtility.GetHandleSize(center) * 0.12f, Handles.CubeHandleCap)) Select(SelectedKind.Goal, 0);
    }

    private void DrawCheckpoint()
    {
        if (level.checkpointX <= -900f) return;
        float ground = level.checkpointY > -900f ? level.checkpointY : SupportAt(level.checkpointX, 0f);
        Vector3 center = new Vector3(level.checkpointX, ground + 0.9f, 0f);
        Handles.color = new Color(1f, 0.55f, 0.17f);
        Handles.DrawLine(new Vector3(level.checkpointX, ground, 0f), new Vector3(level.checkpointX, ground + 1.8f, 0f), 3f);
        Handles.DrawWireCube(center, new Vector3(0.6f, 0.38f, 0.1f));
        if (showCollisionOutlines) Handles.DrawWireCube(center, new Vector3(1.1f, 1.8f, 0.1f));
        Handles.Label(center + Vector3.up * 0.36f, "CHECKPOINT");
        if (sceneTool == SceneTool.Select && IsSelected(SelectedKind.Checkpoint, 0))
        {
            EditorGUI.BeginChangeCheck(); var fmh_1114_90_639275155214915630 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(center, HandleUtility.GetHandleSize(center) * 0.1f, new Vector3(snapToGrid ? gridSize : 0f, 0f, 0f), Handles.RectangleHandleCap);
            if (EditorGUI.EndChangeCheck()) { Undo.RecordObject(level, "Move checkpoint"); level.checkpointX = moved.x; level.checkpointY = SupportAt(level.checkpointX, ground); DirtyLevel(); }
        }
        else if (sceneTool == SceneTool.Select && Handles.Button(center, Quaternion.identity, HandleUtility.GetHandleSize(center) * 0.11f, HandleUtility.GetHandleSize(center) * 0.11f, Handles.RectangleHandleCap)) Select(SelectedKind.Checkpoint, 0);
    }

    private void PlaceAt(Vector3 raw)
    {
        if (level == null) return;
        float x = Snap(raw.x);
        float y = Snap(raw.y);
        Undo.RecordObject(level, "Place " + sceneTool);
        switch (sceneTool)
        {
            case SceneTool.Platform:
                level.platforms.Add(new LevelPlatformEntry { x = x - newPlatformWidth * 0.5f, topY = y, width = newPlatformWidth, style = newPlatformStyle });
                Select(SelectedKind.Platform, level.platforms.Count - 1); break;
            case SceneTool.Wall:
                level.walls.Add(new LevelWallEntry { x = x - 0.5f, bottomY = y, height = newWallHeight, width = 1f, style = "stone" });
                Select(SelectedKind.Wall, level.walls.Count - 1); break;
            case SceneTool.Enemy:
                float support = SupportAt(x, y);
                float enemyY = support + 0.03f;
                level.enemies.Add(new LevelEnemyEntry { x = x, y = enemyY, kind = newEnemyKind, patrolMin = x - 1.5f, patrolMax = x + 1.5f });
                Select(SelectedKind.Enemy, level.enemies.Count - 1); break;
            case SceneTool.Coin:
                level.pickups.Add(new LevelPickupEntry { x = x, y = y }); Select(SelectedKind.Pickup, level.pickups.Count - 1); break;
            case SceneTool.Spike:
                float spikeTop = SupportAt(x, y);
                if (y > 0.5f || level.cameraCeiling > 14f) { level.spikePositions.Add(new Vector2(x, spikeTop)); Select(SelectedKind.Spike, level.spikes.Count + level.spikePositions.Count - 1); }
                else { level.spikes.Add(x); Select(SelectedKind.Spike, level.spikes.Count - 1); }
                break;
            case SceneTool.Spawn:
                level.playerSpawn = new Vector3(x, SupportAt(x, y) + 0.03f, 0f); Select(SelectedKind.Spawn, 0); break;
            case SceneTool.Goal:
                level.goalX = x; level.goalY = SupportAt(x, y); Select(SelectedKind.Goal, 0); break;
            case SceneTool.Checkpoint:
                level.checkpointX = x; level.checkpointY = SupportAt(x, y); Select(SelectedKind.Checkpoint, 0); break;
        }
        DirtyLevel();
        validationText = "Layout changed. Run Validate Layout to check it.";
        validationType = MessageType.Info;
        Repaint();
    }

    private float SupportAt(float x, float referenceY)
    {
        float bestBelow = float.NegativeInfinity;
        float closest = float.PositiveInfinity;
        float closestTop = 0f;
        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry p = level.platforms[i];
            if (x < p.x || x > p.x + p.width) continue;
            float distance = Mathf.Abs(referenceY - p.topY);
            if (distance < closest) { closest = distance; closestTop = p.topY; }
            if (p.topY <= referenceY + 0.15f && p.topY > bestBelow) bestBelow = p.topY;
        }
        if (!float.IsNegativeInfinity(bestBelow)) return bestBelow;
        return float.IsPositiveInfinity(closest) ? 0f : closestTop;
    }

    private float Snap(float value) => snapToGrid ? Mathf.Round(value / gridSize) * gridSize : value;

    private static bool TryMouseWorld(SceneView view, Vector2 mousePosition, out Vector3 world)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        Plane plane = new Plane(Vector3.forward, Vector3.zero);
        float distance;
        if (plane.Raycast(ray, out distance)) { world = ray.GetPoint(distance); world.z = 0f; return true; }
        world = Vector3.zero;
        return false;
    }

    private void Select(SelectedKind kind, int index)
    {
        selectedKind = kind;
        selectedIndex = index;
        Repaint();
        SceneView.RepaintAll();
    }

    private bool IsSelected(SelectedKind kind, int index) => selectedKind == kind && selectedIndex == index;

    private void DeleteSelected()
    {
        if (level == null || selectedKind == SelectedKind.None) return;
        Undo.RecordObject(level, "Delete level object");
        switch (selectedKind)
        {
            case SelectedKind.Platform: if (ValidIndex(level.platforms, selectedIndex)) level.platforms.RemoveAt(selectedIndex); break;
            case SelectedKind.Wall: if (ValidIndex(level.walls, selectedIndex)) level.walls.RemoveAt(selectedIndex); break;
            case SelectedKind.Enemy: if (ValidIndex(level.enemies, selectedIndex)) level.enemies.RemoveAt(selectedIndex); break;
            case SelectedKind.Pickup: if (ValidIndex(level.pickups, selectedIndex)) level.pickups.RemoveAt(selectedIndex); break;
            case SelectedKind.Spike:
                if (selectedIndex < level.spikes.Count && ValidIndex(level.spikes, selectedIndex)) level.spikes.RemoveAt(selectedIndex);
                else { int idx = selectedIndex - level.spikes.Count; if (ValidIndex(level.spikePositions, idx)) level.spikePositions.RemoveAt(idx); }
                break;
            case SelectedKind.Checkpoint: level.checkpointX = -999f; level.checkpointY = -999f; break;
            case SelectedKind.Spawn: EditorUtility.DisplayDialog("Wildrune", "The player spawn can't be deleted. Place or move it instead.", "OK"); break;
            case SelectedKind.Goal: EditorUtility.DisplayDialog("Wildrune", "The level goal can't be deleted. Move it instead.", "OK"); break;
        }
        selectedKind = SelectedKind.None;
        selectedIndex = -1;
        DirtyLevel();
    }

    private static bool ValidIndex<T>(IList<T> list, int index) => index >= 0 && index < list.Count;

    private void ValidateLayout()
    {
        if (level == null) return;
        List<string> issues = new List<string>();
        if (level.platforms.Count == 0) issues.Add("No platforms are defined.");
        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry p = level.platforms[i];
            if (p.width < 1f) issues.Add($"Platform {i} is narrower than 1 unit.");
            if (string.IsNullOrWhiteSpace(p.style) || !GetPlatformStyleKeys().Contains(p.style)) issues.Add($"Platform {i} uses unknown style '{p.style}'.");
            for (int j = i + 1; j < level.platforms.Count; j++)
            {
                LevelPlatformEntry q = level.platforms[j];
                bool sameHeight = Mathf.Abs(p.topY - q.topY) < 0.02f;
                bool overlap = Mathf.Min(p.x + p.width, q.x + q.width) - Mathf.Max(p.x, q.x) > 0.01f;
                if (sameHeight && overlap) issues.Add($"Platforms {i} and {j} overlap at the same height.");
            }
        }
        if (!IsSupported(level.playerSpawn.x, level.playerSpawn.y)) issues.Add("Player spawn has no platform beneath it.");
        float goalSupportY = level.goalY > -900f ? level.goalY : SupportAt(level.goalX, 0f);
        if (!IsSupported(level.goalX, goalSupportY + 0.03f)) issues.Add("Goal is not above a supporting platform.");
        for (int i = 0; i < level.walls.Count; i++)
        {
            if (level.walls[i].height < 2f || level.walls[i].width < 0.5f) issues.Add($"Wall {i} is too small to be useful.");
        }
        for (int i = 0; i < level.enemies.Count; i++)
        {
            LevelEnemyEntry e = level.enemies[i];
            if (e.patrolMax <= e.patrolMin) issues.Add($"Enemy {i} has invalid patrol bounds.");
            if (e.x < e.patrolMin || e.x > e.patrolMax) issues.Add($"Enemy {i} starts outside its patrol bounds.");
        }
        if (level.goalX <= level.playerSpawn.x) issues.Add("Goal is not to the right of the player spawn.");
        if (issues.Count == 0)
        {
            validationText = "No obvious layout issues found. Still play-test jumps, enemy patrols, and hitboxes in the game.";
            validationType = MessageType.Info;
        }
        else
        {
            validationText = string.Join("\n", issues.Take(8).Select(issue => "• " + issue));
            if (issues.Count > 8) validationText += $"\n• ...and {issues.Count - 8} more.";
            validationType = MessageType.Warning;
            Debug.LogWarning("Wildrune layout validation for " + level.name + ":\n" + string.Join("\n", issues));
        }
    }

    private bool HasPlatformAtX(float x)
    {
        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry p = level.platforms[i];
            if (x >= p.x - 0.1f && x <= p.x + p.width + 0.1f) return true;
        }
        return false;
    }

    private bool IsSupported(float x, float y)
    {
        for (int i = 0; i < level.platforms.Count; i++)
        {
            LevelPlatformEntry p = level.platforms[i];
            if (x >= p.x - 0.1f && x <= p.x + p.width + 0.1f && p.topY <= y + 0.2f && y - p.topY < 3f) return true;
        }
        return false;
    }

    private void FocusLevel()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null) { view = GetWindow<SceneView>(); }
        float right = level == null ? 20f : level.goalX;
        float maxY = level == null ? 7f : level.cameraCeiling;
        view.pivot = new Vector3(right * 0.5f, maxY * 0.48f, 0f);
        view.size = Mathf.Max(12f, Mathf.Max(right * 0.72f, maxY * 0.72f));
        view.orthographic = true;
        view.Repaint();
    }

    private void QuickTestSelectedLevel()
    {
        if (level == null) { EditorUtility.DisplayDialog("Wildrune", "Select a level first.", "OK"); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.SaveAssets();
        OpenMainScene();
        PlayerPrefs.SetInt("Wildrune_EditorQuickTestLevel", levelIndex);
        PlayerPrefs.SetInt("Wildrune_HardDifficulty", testHardDifficulty ? 1 : 0);
        PlayerPrefs.Save();
        EditorApplication.isPlaying = true;
    }

    private void PlayMainMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.SaveAssets();
        OpenMainScene();
        PlayerPrefs.DeleteKey("Wildrune_EditorQuickTestLevel");
        PlayerPrefs.Save();
        EditorApplication.isPlaying = true;
    }

    private void OpenMainScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath) == null)
        {
            EditorUtility.DisplayDialog("Wildrune", "Could not find " + MainScenePath, "OK");
            return;
        }
        if (EditorSceneManager.GetActiveScene().path != MainScenePath)
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
    }

    private void CreateTuningAsset()
    {
        GameTuning asset = CreateInstance<GameTuning>();
        AssetDatabase.CreateAsset(asset, TuningPath);
        AssetDatabase.SaveAssets();
        RefreshAssets();
    }

    private static Color StyleColor(string style)
    {
        switch (style)
        {
            case "ember": return new Color(0.95f, 0.35f, 0.2f);
            case "ice": return new Color(0.36f, 0.83f, 0.98f);
            case "canopy": return new Color(0.4f, 0.88f, 0.55f);
            case "ruins": return new Color(0.72f, 0.65f, 0.86f);
            case "stone": return new Color(0.68f, 0.7f, 0.74f);
            default: return new Color(0.5f, 0.88f, 0.37f);
        }
    }

    private static Color EnemyColor(EnemyKind kind)
    {
        switch (kind)
        {
            case EnemyKind.Slime: return new Color(0.4f, 0.95f, 0.42f);
            case EnemyKind.Archer: return new Color(1f, 0.66f, 0.32f);
            case EnemyKind.Bat: return new Color(0.72f, 0.57f, 0.98f);
            case EnemyKind.Brute: return new Color(0.95f, 0.35f, 0.28f);
            case EnemyKind.Boss: return new Color(1f, 0.25f, 0.55f);
            default: return new Color(0.95f, 0.82f, 0.35f);
        }
    }

    private void DirtyLevel()
    {
        if (level != null) EditorUtility.SetDirty(level);
        // Scene handles fire continuously while dragging. Debounce disk writes so the editor
        // remains responsive and only saves after the user pauses moving an object.
        lastEditTime = EditorApplication.timeSinceStartup;
        if (!saveScheduled)
        {
            saveScheduled = true;
            EditorApplication.update += FlushPendingLevelSave;
        }
        SceneView.RepaintAll();
        Repaint();
    }

    private void FlushPendingLevelSave()
    {
        if (!saveScheduled)
        {
            EditorApplication.update -= FlushPendingLevelSave;
            return;
        }
        if (EditorApplication.timeSinceStartup - lastEditTime < 0.8d) return;
        saveScheduled = false;
        EditorApplication.update -= FlushPendingLevelSave;
        if (level != null && EditorUtility.IsDirty(level)) AssetDatabase.SaveAssetIfDirty(level);
    }
}
#endif
