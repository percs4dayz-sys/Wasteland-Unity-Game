using System.Collections;
using UnityEngine;

/// <summary>One-time provisions for this separate world save; never runs in the original world.</summary>
public class BlackwaterArrival : MonoBehaviour
{
    IEnumerator Start()
    {
        yield return null; yield return null; // SaveManager restores the player first.
        var player=PlayerEntity.Instance;
        if(player==null || player.HasFlag("blackwater_arrived"))yield break;
        player.SetFlag("blackwater_arrived");
        foreach(int id in new[]{3,4,5})player.Inventory.Add(id);
        HUDController.Emit("Welcome to Hearthwick. Your gathering tools are in your bag. Bank and workshops are on the square; press F8 for the Blackwater atlas.");
    }
}
