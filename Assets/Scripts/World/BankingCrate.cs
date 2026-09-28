using System;
using UnityEngine;

public class BankingCrate : MonoBehaviour, IExaminable
{
    [SerializeField] public string label = "Abandoned Crate";
    [Header("Roxy's town bank guide")]
    public string townName;
    [TextArea] public string directions;
    public Sprite guideImage;
    public int guideOrder;

    public string DisplayName => label;
    public string ExamineText => "A battered storage crate. Stash your gear here for safekeeping.";

    // BankUI subscribes to this; the 3D interaction layer (Interactor3D) opens the bank directly
    // via BankUI.Open, so this is kept for any code that prefers the event-based open.
    public event Action<PlayerEntity> OnOpened;

    /// <summary>Open the bank for a player (fires OnOpened, which BankUI listens to).</summary>
    public void Open(PlayerEntity player) => OnOpened?.Invoke(player);
}
