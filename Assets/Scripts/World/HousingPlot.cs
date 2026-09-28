using UnityEngine;

/// <summary>
/// Marks the player's housing plot (placed via the Foundation Shelter buildable). Setting one raises
/// the "housing_plot" flag — the future Hearthcraft building phase (interior walls, storage, defenses)
/// will hang off this anchor.
/// </summary>
public class HousingPlot : MonoBehaviour, IExaminable
{
    public string DisplayName => "Housing Plot";
    public string ExamineText => "The foundation of your wasteland home.";

    void Start() => PlayerEntity.Instance?.SetFlag("housing_plot");
}
