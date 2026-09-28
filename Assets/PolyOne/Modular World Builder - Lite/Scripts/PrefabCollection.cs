using UnityEngine;
using System.Collections.Generic;

namespace PolyOne.ModularWorldBuilder
{
    [CreateAssetMenu(
        fileName = "PrefabCollection",
        menuName = "PolyOne/Prefab Collection")]
    public class PrefabCollection : ScriptableObject
    {
        public List<GameObject> prefabs = new();
    }
}