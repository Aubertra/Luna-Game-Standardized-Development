
# if UNITY_EDITOR

using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AirStack.Pathfinding
{
    public class MenuItems : EditorWindow
    {
        public Vector3 rootPostion = Vector3.zero;
        public Vector2Int mapSize = Vector2Int.one * 20;

        // 创建菜单选项
        [MenuItem("AirStack/Tool/AStar Pathfinding")]
        private static void AStar_Pathfinding()
        {
            // 创建或获取窗口实例，并设置标题
            var window = GetWindow<MenuItems>();
            window.titleContent = new GUIContent("AStar Pathfinding");
        }

        // 2. 构建UI：当窗口需要显示时，Unity会自动调用此方法
        private void CreateGUI()
        {
            // 导入自定义样式表
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/UI Toolkit/Style Sheet/Common.uss");
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
                Debug.Log("styleSheet导入成功");
            }
            else
            {
                Debug.LogError("styleSheet导入出错");
            }

            // 主题Logo
            var LogoLabel = new Label("AirStack");
            LogoLabel.AddToClassList("welcome-label");
            rootVisualElement.Add(LogoLabel);

            // 主题Logo
            var createMapLabel = new Label("创建地图");
            createMapLabel.AddToClassList("function-label");
            rootVisualElement.Add(createMapLabel);

            var separator = new VisualElement();
            separator.AddToClassList("dashed-divider"); // 虚线分隔线样式
            rootVisualElement.Add(separator);

            // 瓦片预制体
            var tileSizeField = new Vector3Field("瓦片预制体大小");
            tileSizeField.AddToClassList("custom-font");
            var filer = MapTool.tilePrefab.GetComponentInChildren<MeshFilter>();
            var xField = tileSizeField.Q<FloatField>("unity-x-input");
            var yField = tileSizeField.Q<FloatField>("unity-y-input");
            var zField = tileSizeField.Q<FloatField>("unity-z-input");

            if (xField != null) xField.isReadOnly = true;
            if (yField != null) yField.isReadOnly = true;
            if (zField != null) zField.isReadOnly = true;
            if (filer != null) tileSizeField.value = Vector3.Scale(filer.transform.localScale, filer.sharedMesh.bounds.size);
            else tileSizeField.value = Vector3.zero;

            var tileField = new ObjectField("瓦片预制体");
            tileField.objectType = typeof(GameObject);
            tileField.value = MapTool.tilePrefab;
            tileField.AddToClassList("custom-font");
            tileField.RegisterValueChangedCallback(evt =>
            {
                MapTool.tilePrefab = tileField.value as GameObject;
                filer = MapTool.tilePrefab.GetComponentInChildren<MeshFilter>();
                tileSizeField.value = Vector3.Scale(filer.transform.localScale, filer.sharedMesh.bounds.size);
            });

            rootVisualElement.Add(tileField);
            rootVisualElement.Add(tileSizeField);

            // 创建基准地点
            var rootPostionField = new Vector3Field("基准位置");
            rootPostionField.AddToClassList("custom-font");
            rootPostionField.value = rootPostion;
            rootPostionField.RegisterValueChangedCallback(evt =>
            {
                rootPostion = evt.newValue;
                //Debug.Log($"地图基准位置: {evt.newValue}");
            });
            rootVisualElement.Add(rootPostionField);

            // 创建地图大小
            var mapSizeField = new Vector2IntField("地图大小");
            mapSizeField.AddToClassList("custom-font");
            mapSizeField.value = mapSize;
            mapSizeField.RegisterValueChangedCallback(evt =>
            {
                mapSize = evt.newValue;
                //Debug.Log($"创建地图大小: {evt.newValue}");
            });
            rootVisualElement.Add(mapSizeField);

            var perTileSizeField = new FloatField("每个瓦片的大小缩放");
            perTileSizeField.AddToClassList("custom-font");
            perTileSizeField.value = MapTool.perTileSize;
            perTileSizeField.RegisterValueChangedCallback(evt =>
            {
                MapTool.perTileSize = evt.newValue;
                //Debug.Log($"创建瓷砖大小: {evt.newValue}");
            });
            rootVisualElement.Add(perTileSizeField);

            // 创建一个按钮控件
            var createButton = new Button(() =>
            {
                if (MapTool.CreateMap(rootPostion, mapSize))
                {
                    Debug.Log("创建地图成功！\n" +
                        $"地图基准位置: {rootPostion}\n" +
                        $"创建地图大小: {mapSize}\n" +
                        $"创建瓷砖大小: {MapTool.perTileSize}"
                        );
                }
            });
            createButton.AddToClassList("action-button");
            createButton.text = "确认";
            rootVisualElement.Add(createButton);

            // 创建一个按钮控件
            var displayButton = new Button(() =>
            {
                MapCache.DisplayMap();
            });
            displayButton.AddToClassList("action-button");
            displayButton.text = "展示地图信息";
            rootVisualElement.Add(displayButton);
        }
    }
}
#endif