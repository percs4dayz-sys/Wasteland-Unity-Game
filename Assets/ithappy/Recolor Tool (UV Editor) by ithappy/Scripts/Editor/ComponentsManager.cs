using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ithappy.RecolorToolWindow
{
    public class ComponentsManager
    {
        private readonly Color SELECTION_RECT_COLOR = new Color(0f, .3f, 1f, 0.5f);
        private readonly Color SELECTION_BACK_COLOR = new Color(0f, .3f, 1f, 0.2f);
        
        public Color DefaultVertexColor { get; set; } = new Color(1f, 1f, 1f, 1f);
        public Color DefaultBlackVertexColor { get; set; } = new Color(0f, 0f, 0f, 1f);
        public Color SelectedVertexColor { get; set; } = new Color(1f, 1f, 1f, 1f);
        public Color SelectedBlackVertexColor { get; set; } = new Color(0f, 0f, 0f, 1f);
        
        private Texture2D _gridTexture;
        private Texture2D _cacheUsedFillTexture;
        private Vector2 _cacheUsedFillTextureOffset;
        private Vector2 _cacheUsedFillTextureScale;
        private Texture2D _rotateGizmoSprite;
        
        public VisualElement RootLayout { get; private set; }
        public VisualElement Root { get; private set; }
        public ObjectField ObjectField { get; private set; }
        public DropdownField SubMeshField { get; private set; }
        public DropdownField UVField { get; private set; }
        public DropdownField MeshComponentField { get; private set; }
        public Button CenterButton { get; private set; }
        public Button VisibilityButton { get; private set; }
        public Button ColorButton { get; private set; }
        public Button FocusButton { get; private set; }
        public Button ApplyButton { get; private set; }
        public Button TestAssetButton { get; private set; }
        public Button RectSelectButton { get; private set; }
        public Button LassoSelectButton { get; private set; }
        public Button AssetViewerButton { get; private set; }
        public Button SupportButton { get; private set; }
        public Button AllAssetsButton { get; private set; }
        public Button CopyButton { get; private set; }
        public Button PasteButton { get; private set; }
        public Button MasterMaterialButton { get; private set; }
        public VisualElement Canvas { get; private set; }
        public VisualElement GridContainer { get; private set; }
        public VisualElement TextureContainer { get; private set; }
        public LineDrawer VertexContainer { get; private set; }
        public LineDrawer SelectionRectContainer { get; private set; }

        public List<LineDrawer> UVLinesContainers { get; private set; } = new List<LineDrawer>();
        public Color UVLineColor { get; private set; } = Color.white;
        public Color CurrentDefaultVertexColor { get; private set; }
        public Color CurrentSelectedVertexColor { get; private set; }
        public LineDrawer CurrentUVLinesContainer;
        public Texture2D CheckerTexture;
        public LineDrawer LassoContainer { get; private set; }
        public Texture2D RotateGizmoSprite => _rotateGizmoSprite;

        public ComponentsManager(VisualElement root)
        {
            Root = root;
            FindComponents();
            CurrentDefaultVertexColor = DefaultVertexColor;
            CurrentSelectedVertexColor = SelectedVertexColor;
        }

        private void FindComponents()
        {
            var layout = LoadAssetController.GetLayout();
            RootLayout = layout.Instantiate();

            RootLayout.style.flexGrow = 1;
            Root.Add(RootLayout);

            CheckerTexture = LoadAssetController.GetCheckerTexture();

            ObjectField = RootLayout.Q<ObjectField>(name: "ObjectField");
            ObjectField.objectType = typeof(GameObject);
            ObjectField.allowSceneObjects = false;

            SubMeshField = RootLayout.Q<DropdownField>(name: "SubMeshField");
            UVField = RootLayout.Q<DropdownField>(name: "UVField");
            MeshComponentField = RootLayout.Q<DropdownField>(name: "SubPrefabsField");

            CenterButton = RootLayout.Q<Button>(name: "CenterButton");
            ColorButton = RootLayout.Q<Button>(name: "ColorButton");
            FocusButton = RootLayout.Q<Button>(name: "FocusButton");
            RectSelectButton = RootLayout.Q<Button>(name: "RectSelectButton");
            LassoSelectButton = RootLayout.Q<Button>(name: "LassoSelectButton");
            AssetViewerButton = RootLayout.Q<Button>(name: "AssetViewerButton");
            SupportButton = RootLayout.Q<Button>(name: "SupportButton");
            AllAssetsButton = RootLayout.Q<Button>(name: "AllAssetsButton");
            ApplyButton = RootLayout.Q<Button>(name: "SaveButton");
            TestAssetButton = RootLayout.Q<Button>(name: "TestAssetButton");
            CopyButton = RootLayout.Q<Button>(name: "CopyButton");
            PasteButton = RootLayout.Q<Button>(name: "PasteButton");
            VisibilityButton = RootLayout.Q<Button>(name: "VisibilityButton");
            MasterMaterialButton = RootLayout.Q<Button>(name: "MasterMaterialButton");
            MasterMaterialButton.tooltip = "Replace materials with a palette-based master material";

            Canvas = RootLayout.Q(name: "Canvas");

            TextureContainer = new VisualElement();
            TextureContainer.name = "TextureContainer";
            TextureContainer.style.position = Position.Absolute;
            Canvas.Add(TextureContainer);

            GridContainer = new VisualElement();
            GridContainer.name = "GridContainer";
            GridContainer.style.position = Position.Absolute;
            GridContainer.style.backgroundColor = new Color(0f, 0f, 0f, 0f);
            Canvas.Add(GridContainer);

            _gridTexture = LoadAssetController.GetGridTexture();
            if (_gridTexture != null)
            {
                GridContainer.style.backgroundImage = _gridTexture;
            }

            VertexContainer = new LineDrawer();
            VertexContainer.pickingMode = PickingMode.Ignore;
            VertexContainer.name = "VertexContainer";
            VertexContainer.style.position = Position.Absolute;
            VertexContainer.style.display = DisplayStyle.Flex;
            Canvas.Add(VertexContainer);

            SelectionRectContainer = new LineDrawer();
            SelectionRectContainer.pickingMode = PickingMode.Ignore;
            SelectionRectContainer.name = "SelectionRectContainer";
            SelectionRectContainer.style.position = Position.Absolute;
            SelectionRectContainer.style.display = DisplayStyle.Flex;
            Canvas.Add(SelectionRectContainer);

            AddUVLinesContainer();
            
            LassoContainer = new LineDrawer();
            LassoContainer.pickingMode = PickingMode.Ignore;
            LassoContainer.name = "LassoContainer";
            LassoContainer.style.position = Position.Absolute;
            LassoContainer.LineColor = SELECTION_RECT_COLOR;
            LassoContainer.style.display = DisplayStyle.None;
            Canvas.Add(LassoContainer);
            
            _rotateGizmoSprite = LoadAssetController.LoadAsset<Texture2D>("Texture2D", "RotateGizmoIcon");
        }

        public LineDrawer AddUVLinesContainer(VisualElement sibling = null)
        {
            var uvLinesContainer = new LineDrawer();
            uvLinesContainer.name = "UVLinesContainer";
            uvLinesContainer.style.position = Position.Absolute;
            uvLinesContainer.InvertVertical = true;
            uvLinesContainer.LineColor = UVLineColor;
            uvLinesContainer.LineWidth = 1f;
            UVLinesContainers.Add(uvLinesContainer);

            CurrentUVLinesContainer = uvLinesContainer;

            Canvas.Add(uvLinesContainer);
            if (sibling != null)
            {
                uvLinesContainer.PlaceInFront(sibling);
            }

            return uvLinesContainer;
        }

        public LineDrawer GetOrCreateUVLinesContainer(int index)
        {
            while (index >= UVLinesContainers.Count)
            {
                AddUVLinesContainer();
            }

            return UVLinesContainers[index];
        }

        public void ChangeLinesColor()
        {
            if (UVLineColor == Color.white)
            {
                UVLineColor = Color.black;
                CurrentDefaultVertexColor = DefaultBlackVertexColor;
                CurrentSelectedVertexColor = SelectedBlackVertexColor;
            }
            else
            {
                UVLineColor = Color.white;
                CurrentDefaultVertexColor = DefaultVertexColor;
                CurrentSelectedVertexColor = SelectedVertexColor;
            }
        }

        public void GetCanvasSize(float zoom, out float size)
        {
            float margin = 25;

            float maxSize = Mathf.Min(Canvas.resolvedStyle.width - margin * 2f, Canvas.resolvedStyle.height - margin * 2f);
            size = maxSize * zoom;
        }

        public void RefreshTextureContainer(Texture2D texture, Material material, Vector2 offset, Vector2 scale)
        {
            if (!NeedsUpdate(texture, offset, scale))
                return;

            UpdateCache(texture, offset, scale);
            TextureContainer.Clear();

            if (IsSimpleMode(scale, offset))
            {
                SetupSimpleTexture(texture, material);
            }
            else
            {
                SetupTiledTexture(texture, material, offset, scale);
            }
        }

        private bool NeedsUpdate(Texture2D texture, Vector2 offset, Vector2 scale)
        {
            return _cacheUsedFillTexture != texture
                   || _cacheUsedFillTextureOffset != offset
                   || _cacheUsedFillTextureScale != scale;
        }

        private void UpdateCache(Texture2D texture, Vector2 offset, Vector2 scale)
        {
            _cacheUsedFillTexture = texture;
            _cacheUsedFillTextureOffset = offset;
            _cacheUsedFillTextureScale = scale;
        }

        private bool IsSimpleMode(Vector2 scale, Vector2 offset)
        {
            return scale == Vector2.one && offset == Vector2.zero;
        }

        private void SetupSimpleTexture(Texture2D texture, Material material)
        {
            TextureContainer.style.flexWrap = Wrap.NoWrap;
            TextureContainer.style.flexDirection = FlexDirection.Row;
            TextureContainer.style.backgroundImage = GetBackgroundTexture(texture);
            TextureContainer.style.unityBackgroundImageTintColor = GetTintColor(texture, material);
    
#if UNITY_6000_0_OR_NEWER
    TextureContainer.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
#else
            TextureContainer.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
#endif
        }

        private VisualElement CreateTile(Texture2D texture, Color tint, float widthPercent)
        {
            var tile = new VisualElement
            {
                style =
                {
                    backgroundImage = texture,
                    unityBackgroundImageTintColor = tint,
                    width = new Length(widthPercent, LengthUnit.Percent),
                    height = new Length(100, LengthUnit.Percent)
                }
            };
    
#if UNITY_6000_0_OR_NEWER
    tile.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Cover);
#else
            tile.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
#endif
    
            return tile;
        }

        private void SetupTiledTexture(Texture2D texture, Material material, Vector2 offset, Vector2 scale)
        {
            TextureContainer.style.flexWrap = Wrap.NoWrap;
            TextureContainer.style.flexDirection = FlexDirection.ColumnReverse;
            TextureContainer.style.backgroundImage = StyleKeyword.None;

            var tiles = CalculateTilesCount(scale, offset);
            float absScaleX = Mathf.Abs(scale.x);
            float absScaleY = Mathf.Abs(scale.y);
            float tileWidthPercent = 100f / absScaleX;
            float tileHeightPercent = 100f / absScaleY;

            Vector2 normalizedOffset = NormalizeOffset(offset);

            float rowLeftPercent = (-normalizedOffset.x * 100f) / absScaleX;
            float rowBottomPercent = (-normalizedOffset.y * 100f) / absScaleY;

            Color tileTint = GetTintColor(texture, material);
            Texture2D backgroundTexture = GetBackgroundTexture(texture);

            for (int y = 0; y < tiles.y; y++)
            {
                var row = CreateRow(tileWidthPercent * tiles.x, tileHeightPercent,
                    rowLeftPercent, rowBottomPercent);

                for (int x = 0; x < tiles.x; x++)
                {
                    var tile = CreateTile(backgroundTexture, tileTint, tileWidthPercent);
                    row.Add(tile);
                }

                TextureContainer.Add(row);
            }
        }

        private Vector2Int CalculateTilesCount(Vector2 scale, Vector2 offset)
        {
            int extraTile = offset != Vector2.zero ? 1 : 0;
            return new Vector2Int(
                Mathf.CeilToInt(Mathf.Abs(scale.x)) + extraTile,
                Mathf.CeilToInt(Mathf.Abs(scale.y)) + extraTile
            );
        }

        private Vector2 NormalizeOffset(Vector2 offset)
        {
            return new Vector2(
                offset.x < 0f ? offset.x + 1f : offset.x,
                offset.y < 0f ? offset.y + 1f : offset.y
            );
        }

        private VisualElement CreateRow(float widthPercent, float heightPercent,
            float leftPercent, float bottomPercent)
        {
            return new VisualElement
            {
                style =
                {
                    flexWrap = Wrap.NoWrap,
                    flexDirection = FlexDirection.Row,
                    flexGrow = 0f,
                    flexShrink = 0f,
                    position = Position.Relative,
                    height = new Length(heightPercent, LengthUnit.Percent),
                    width = new Length(widthPercent, LengthUnit.Percent),
                    left = new Length(leftPercent, LengthUnit.Percent),
                    bottom = new Length(bottomPercent, LengthUnit.Percent)
                }
            };
        }

        private Texture2D GetBackgroundTexture(Texture2D texture)
        {
            return texture ?? CheckerTexture;
        }

        private Color GetTintColor(Texture2D texture, Material material)
        {
            if (texture == null && material != null)
            {
                var col = material.color;
                col.a = 0.65f;
                return col;
            }

            return Color.white;
        }

        public void UpdateTextureAndGrid(float size, Vector2 anchor, Texture2D texture, Material material)
        {
            if (Canvas == null || TextureContainer == null) return;

            TextureContainer.style.width = size;
            TextureContainer.style.height = size;
            TextureContainer.style.left = (Canvas.resolvedStyle.width - size) / 2f + anchor.x;
            TextureContainer.style.bottom = (Canvas.resolvedStyle.height - size) / 2f - anchor.y;

            var offset = material?.GetMainTextureOffset() ?? Vector2.zero;
            var scale = material?.GetMainTextureScale() ?? Vector2.one;
            RefreshTextureContainer(texture, material, offset, scale);

            UpdateGridPosition();
        }
        
        public void ClearTextureCache()
        {
            _cacheUsedFillTexture = null;
        }

        public void UpdateGridPosition()
        {
            if (GridContainer == null || TextureContainer == null) return;

            float textureWidth = TextureContainer.style.width.value.value;
            float textureHeight = TextureContainer.style.height.value.value;
            float textureLeft = TextureContainer.style.left.value.value;
            float textureBottom = TextureContainer.style.bottom.value.value;

            float gridMarginWidth = textureWidth * 0.12f;
            float gridMarginHeight = textureHeight * 0.12f;

            GridContainer.style.left = textureLeft - gridMarginWidth;
            GridContainer.style.bottom = textureBottom - gridMarginHeight;
            GridContainer.style.width = textureWidth * 1.2f;
            GridContainer.style.height = textureHeight * 1.2f;
        }

        public void UpdateUVLinesContainers()
        {
            if (TextureContainer == null) return;

            foreach (var container in UVLinesContainers)
            {
                container.style.display = DisplayStyle.Flex;
                container.style.width = TextureContainer.style.width;
                container.style.height = TextureContainer.style.height;
                container.style.left = TextureContainer.style.left;
                container.style.bottom = TextureContainer.style.bottom;
                container.InvertVertical = true;
                container.LineColor = UVLineColor;
                container.LineWidth = 1f;
            }
        }

        public void ClearAllUVLines()
        {
            foreach (var container in UVLinesContainers)
            {
                container.ClearVertices();
            }
        }

        public void HideAllUVLines()
        {
            foreach (var container in UVLinesContainers)
            {
                container.style.display = DisplayStyle.None;
            }
        }

        public void PrepareVertexContainer(float size, float zoom)
        {
            if (VertexContainer == null || TextureContainer == null) return;

            VertexContainer.style.display = DisplayStyle.Flex;
            VertexContainer.style.width = TextureContainer.style.width;
            VertexContainer.style.height = TextureContainer.style.height;
            VertexContainer.style.left = TextureContainer.style.left;
            VertexContainer.style.bottom = TextureContainer.style.bottom;
            VertexContainer.InvertVertical = true;
            VertexContainer.LineWidth = (0.015f * size) / zoom;
            VertexContainer.ClearVertices();
        }

        public void HideVertexContainer()
        {
            if (VertexContainer == null) return;

            VertexContainer.style.display = DisplayStyle.None;
            VertexContainer.ClearVertices();
        }

        public void UpdateSelectionRect(Vector2 start, Vector2 end, float size, float zoom)
        {
            if (SelectionRectContainer == null || TextureContainer == null) return;

            if (Vector2.Distance(start, end) <= 0f)
            {
                SelectionRectContainer.style.display = DisplayStyle.None;
                return;
            }

            SelectionRectContainer.style.display = DisplayStyle.Flex;
            SelectionRectContainer.style.width = TextureContainer.style.width;
            SelectionRectContainer.style.height = TextureContainer.style.height;
            SelectionRectContainer.style.left = TextureContainer.style.left;
            SelectionRectContainer.style.bottom = TextureContainer.style.bottom;
            SelectionRectContainer.InvertVertical = true;
            SelectionRectContainer.LineWidth = (0.004f * size) / zoom;
            SelectionRectContainer.LineColor = SELECTION_RECT_COLOR;

            SelectionRectContainer.ClearVertices();
            
            SelectionRectContainer.StartNewLine();
            SelectionRectContainer.AddVertex(start * size);
            SelectionRectContainer.AddVertex(new Vector2(end.x, start.y) * size);
            SelectionRectContainer.AddVertex(end * size);
            SelectionRectContainer.AddVertex(new Vector2(start.x, end.y) * size, closeLoop: true);
            
            SelectionRectContainer.StartNewLine();
            SelectionRectContainer.LineWidth = Mathf.Abs(end.y - start.y) * size;
            float midY = start.y + (end.y - start.y) * 0.5f;
            SelectionRectContainer.AddVertex(new Vector2(start.x, midY) * size, SELECTION_BACK_COLOR);
            SelectionRectContainer.AddVertex(new Vector2(end.x, midY) * size, SELECTION_BACK_COLOR);

            SelectionRectContainer.MarkDirtyRepaint();
        }
        
        public void SetupUVLinesContainer(LineDrawer container, Color lineColor)
        {
            container.style.display = DisplayStyle.Flex;
            container.style.width = TextureContainer.style.width;
            container.style.height = TextureContainer.style.height;
            container.style.left = TextureContainer.style.left;
            container.style.bottom = TextureContainer.style.bottom;
            container.InvertVertical = true;
            container.LineColor = lineColor;
            container.LineWidth = 1f;
        }
        
        public void SetSelectionModeActive(RecolorToolWindow.SelectionMode mode)
        {
            var normalOpacity = 1f;
            var activeOpacity = 0.6f;
    
            if (mode == RecolorToolWindow.SelectionMode.Rect)
            {
                RectSelectButton.style.opacity = activeOpacity;
                RectSelectButton.SetEnabled(false);
                
                LassoSelectButton.style.opacity = normalOpacity;
                LassoSelectButton.SetEnabled(true);
            }
            else
            {
                LassoSelectButton.style.opacity = activeOpacity;
                LassoSelectButton.SetEnabled(false);
                
                RectSelectButton.style.opacity = normalOpacity;
                RectSelectButton.SetEnabled(true);
            }
        }
    }
}