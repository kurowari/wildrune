#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>One-time-safe migration for new global tuning fields in existing Wildrune projects.</summary>
[InitializeOnLoad]
internal static class WildruneTuningMigration
{
    private const string AssetPath = "Assets/Resources/GameTuning.asset";

    static WildruneTuningMigration()
    {
        EditorApplication.delayCall += EnsureFields;
    }

    private static void EnsureFields()
    {
        GameTuning tuning = AssetDatabase.LoadAssetAtPath<GameTuning>(AssetPath);
        if (tuning == null) return;
        SerializedObject serialized = new SerializedObject(tuning);
        SerializedProperty schema = serialized.FindProperty("tuningSchemaVersion");
        if (schema == null || schema.intValue >= 1) return;
        bool changed = false;
        changed |= EnsureFloat(serialized, "playerColliderWidth", 0.60f);
        changed |= EnsureFloat(serialized, "playerColliderHeight", 1.22f);
        changed |= EnsureFloat(serialized, "playerVisualScale", 1f);
        changed |= EnsureFloat(serialized, "projectileColliderWidth", 0.98f);
        changed |= EnsureFloat(serialized, "projectileColliderHeight", 0.34f);
        changed |= EnsureFloat(serialized, "projectileHitRadius", 0.18f);
        changed |= EnsureFloat(serialized, "projectileVisualScale", 1f);
        changed |= EnsureFloat(serialized, "projectileSpeed", 13.5f);
        changed |= EnsureFloat(serialized, "platformColliderDepth", 1.06f);
        changed |= EnsureFloat(serialized, "platformColliderExtraWidth", 0.12f);
        changed |= EnsureFloat(serialized, "groundPlatformDefaultWidth", 8f);
        changed |= EnsureFloat(serialized, "skyPlatformDefaultWidth", 2.5f);
        changed |= EnsureFloat(serialized, "recommendedSkyPlatformRise", 2.2f);
        changed |= EnsureFloat(serialized, "recommendedSkyPlatformGap", 1.25f);
        schema.intValue = 1;
        changed = true;
        if (changed)
        {
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(tuning);
            AssetDatabase.SaveAssetIfDirty(tuning);
            Debug.Log("Wildrune: initialized newly added global tuning defaults.");
        }
    }

    private static bool EnsureFloat(SerializedObject obj, string fieldName, float fallback)
    {
        SerializedProperty property = obj.FindProperty(fieldName);
        if (property == null || property.propertyType != SerializedPropertyType.Float || property.floatValue > 0f) return false;
        property.floatValue = fallback;
        return true;
    }
}
#endif
