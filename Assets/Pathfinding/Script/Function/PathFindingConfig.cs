#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace AirStack.Pathfinding
{
    public enum MeshScaleType
    {
        Square,
        Hexagonal
    }

    [CreateAssetMenu(fileName = "PathFindingConfig", menuName = "AirStack/PathFinding/Config")]
    public class PathFindingConfig : ScriptableObject
    {
        [Header("移动设置")]
        [SerializeField]private bool costDecideSpeed = false;
        [SerializeField] private bool allowOccupyTile = false;

        [Header("调试")]
        [SerializeField] private bool debugMode = false;

        [Header("网格设置")]
        [SerializeField] private MeshScaleType createMeshScale = MeshScaleType.Hexagonal;

        // 单例访问器
        private static PathFindingConfig instance;
        public static PathFindingConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<PathFindingConfig>("PathFindingConfig");

                    // 如果 Resources 中没有，自动创建
                    if (instance == null)
                    {
                        instance = CreateDefaultConfig();
                    }
                }

                return instance;
            }
        }

        private static PathFindingConfig CreateDefaultConfig()
        {
#if UNITY_EDITOR
            // 编辑器模式：创建资源文件
            if (!Application.isPlaying)
            {
                // 确保 Resources 文件夹存在
                string resourcesPath = Application.dataPath + "/Resources";
                if (!System.IO.Directory.Exists(resourcesPath))
                {
                    System.IO.Directory.CreateDirectory(resourcesPath);
                    AssetDatabase.Refresh();
                }

                // 创建 ScriptableObject 资源
                PathFindingConfig config = CreateInstance<PathFindingConfig>();
                AssetDatabase.CreateAsset(config, "Assets/Resources/PathFindingConfig.asset");
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                Debug.Log("自动创建了 PathFindingConfig 配置文件");
                return config;
            }
#endif
            // 运行时：创建临时实例（不会保存到磁盘）
            PathFindingConfig runtimeInstance = ScriptableObject.CreateInstance<PathFindingConfig>();
            Debug.LogWarning("PathFindingConfig 资源未找到，使用运行时默认配置");
            return runtimeInstance;
        }

        // 便捷静态属性（保持旧 API 兼容）
        public static bool CostDecideSpeed
        {
            get => Instance.costDecideSpeed;
            set => Instance.costDecideSpeed = value;
        }

        public static bool AllowOccupyTile
        {
            get => Instance.allowOccupyTile;
            set => Instance.allowOccupyTile = value;
        }

        public static bool DEBUG_MODE
        {
            get => Instance.debugMode;
            set => Instance.debugMode = value;
        }

        public static MeshScaleType CreateMeshScale
        {
            get => Instance.createMeshScale;
            set => Instance.createMeshScale = value;
        }
    }
}
