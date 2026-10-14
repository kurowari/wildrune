using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class LevelPlatformEntry
{
    [Tooltip("World-space left edge of this platform.")] public float x;
    [Tooltip("World-space height of the top surface, not the center.")] public float topY;
    [Min(1)] public float width = 4f;
    [Tooltip("meadow, ember, ice, canopy, ruins or stone.")] public string style = "meadow";
}

[Serializable]
public sealed class LevelEnemyEntry
{
    public float x;
    [Tooltip("Place the enemy feet at the supporting platform top (usually 0.03 for the floor).")]
    public float y = 0.03f;
    public EnemyKind kind = EnemyKind.Melee;
    public float patrolMin;
    public float patrolMax = 2f;
}

[Serializable]
public sealed class LevelPickupEntry
{
    public float x;
    public float y = 1f;
}

[Serializable]
public sealed class LevelWallEntry
{
    [Tooltip("Left edge of the wall in world units.")] public float x;
    [Tooltip("Bottom of the wall in world units.")] public float bottomY;
    [Min(1f)] public float height = 6f;
    [Min(0.5f)] public float width = 1f;
    [Tooltip("Art style used for the wall tiles: stone, canopy, ruins, etc.")] public string style = "stone";
}

/// <summary>
/// Inspector-editable level layout. Assets are stored at Resources/Levels/Level_01 ... Level_05.
/// Move/resize platforms and place enemies/pickups here; no gameplay code changes are required.
/// </summary>
[CreateAssetMenu(menuName = "Wildrune/Level Layout", fileName = "Level_01")]
public sealed class LevelDefinition : ScriptableObject
{
    public string title = "New Frontier";
    public string biome = "meadow";
    [TextArea] public string description = "A new adventure.";
    public Vector3 playerSpawn = new Vector3(1.2f, 0.03f, 0f);
    public float goalX = 40f;
    [Tooltip("Optional platform top height for elevated/vertical exits. Leave at -999 to find the ground under goalX.")] public float goalY = -999f;
    public float cameraCeiling = 8f;
    [Tooltip("Optional platform top height for an elevated checkpoint. Leave at -999 to find the ground under checkpointX.")] public float checkpointY = -999f;
    public bool requireBossDefeated = false;
    public List<LevelPlatformEntry> platforms = new List<LevelPlatformEntry>();
    public List<LevelWallEntry> walls = new List<LevelWallEntry>();
    public List<LevelEnemyEntry> enemies = new List<LevelEnemyEntry>();
    [Tooltip("Legacy ground spikes, defined by X only.")] public List<float> spikes = new List<float>();
    [Tooltip("Explicit spikes defined as (X, supporting platform top Y), for elevated paths.")] public List<Vector2> spikePositions = new List<Vector2>();
    public List<LevelPickupEntry> pickups = new List<LevelPickupEntry>();
    public float checkpointX = -999f;

    public void CopyFrom(LevelBuilder.LevelInfo source)
    {
        title = source.title;
        biome = source.biome;
        description = source.description;
        playerSpawn = source.playerSpawn;
        goalX = source.goalX;
        goalY = source.goalY;
        cameraCeiling = source.cameraCeiling;
        checkpointX = source.checkpointX;
        checkpointY = source.checkpointY;
        requireBossDefeated = source.requireBossDefeated;
        platforms.Clear();
        walls.Clear();
        foreach (LevelBuilder.PlatformSpec p in source.platforms)
            platforms.Add(new LevelPlatformEntry { x = p.x, topY = p.y, width = p.width, style = p.style });
        enemies.Clear();
        foreach (LevelBuilder.EnemySpec e in source.enemies)
            enemies.Add(new LevelEnemyEntry { x = e.x, y = e.y, kind = e.kind, patrolMin = e.minX, patrolMax = e.maxX });
        spikes = new List<float>(source.spikes);
        spikePositions = new List<Vector2>(source.spikePositions);
        pickups.Clear();
        foreach (LevelBuilder.PickupSpec p in source.pickups)
            pickups.Add(new LevelPickupEntry { x = p.x, y = p.y });
    }

    public LevelBuilder.LevelInfo ToRuntimeInfo()
    {
        LevelBuilder.LevelInfo result = new LevelBuilder.LevelInfo
        {
            title = title,
            biome = biome,
            description = description,
            playerSpawn = playerSpawn,
            goalX = goalX,
            goalY = goalY,
            cameraCeiling = cameraCeiling,
            checkpointX = checkpointX,
            checkpointY = checkpointY,
            requireBossDefeated = requireBossDefeated
        };
        foreach (LevelPlatformEntry p in platforms)
            result.platforms.Add(new LevelBuilder.PlatformSpec(p.x, p.topY, Mathf.Max(1f, p.width), p.style));
        foreach (LevelWallEntry w in walls)
            result.walls.Add(new LevelBuilder.WallSpec(w.x, w.bottomY, Mathf.Max(1f, w.height), Mathf.Max(0.5f, w.width), w.style));
        foreach (LevelEnemyEntry e in enemies)
            result.enemies.Add(new LevelBuilder.EnemySpec(e.x, e.y, e.kind, Mathf.Min(e.patrolMin, e.patrolMax), Mathf.Max(e.patrolMin, e.patrolMax)));
        result.spikes.AddRange(spikes);
        result.spikePositions.AddRange(spikePositions);
        foreach (LevelPickupEntry p in pickups)
            result.pickups.Add(new LevelBuilder.PickupSpec(p.x, p.y));
        return result;
    }
}
