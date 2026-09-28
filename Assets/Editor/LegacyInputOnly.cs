using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;

/// <summary>
/// The game reads input through the old Input Manager, and Android builds refuse the "Both" setting,
/// so Player Settings ▸ Active Input Handling is "Input Manager (Old)". The Input System package stays
/// installed (Coplay depends on it) and would ask on every editor launch to switch its backends back
/// on; answering Yes restarts Unity on "Both" and breaks phone builds again. While the project is on
/// the old Input Manager, this marks that question as already answered.
/// </summary>
[InitializeOnLoad]
static class LegacyInputOnly
{
    static LegacyInputOnly()
    {
        var playerSettings = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings"));
        if (playerSettings.FindProperty("activeInputHandler")?.intValue != 0) return;

        var inputSystem = System.Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
        if (inputSystem == null) return;
        RuntimeHelpers.RunClassConstructor(inputSystem.TypeHandle);
        var systemObject = inputSystem.GetField("s_SystemObject", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        systemObject?.GetType().GetField("newInputBackendsCheckedAsEnabled")?.SetValue(systemObject, true);
    }
}
