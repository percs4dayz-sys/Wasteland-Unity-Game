using UnityEngine;

public static class ItemIconLoader
{
    /// <summary>Prefer dedicated artwork, then reuse the existing family/tier artwork.</summary>
    public static Sprite LoadIcon(int id)
    {
        var sprite = Resources.Load<Sprite>($"ItemIcons/{id}");
        if (sprite != null) return sprite;
        var module = ModuleCatalog.Get(id);
        if (module != null)
        {
            var quality = ModuleCatalog.Grade(id) == 2 ? TierModules.Quality.Refined : TierModules.Quality.Common;
            return Resources.Load<Sprite>($"ItemIcons/{TierModules.IdFor(module.Tier, quality)}");
        }
        if (GatheringTools.IsUpgrade(id))
            return Resources.Load<Sprite>($"ItemIcons/{GatheringTools.BaseTool(id)}");
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void InitializeIcons()
    {
        int loadedCount = 0;
        int missingCount = 0;

        foreach (var item in ItemRegistry.All)
        {
            var sprite = LoadIcon(item.id);
            if (sprite != null) item.icon = sprite;
            if (item.icon != null)
            {
                loadedCount++;
            }
            else
            {
                missingCount++;
            }
        }

        Debug.Log($"[ItemIconLoader] Dynamic icon binding complete! Loaded: {loadedCount}, Missing: {missingCount}");
    }
}
