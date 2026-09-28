using System.Collections.Generic;
using UnityEngine;

namespace ithappy.RecolorToolWindow
{
    public class CustomUndoManager
    {
        private Stack<Dictionary<int, Vector2>> _undoStack = new Stack<Dictionary<int, Vector2>>();
        private Stack<Dictionary<int, Vector2>> _redoStack = new Stack<Dictionary<int, Vector2>>();
        
        private UVEditorMeshData _meshData;
        private const int MAX_UNDO_STEPS = 50;
        
        public CustomUndoManager(UVEditorMeshData meshData)
        {
            _meshData = meshData;
        }
        
        public void RecordState()
        {
            var currentState = CaptureCurrentState();
            _undoStack.Push(currentState);
            _redoStack.Clear();
            
            while (_undoStack.Count > MAX_UNDO_STEPS)
            {
                var tempStack = new Stack<Dictionary<int, Vector2>>();
                for (int i = 0; i < MAX_UNDO_STEPS - 1; i++)
                {
                    if (_undoStack.Count > 0)
                        tempStack.Push(_undoStack.Pop());
                }
                _undoStack.Clear();
                while (tempStack.Count > 0)
                    _undoStack.Push(tempStack.Pop());
            }
        }
        
        private Dictionary<int, Vector2> CaptureCurrentState()
        {
            var state = new Dictionary<int, Vector2>();
            var uvs = _meshData.CurrentUV;
            
            if (uvs == null) return state;
            
            for (int i = 0; i < uvs.Count; i++)
            {
                state[i] = uvs[i];
            }
            
            return state;
        }
        
        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            
            var currentState = CaptureCurrentState();
            _redoStack.Push(currentState);
            
            var undoState = _undoStack.Pop();
            RestoreState(undoState);
        }
        
        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            
            var currentState = CaptureCurrentState();
            _undoStack.Push(currentState);
            
            var redoState = _redoStack.Pop();
            RestoreState(redoState);
        }
        
        private void RestoreState(Dictionary<int, Vector2> state)
        {
            var uvs = _meshData.CurrentUV;
            if (uvs == null) return;
            
            foreach (var kvp in state)
            {
                if (kvp.Key < uvs.Count)
                {
                    uvs[kvp.Key] = kvp.Value;
                }
            }
            
            _meshData.SaveTempChanges();
            _meshData.UVIsDirty = true;
            _meshData.VerticesMoved = true;
        }
        
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
        }
    }
}