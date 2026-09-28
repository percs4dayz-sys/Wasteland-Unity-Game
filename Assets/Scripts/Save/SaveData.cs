using System;
using System.Collections.Generic;
using UnityEngine;

// JsonUtility-compatible: no Dictionary fields
[Serializable]
public class SkillEntry { public string skill; public int xp; }

[Serializable]
public class SlotEntry { public int itemId; public int qty; public int[] modules; }

[Serializable]
public class EquipEntry { public string slot; public int itemId; public int[] modules; }

[Serializable]
public class SaveData
{
    public string playerName;
    public int tileX;
    public int tileY;
    public List<SkillEntry> skillXP = new();
    public List<SlotEntry> inventory = new();
    public List<SlotEntry> bank = new();
    public List<EquipEntry> equipment = new();
    public int ammoQty;
    public List<string> flags = new();
    public float posX, posY, posZ;   // 3D world position (tileX/Y above are 2D-only)
    public bool  hasPos3D;           // true when posX/Y/Z were captured in the 3D world
    public string sceneName;         // scene the player was in when saved, so Continue reloads it

    public static SaveData Capture(PlayerEntity player)
    {
        var data = new SaveData
        {
            playerName = player.PlayerName,
        };

        var wpos = player.transform.position;
        data.posX = wpos.x; data.posY = wpos.y; data.posZ = wpos.z;
        data.hasPos3D = GameMode.Is3D;
        data.sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

        foreach (Skill skill in Enum.GetValues(typeof(Skill)))
            data.skillXP.Add(new SkillEntry { skill = skill.ToString(), xp = player.Stats.GetXP(skill) });

        for (int i = 0; i < Inventory.MAX; i++)   // include saddlebag overflow slots
        {
            var s = player.Inventory.GetSlot(i);
            data.inventory.Add(s != null ? new SlotEntry { itemId = s.itemId, qty = s.quantity, modules = s.Copy().modules } : new SlotEntry());
        }

        for (int i = 0; i < Bank.SIZE; i++)
        {
            var s = player.Bank.GetSlot(i);
            data.bank.Add(s != null ? new SlotEntry { itemId = s.itemId, qty = s.quantity, modules = s.Copy().modules } : new SlotEntry());
        }

        foreach (var kvp in player.Equipment.GetAll())
            data.equipment.Add(new EquipEntry { slot = kvp.Key, itemId = kvp.Value, modules = player.Equipment.GetModules(kvp.Key) });
        data.ammoQty = player.Equipment.AmmoQuantity;

        foreach (var flag in player.GetAllFlags())
            data.flags.Add(flag);

        return data;
    }

    public void ApplyTo(PlayerEntity player)
    {
        player.PlayerName = playerName;

        // Restore the saved 3D world position. Disable the CharacterController briefly —
        // it resists direct transform moves.
        if (hasPos3D)
        {
            var saved = new Vector3(posX, posY, posZ);

            // A saved position is only honoured if it's a real place. Saving while stuck outside the
            // world (or a save written before a terrain rebuild moved the ground) would otherwise
            // restore you straight back into the void every single load — the stuck state becomes
            // permanent and survives restarts. Reject it and let PlayerRespawn use the authored
            // spawn point instead.
            if (WorldBounds.IsOutside(saved))
            {
                Debug.LogWarning($"[Save] Saved position {saved} is outside the world " +
                                 $"({WorldBounds.Describe()}). Ignoring it — you'll start at the spawn point.");
                player.GetComponent<PlayerRespawn>()?.ReturnToSpawn();
            }
            else
            {
                var cc = player.GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                player.transform.position = saved;
                if (cc != null) cc.enabled = true;
            }
        }

        foreach (var entry in skillXP)
        {
            // Migrate old skill names so existing saves keep their XP after a rename.
            string name = entry.skill switch
            {
                "Salvaging"    => "Woodcutting",
                "Scavenging"   => "Fishing",
                "Excavation"   => "Scrapping",
                // The 2026-08-06 pass to plain OSRS names. The invented names were unmemorable;
                // where the job matched an OSRS skill it now uses that name.
                "Melee"        => "Attack",       // wrong-direction rename of Bladework, then →Attack
                "Bladework"    => "Attack",
                "Brutality"    => "Strength",
                "Hardening"    => "Defence",
                "Sustenance"   => "Cooking",
                // Smithing absorbed BOTH old production skills: Hearthcraft (smelting) and Tinkering
                // (forging). A save may carry XP in either; whichever loads LAST wins, so a character
                // that trained both keeps only the higher-indexed one's XP. Acceptable — they were
                // always the same underlying job.
                "Hearthcraft"  => "Smithing",
                "Tinkering"    => "Smithing",
                _              => entry.skill
            };
            if (Enum.TryParse<Skill>(name, out var skill))
                player.Stats.SetXP(skill, entry.xp);
        }

        // Slot-exact restore: preserves the player's arrangement, and items saved in
        // saddlebag slots survive even though the companion isn't summoned at load.
        // An item that's since been removed from the game is dropped rather than left as a broken slot.
        player.Inventory.Clear();
        for (int i = 0; i < inventory.Count && i < Inventory.MAX; i++)
        {
            var s = inventory[i];
            player.Inventory.SetSlotRaw(i, (s != null && s.itemId > 0 && s.qty > 0 && ItemRegistry.Get(s.itemId) != null)
                ? new ItemStack(s.itemId, s.qty) { modules = s.modules == null ? null : (int[])s.modules.Clone() } : null);
        }

        player.Bank.Clear();
        foreach (var s in bank)
            if (s != null && s.itemId > 0 && ItemRegistry.Get(s.itemId) != null) player.Bank.Deposit(new ItemStack(s.itemId, s.qty) { modules = s.modules });

        // Missing module arrays in older saves mean empty sockets.
        player.Equipment.Clear();
        foreach (var e in equipment)
        {
            if (e.itemId <= 0) continue;
            var item = ItemRegistry.Get(e.itemId);
            if (item == null) continue;
            if (e.slot == "Ammo") player.Equipment.EquipAmmo(e.itemId, ammoQty);
            else player.Equipment.Equip(item, e.modules);
        }

        player.ClearFlags();
        foreach (var flag in flags)
            player.SetFlag(flag);
    }
}
