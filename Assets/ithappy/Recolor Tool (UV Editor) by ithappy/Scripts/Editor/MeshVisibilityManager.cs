using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ithappy.RecolorToolWindow
{
    public class MeshVisibilityManager
    {
        private Dictionary<int, bool> _visibility = new Dictionary<int, bool>();
        private List<string> _meshNames = new List<string>();

        public void Initialize(List<MeshResolver.MeshComponentInfo> meshComponents)
        {
            _visibility.Clear();
            _meshNames.Clear();

            for (int i = 0; i < meshComponents.Count; i++)
            {
                _visibility[i] = true;
                _meshNames.Add(meshComponents[i].DisplayName);
            }
        }

        public bool IsVisible(int index)
        {
            return _visibility.ContainsKey(index) && _visibility[index];
        }

        public void ToggleVisibility(int index)
        {
            if (_visibility.ContainsKey(index))
                _visibility[index] = !_visibility[index];
        }

        public void ShowAll()
        {
            var keys = new List<int>(_visibility.Keys);
            foreach (var key in keys)
                _visibility[key] = true;
        }

        public void ShowGenericMenu(System.Action onChanged)
        {
            GenericMenu menu = new GenericMenu();

            foreach (var kvp in _visibility)
            {
                int index = kvp.Key;
                bool visible = kvp.Value;
                string name = index < _meshNames.Count ? _meshNames[index] : $"Mesh {index}";

                menu.AddItem(new GUIContent(name), visible, () =>
                {
                    ToggleVisibility(index);
                    onChanged?.Invoke();
                });
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Show All"), false, () =>
            {
                ShowAll();
                onChanged?.Invoke();
            });

            menu.ShowAsContext();
        }

        public void Clear()
        {
            _visibility.Clear();
            _meshNames.Clear();
        }
    }
}
