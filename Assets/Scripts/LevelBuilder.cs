using System;
using System.Collections.Generic;
using UnityEngine;

public static class LevelBuilder
{
    [Serializable]
    public sealed class LevelInfo
    {
        public string title;
        public string biome;
        public string description;
        public Vector3 playerSpawn;
        public float goalX;
        public float goalY = -999f;
        public float cameraCeiling;
        public float checkpointY = -999f;
        public bool requireBossDefeated;
        public List<PlatformSpec> platforms = new List<PlatformSpec>();
        public List<WallSpec> walls = new List<WallSpec>();
        public List<EnemySpec> enemies = new List<EnemySpec>();
        public List<float> spikes = new List<float>();
        public List<Vector2> spikePositions = new List<Vector2>();
        public List<PickupSpec> pickups = new List<PickupSpec>();
        public float checkpointX = -999f;
    }

    [Serializable]
    public sealed class WallSpec
    {
        public float x, bottomY, height, width;
        public string style;
        public WallSpec(float x, float bottomY, float height, float width, string style)
        { this.x = x; this.bottomY = bottomY; this.height = height; this.width = width; this.style = style; }
    }

    [Serializable]
    public sealed class PlatformSpec
    {
        public float x, y, width;
        public string style;
        public PlatformSpec(float x, float y, float width, string style)
        { this.x = x; this.y = y; this.width = width; this.style = style; }
    }

    [Serializable]
    public sealed class EnemySpec
    {
        public float x, y;
        public EnemyKind kind;
        public float minX, maxX;
        public EnemySpec(float x, float y, EnemyKind kind, float minX, float maxX)
        { this.x = x; this.y = y; this.kind = kind; this.minX = minX; this.maxX = maxX; }
    }

    [Serializable]
    public sealed class PickupSpec
    {
        public float x, y;
        public PickupSpec(float x, float y) { this.x = x; this.y = y; }
    }

    private static readonly string[] Titles =
    {
        "Greenward Path", "Cinderkeep", "Frostbite Crossing", "Moonleaf Canopy", "The Broken Crown"
    };

    public static string GetTitle(int levelIndex)
    {
        levelIndex = Mathf.Clamp(levelIndex, 0, 4);
        LevelDefinition definition = Resources.Load<LevelDefinition>("Levels/Level_0" + (levelIndex + 1));
        return definition != null && !string.IsNullOrEmpty(definition.title) ? definition.title : Titles[levelIndex];
    }

    /// <summary>Uses Inspector-editable layout assets when present, with code defaults as a safe fallback.</summary>
    public static LevelInfo GetInfo(int index)
    {
        index = Mathf.Clamp(index, 0, 4);
        LevelDefinition definition = Resources.Load<LevelDefinition>("Levels/Level_0" + (index + 1));
        return definition != null ? definition.ToRuntimeInfo() : GetDefaultInfo(index);
    }

    public static LevelInfo GetDefaultInfo(int index)
    {
        switch (Mathf.Clamp(index, 0, 4))
        {
            case 0: return MeadowLevel();
            case 1: return EmberLevel();
            case 2: return IceLevel();
            case 3: return CanopyLevel();
            default: return FinalLevel();
        }
    }

    public static PlayerController Build(GameBootstrap owner, Transform root, int levelIndex)
    {
        LevelInfo info = GetInfo(levelIndex);
        BuildBackground(root, info);
        for (int i = 0; i < info.walls.Count; i++) BuildWall(root, info.walls[i]);
        for (int i = 0; i < info.platforms.Count; i++) BuildPlatform(root, info.platforms[i]);
        for (int i = 0; i < info.spikes.Count; i++) BuildSpikes(root, info.spikes[i], FindGroundTop(info, info.spikes[i]));
        for (int i = 0; i < info.spikePositions.Count; i++) BuildSpikes(root, info.spikePositions[i].x, info.spikePositions[i].y);
        if (info.checkpointX > -900f)
        {
            float checkpointTop = info.checkpointY > -900f ? info.checkpointY : FindGroundTop(info, info.checkpointX);
            BuildCheckpoint(root, info.checkpointX, checkpointTop);
        }
        for (int i = 0; i < info.pickups.Count; i++) BuildPickup(root, info.pickups[i]);

        float goalTop = info.goalY > -900f ? info.goalY : FindGroundTop(info, info.goalX);
        BuildGoal(root, info.goalX, goalTop, info.requireBossDefeated);
        PlayerController player = BuildPlayer(root, info.playerSpawn);
        for (int i = 0; i < info.enemies.Count; i++) BuildEnemy(root, info.enemies[i]);
        return player;
    }

    private static void BuildBackground(Transform root, LevelInfo info)
    {
        Sprite sprite = GameArt.Get("bg_" + info.biome);
        if (sprite == null) return;
        float width = sprite.bounds.size.x;
        float height = sprite.bounds.size.y;
        float left = -5f;
        float right = info.goalX + 8f;
        float worldWidth = Mathf.Max(20f, right - left);

        // The vertical ascent gets one continuous sky behind the whole climb. The biome
        // panorama is kept low, around the grassy starting area, rather than repeating hills
        // every few metres all the way to the summit.
        if (info.cameraCeiling > 14f)
        {
            float bottom = -4f;
            float top = info.cameraCeiling + 8f; // Leave room above the camera's highest view.
            float worldHeight = top - bottom;
            Sprite skySprite = GameArt.Get("bg_vertical_sky");
            if (skySprite != null)
            {
                GameObject sky = new GameObject("Vertical Blue Sky");
                sky.transform.SetParent(root, false);
                sky.transform.position = new Vector3((left + right) * 0.5f, (bottom + top) * 0.5f, 5f);
                sky.transform.localScale = new Vector3(worldWidth / skySprite.bounds.size.x,
                    worldHeight / skySprite.bounds.size.y, 1f);
                SpriteRenderer skyRenderer = sky.AddComponent<SpriteRenderer>();
                skyRenderer.sprite = skySprite;
                skyRenderer.sortingOrder = -110;
            }

            const float lowerBandHeight = 8.5f;
            GameObject lowerBand = new GameObject("Canopy Lowland Panorama");
            lowerBand.transform.SetParent(root, false);
            lowerBand.transform.position = new Vector3((left + right) * 0.5f,
                bottom + lowerBandHeight * 0.5f, 5f);
            lowerBand.transform.localScale = new Vector3(worldWidth / width, lowerBandHeight / height, 1f);
            SpriteRenderer lowerRenderer = lowerBand.AddComponent<SpriteRenderer>();
            lowerRenderer.sprite = sprite;
            lowerRenderer.sortingOrder = -100; // Above the sky, only in the grassy lowland.
            // The authored canopy panorama already contains its own sky/forest palette.
            // Do not apply the green biome tint here: it darkens the lower sky versus the
            // untinted vertical sky sprite and creates a visible horizontal seam.
            return;
        }

        GameObject go = new GameObject("Biome Panorama");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3((left + right) * 0.5f, 4.5f, 5f);
        go.transform.localScale = new Vector3(worldWidth / width, 17.5f / height, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = -100;
        go.AddComponent<BiomeTint>().SetBiome(info.biome);
    }

    private static readonly Dictionary<string, PhysicsMaterial2D> platformMaterials = new Dictionary<string, PhysicsMaterial2D>();
    private static PhysicsMaterial2D playerNoFrictionMaterial;

    private static PhysicsMaterial2D GetPlayerMaterial()
    {
        if (playerNoFrictionMaterial == null)
            playerNoFrictionMaterial = new PhysicsMaterial2D("Wildrune Player - No Friction") { friction = 0f, bounciness = 0f };
        return playerNoFrictionMaterial;
    }

    private static PlatformTypeProfile GetPlatformProfile(string style)
    {
        PlatformDefaults defaults = PlatformDefaults.Current;
        return defaults != null ? defaults.Find(style) : null;
    }

    private static PhysicsMaterial2D GetPlatformMaterial(string style)
    {
        PlatformTypeProfile profile = GetPlatformProfile(style);
        float friction = profile != null ? profile.friction : (style == "ice" ? 0f : 0.12f);
        float bounce = profile != null ? profile.bounciness : 0f;
        string key = style + "_" + friction.ToString("F3") + "_" + bounce.ToString("F3");
        if (!platformMaterials.TryGetValue(key, out PhysicsMaterial2D material) || material == null)
        {
            material = new PhysicsMaterial2D("Wildrune - " + style) { friction = friction, bounciness = bounce };
            platformMaterials[key] = material;
        }
        return material;
    }

    private static void BuildWall(Transform root, WallSpec spec)
    {
        GameObject wall = new GameObject("Wall - " + spec.style);
        wall.transform.SetParent(root, false);
        wall.layer = LayerMask.NameToLayer("Ground");
        wall.transform.position = new Vector3(spec.x, spec.bottomY, 0f);
        BoxCollider2D collider = wall.AddComponent<BoxCollider2D>();
        collider.offset = new Vector2(spec.width * 0.5f, spec.height * 0.5f);
        collider.size = new Vector2(spec.width, spec.height);
        collider.sharedMaterial = GetPlatformMaterial(spec.style);

        // A wall is rendered as one full-face sprite rather than a stack of repeated platform
        // tiles. This keeps the masonry continuous across the entire wall and the collider unchanged.
        Sprite art = GameArt.Get("wall_stone");
        if (art == null) return;

        GameObject face = new GameObject("Continuous Stone Wall Face");
        face.transform.SetParent(wall.transform, false);
        face.transform.localPosition = new Vector3(spec.width * 0.5f, spec.height * 0.5f, 0.05f);
        face.transform.localScale = new Vector3(spec.width / art.bounds.size.x,
            spec.height / art.bounds.size.y, 1f);
        SpriteRenderer sr = face.AddComponent<SpriteRenderer>();
        sr.sprite = art;
        // Ruins lean a little warmer; ordinary stone stays neutral slate-grey.
        sr.color = string.Equals(spec.style, "ruins", StringComparison.OrdinalIgnoreCase)
            ? new Color32(244, 228, 209, 255) : Color.white;
        sr.sortingOrder = -7;
    }

    private static string TileForStyle(string style)
    {
        switch (style)
        {
            case "ember": return "tile_ember";
            case "ice": return "tile_ice";
            case "stone": return "tile_stone";
            case "canopy": return "tile_canopy";
            case "ruins": return "tile_ruins";
            case "snow": return "tile_snow";
            default: return "tile_grass";
        }
    }

    private static void BuildPlatform(Transform root, PlatformSpec spec)
    {
        // The collider and the visible tile row now occupy the same one-block footprint.
        // Slightly wider side faces prevent tiny seams at platform edges without filling jumps/gaps.
        GameObject platform = new GameObject("Platform - " + spec.style);
        platform.transform.SetParent(root, false);
        platform.layer = LayerMask.NameToLayer("Ground");
        GameTuning tuning = GameTuning.Current;
        float colliderDepth = tuning != null ? Mathf.Max(0.25f, tuning.platformColliderDepth) : 1.06f;
        float extraWidth = tuning != null ? Mathf.Max(0f, tuning.platformColliderExtraWidth) : 0.12f;
        platform.transform.position = new Vector3(spec.x + spec.width * 0.5f, spec.y - colliderDepth * 0.5f, 0f);
        BoxCollider2D collider = platform.AddComponent<BoxCollider2D>();
        // Global defaults control collision depth below the top and total overhang, while the
        // upper face remains aligned with the visible one-row tile art.
        collider.size = new Vector2(spec.width + extraWidth, colliderDepth);
        collider.sharedMaterial = GetPlatformMaterial(spec.style);
        PlatformSurface surface = platform.AddComponent<PlatformSurface>();
        PlatformTypeProfile profile = GetPlatformProfile(spec.style);
        surface.biome = spec.style;
        surface.slippery = profile != null ? profile.slippery : spec.style == "ice";

        string tile = profile != null && !string.IsNullOrEmpty(profile.fallbackSpriteKey) ? profile.fallbackSpriteKey : TileForStyle(spec.style);
        Sprite art = profile != null && profile.tileSprite != null ? profile.tileSprite : GameArt.Get(tile);
        // A single row of full square tiles: there is no invisible dirt stack below the collider.
        BuildTileRow(root, spec.x, spec.y - 0.5f, spec.width, art, -8, profile != null ? profile.tint : Color.white);
    }

    private static void BuildTileRow(Transform root, float x, float y, float width, Sprite sprite, int order, Color tint)
    {
        if (sprite == null) return;
        float end = x + width;
        int count = Mathf.CeilToInt(width);
        for (int i = 0; i < count; i++)
        {
            float tileWidth = Mathf.Min(1f, end - (x + i));
            if (tileWidth <= 0f) continue;
            GameObject tile = new GameObject("Pixel Tile");
            tile.transform.SetParent(root, false);
            tile.transform.position = new Vector3(x + i + tileWidth * 0.5f, y, 0.04f);
            tile.transform.localScale = new Vector3(tileWidth, 1f, 1f);
            SpriteRenderer sr = tile.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = tint;
            sr.sortingOrder = order;
        }
    }

    // The explicit-height overload is used by level construction so hazards rest on a known floor.
    private static void BuildSpikes(Transform root, float x, float top)
    {
        GameObject go = new GameObject("Cracked Spikes");
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(x, top + 0.14f, -0.02f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("hazard_spikes");
        sr.sortingOrder = 2;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.78f, 0.57f);
        collider.isTrigger = true;
        Hazard hazard = go.AddComponent<Hazard>();
        hazard.damage = 1;
        go.layer = LayerMask.NameToLayer("Hazard");
    }

    private static float FindGroundTop(LevelInfo info, float x)
    {
        float top = 0f;
        float bestDistance = 999f;
        for (int i = 0; i < info.platforms.Count; i++)
        {
            PlatformSpec p = info.platforms[i];
            if (x >= p.x - 0.01f && x <= p.x + p.width + 0.01f && p.y < bestDistance)
            {
                bestDistance = p.y;
                top = p.y;
            }
        }
        return top;
    }

    private static void BuildCheckpoint(Transform root, float x, float groundTop)
    {
        GameObject marker = new GameObject("Checkpoint Flag");
        marker.transform.SetParent(root, false);
        marker.transform.position = new Vector3(x, groundTop + 0.9f, -0.01f);
        SpriteRenderer sr = marker.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("checkpoint");
        sr.sortingOrder = 4;
        BoxCollider2D trigger = marker.AddComponent<BoxCollider2D>();
        trigger.size = new Vector2(1.1f, 1.8f);
        trigger.isTrigger = true;
        marker.AddComponent<Checkpoint>();
    }

    private static void BuildPickup(Transform root, PickupSpec pickup)
    {
        GameObject item = new GameObject("Rune Shard");
        item.transform.SetParent(root, false);
        item.transform.position = new Vector3(pickup.x, pickup.y, -0.02f);
        SpriteRenderer sr = item.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("pickup_shard");
        sr.sortingOrder = 7;
        CircleCollider2D trigger = item.AddComponent<CircleCollider2D>();
        trigger.radius = 0.38f;
        trigger.isTrigger = true;
        item.AddComponent<CoinPickup>();
    }

    private static void BuildGoal(Transform root, float x, float groundTop, bool requireBossDefeated)
    {
        GameObject gate = new GameObject("Frontier Gate");
        gate.transform.SetParent(root, false);
        gate.transform.position = new Vector3(x, groundTop + 1.5f, -0.01f);
        SpriteRenderer sr = gate.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("goal_gate");
        sr.sortingOrder = 3;
        BoxCollider2D trigger = gate.AddComponent<BoxCollider2D>();
        trigger.size = new Vector2(1.45f, 2.65f);
        trigger.isTrigger = true;
        GoalGate goal = gate.AddComponent<GoalGate>();
        goal.requiresBossDefeated = requireBossDefeated;
    }

    private static PlayerController BuildPlayer(Transform root, Vector3 position)
    {
        GameObject go = new GameObject("The Adventurer");
        go.transform.SetParent(root, false);
        go.transform.position = position;
        go.layer = LayerMask.NameToLayer("Player");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GameArt.Get("player_idle_0");
        sr.sortingOrder = 10;
        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = 3.6f;
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.60f, 1.22f);
        collider.offset = new Vector2(0f, 0.61f);
        collider.sharedMaterial = GetPlayerMaterial();
        PlayerController controller = go.AddComponent<PlayerController>();
        controller.ApplyTuning(GameTuning.Current);
        return controller;
    }

    private static void BuildEnemy(Transform root, EnemySpec spec)
    {
        GameObject go = new GameObject("Enemy - " + spec.kind);
        go.transform.SetParent(root, false);
        go.transform.position = new Vector3(spec.x, spec.y, 0f);
        go.layer = LayerMask.NameToLayer("Enemy");
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        // EnemyController.Configure assigns the default sprite from EnemyDefaults.asset.
        sr.sortingOrder = 5;
        Rigidbody2D body = go.AddComponent<Rigidbody2D>();
        body.gravityScale = spec.kind == EnemyKind.Bat ? 0f : 3.2f;
        body.freezeRotation = true;
        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.68f, 0.80f);
        collider.offset = new Vector2(0f, 0.40f);
        EnemyController controller = go.AddComponent<EnemyController>();
        controller.Configure(spec.kind, spec.minX, spec.maxX);
    }

    private static LevelInfo MeadowLevel()
    {
        LevelInfo l = new LevelInfo { title = Titles[0], biome = "meadow", description = "A gentle trail for learning the ropes", playerSpawn = new Vector3(1.3f, 0.03f, 0f), goalX = 49f, cameraCeiling = 7.4f };
        l.platforms.AddRange(new[] {
            new PlatformSpec(-4,0,14,"meadow"), new PlatformSpec(12,0,8,"meadow"),
            new PlatformSpec(22.5f,0,8.5f,"meadow"), new PlatformSpec(33.5f,0,7.5f,"meadow"),
            new PlatformSpec(43.5f,0,10.5f,"meadow"), new PlatformSpec(14,1.7f,3,"meadow"),
            new PlatformSpec(25,1.65f,3,"meadow"), new PlatformSpec(45,1.6f,3,"meadow")
        });
        l.enemies.AddRange(new[] {
            new EnemySpec(6.0f,0.03f,EnemyKind.Slime,4.2f,7.3f),
            new EnemySpec(16f,1.73f,EnemyKind.Slime,14.3f,17.7f),
            new EnemySpec(23.5f,0.03f,EnemyKind.Melee,21f,28.3f),
            new EnemySpec(36.2f,0.03f,EnemyKind.Slime,34.5f,39f),
            new EnemySpec(48f,0.03f,EnemyKind.Melee,45f,52f)
        });
        l.pickups.AddRange(new[] { new PickupSpec(15.5f,3.0f), new PickupSpec(27f,3.0f), new PickupSpec(38f,1.2f) });
        l.spikes.Add(35.5f);
        return l;
    }

    private static LevelInfo EmberLevel()
    {
        LevelInfo l = new LevelInfo { title = Titles[1], biome = "ember", description = "Ash bridges, hot cracks, and goblin archers", playerSpawn = new Vector3(1.2f,0.03f,0f), goalX = 52f, cameraCeiling = 8.6f };
        l.platforms.AddRange(new[] {
            new PlatformSpec(-4,0,13,"ember"), new PlatformSpec(11,0,8,"ember"),
            new PlatformSpec(21,0,9,"ember"), new PlatformSpec(32,0,8,"ember"),
            new PlatformSpec(42,0,14,"ember"), new PlatformSpec(8,1.55f,3,"stone"),
            new PlatformSpec(18,1.9f,3,"stone"), new PlatformSpec(25,2.15f,3,"stone"),
            new PlatformSpec(34,1.9f,3,"stone"), new PlatformSpec(45,2.15f,3,"stone")
        });
        l.enemies.AddRange(new[] {
            new EnemySpec(6.2f,0.03f,EnemyKind.Melee,3f,8f),
            new EnemySpec(19.5f,1.93f,EnemyKind.Archer,18.2f,20.8f),
            new EnemySpec(24.4f,0.03f,EnemyKind.Slime,22f,28.7f),
            new EnemySpec(37f,0.03f,EnemyKind.Archer,33f,41f),
            new EnemySpec(47f,0.03f,EnemyKind.Brute,44f,51f)
        });
        l.spikes.AddRange(new[] { 12.6f, 27.5f, 39.5f, 46.0f });
        l.pickups.AddRange(new[] { new PickupSpec(9.5f,3.3f), new PickupSpec(29f,1.2f), new PickupSpec(44.5f,1.0f) });
        return l;
    }

    private static LevelInfo IceLevel()
    {
        LevelInfo l = new LevelInfo { title = Titles[2], biome = "ice", description = "Low-friction ice rewards momentum and careful jumps", playerSpawn = new Vector3(1.2f,0.03f,0f), goalX = 52.5f, cameraCeiling = 9.0f };
        l.platforms.AddRange(new[] {
            new PlatformSpec(-4,0,12,"ice"), new PlatformSpec(10,0,8,"ice"),
            new PlatformSpec(21,0,7,"ice"), new PlatformSpec(30,0,8,"ice"),
            new PlatformSpec(40,0,7,"ice"), new PlatformSpec(49,0,8,"ice"),
            new PlatformSpec(7.5f,1.65f,2.5f,"ice"), new PlatformSpec(17.5f,2.05f,3,"ice"),
            new PlatformSpec(25,1.6f,3,"ice"), new PlatformSpec(34,2.1f,3,"ice"),
            new PlatformSpec(44,1.65f,3,"ice")
        });
        l.enemies.AddRange(new[] {
            new EnemySpec(6f,0.03f,EnemyKind.Slime,3.5f,7.5f),
            new EnemySpec(15f,0.03f,EnemyKind.Bat,12f,17f),
            new EnemySpec(23.7f,0.03f,EnemyKind.Brute,21.5f,27f),
            new EnemySpec(35.5f,2.13f,EnemyKind.Archer,34.2f,36.8f),
            new EnemySpec(43f,0.03f,EnemyKind.Bat,40.5f,47f),
            new EnemySpec(50f,0.03f,EnemyKind.Melee,48.5f,54.5f)
        });
        l.spikes.AddRange(new[] { 13.0f, 27.6f, 37.0f, 45.0f });
        l.pickups.AddRange(new[] { new PickupSpec(9.0f,2.2f), new PickupSpec(28f,1.3f), new PickupSpec(42.8f,3.2f) });
        l.checkpointX = 30.8f;
        return l;
    }

    private static LevelInfo CanopyLevel()
    {
        LevelInfo l = new LevelInfo { title = Titles[3], biome = "canopy", description = "Vine paths, narrow ledges, and airborne hunters", playerSpawn = new Vector3(1.2f,0.03f,0f), goalX = 57f, cameraCeiling = 10.0f };
        l.platforms.AddRange(new[] {
            new PlatformSpec(-4,0,13,"canopy"), new PlatformSpec(11,0,8,"canopy"),
            new PlatformSpec(21,0,8.5f,"canopy"), new PlatformSpec(31.5f,0,8,"canopy"),
            new PlatformSpec(42,0,7.5f,"canopy"), new PlatformSpec(52,0,10,"canopy"),
            new PlatformSpec(8,1.9f,3,"canopy"), new PlatformSpec(16,2.15f,3,"canopy"),
            new PlatformSpec(22,1.8f,3,"canopy"), new PlatformSpec(29,2.15f,3,"canopy"),
            new PlatformSpec(38,2.3f,3,"canopy"), new PlatformSpec(49,2.0f,3,"canopy"),
            new PlatformSpec(26.2f,3.8f,2.2f,"canopy")
        });
        l.enemies.AddRange(new[] {
            new EnemySpec(7f,0.03f,EnemyKind.Melee,4f,10f),
            new EnemySpec(17.5f,2.18f,EnemyKind.Archer,16.2f,18.8f),
            new EnemySpec(21f,0.03f,EnemyKind.Bat,17f,23f),
            new EnemySpec(30.3f,2.18f,EnemyKind.Archer,29.2f,31.5f),
            new EnemySpec(36f,0.03f,EnemyKind.Brute,34.5f,42.5f),
            new EnemySpec(43f,0.03f,EnemyKind.Bat,40f,47f),
            new EnemySpec(51f,2.03f,EnemyKind.Melee,49.5f,52.5f)
        });
        l.spikes.AddRange(new[] { 26.5f, 39.0f, 48.0f });
        l.pickups.AddRange(new[] { new PickupSpec(17.5f,2.5f), new PickupSpec(31f,3.2f), new PickupSpec(27.4f,4.55f), new PickupSpec(46.5f,2.2f) });
        l.checkpointX = 34.8f;
        return l;
    }

    private static LevelInfo FinalLevel()
    {
        LevelInfo l = new LevelInfo { title = Titles[4], biome = "ruins", description = "Every trick matters. Break the Crown and reach the gate.", playerSpawn = new Vector3(1.2f,0.03f,0f), goalX = 58.2f, cameraCeiling = 10.0f };
        l.platforms.AddRange(new[] {
            new PlatformSpec(-4,0,11,"meadow"), new PlatformSpec(9,0,7,"meadow"),
            new PlatformSpec(18,0,6,"ice"), new PlatformSpec(26,0,6,"ember"),
            new PlatformSpec(34,0,6,"ruins"), new PlatformSpec(42,0,6,"ice"),
            new PlatformSpec(50,0,10,"ruins"), new PlatformSpec(10,1.6f,3,"meadow"),
            new PlatformSpec(18,2.2f,3,"ice"), new PlatformSpec(26,1.9f,3,"ember"),
            new PlatformSpec(34,2.2f,3,"ruins"), new PlatformSpec(42,1.9f,3,"ice"),
            new PlatformSpec(49,2.2f,3,"ruins"), new PlatformSpec(53,2.2f,3,"ruins")
        });
        l.enemies.AddRange(new[] {
            new EnemySpec(5.2f,0.03f,EnemyKind.Slime,2.5f,6.5f),
            new EnemySpec(13f,0.03f,EnemyKind.Archer,11f,14f),
            new EnemySpec(21f,0.03f,EnemyKind.Bat,17.5f,23f),
            new EnemySpec(24.8f,0.03f,EnemyKind.Melee,23.8f,30.5f),
            new EnemySpec(35f,2.23f,EnemyKind.Archer,33f,36f),
            new EnemySpec(44f,0.03f,EnemyKind.Brute,41f,47f),
            new EnemySpec(53f,0.03f,EnemyKind.Boss,50.5f,57.0f),
            new EnemySpec(55.1f,2.23f,EnemyKind.Archer,54.0f,56.2f)
        });
        l.spikes.AddRange(new[] { 8.5f, 19.8f, 30.5f, 38.0f, 46.3f, 55.8f });
        l.pickups.AddRange(new[] { new PickupSpec(11.5f,2.8f), new PickupSpec(28f,1.3f), new PickupSpec(37.7f,2.3f), new PickupSpec(47f,1.3f) });
        l.checkpointX = 31.5f;
        return l;
    }
}

