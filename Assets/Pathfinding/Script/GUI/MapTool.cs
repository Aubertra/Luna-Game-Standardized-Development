
# if UNITY_EDITOR

using UnityEditor;

#endif
using UnityEngine;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System;
using static UnityEngine.GraphicsBuffer;

namespace AirStack.Pathfinding
{
    public static class MapTool
    {
#if UNITY_EDITOR
        public static GameObject tilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pathfinding/Model/Internal Use/Hexagonal_Tiles.prefab");
        public static bool needClearOldMap = true;
        public static Transform mapRoot;
        public static float perTileSize = 1f;

        private static Vector3 tilePrefabSize;
        private static float horizontalSpacing;
        private static float verticalSpacing;

        public static bool CreateMap(Vector3 rootPostion, Vector2Int size)
        {
            if (tilePrefab == null)
            {
                Debug.LogError("tilePrefab is null");
                return false;
            }
            else
            {
                var filer = tilePrefab.GetComponentInChildren<MeshFilter>();
                tilePrefabSize = Vector3.Scale(filer.transform.localScale, filer.sharedMesh.bounds.size);
            }

            if (MapCache.InitLunaMap()) mapRoot = MapCache.mapPrefab.transform;

            if (!mapRoot)
            {
                mapRoot = new GameObject("Map").transform;
                mapRoot.position = rootPostion;
            }
            else if (needClearOldMap)
            {
                //非运行状态下删除物体
                GameObject.DestroyImmediate(mapRoot.gameObject);
                MapCache.ClearMap();
                mapRoot = new GameObject("Map").transform;
                mapRoot.position = rootPostion;
            }

            CalculateSpacing();

            for (int i = 0; i < size.x; i++)
            {
                for (int j = 0; j < size.y; j++)
                {
                    var obj = (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab);
                    obj.name = $"({i},{j})";
                    obj.transform.SetParent(mapRoot.transform);
                    obj.transform.localScale = Vector3.one * perTileSize;
                    obj.transform.localPosition = GetHexPosition(i, j);
                    obj.AddComponent<Tile>().InitTile(AStarLogic.EncodeKey(i, j));
                }
            }

            MapCache.mapPrefab = PrefabUtility.SaveAsPrefabAsset(mapRoot.gameObject, "Assets/AStar Pathfinding/Model/Internal Use/GeneratedMap.prefab");

            return true;
        }

        private static void CalculateSpacing()
        {
            // 正六边形的间距计算
            horizontalSpacing = tilePrefabSize.x * 1.05f * perTileSize;
            verticalSpacing = tilePrefabSize.y * 0.8f * perTileSize;
        }

        private static Vector3 GetHexPosition(int x, int y)
        {
            float posX = x * horizontalSpacing;
            float posY = y * verticalSpacing;

            // 奇数行偏移半个间距，形成蜂窝状排列
            if ((y & 1) == 1)
            {
                posX += horizontalSpacing / 2f;
            }

            return new Vector3(posX, 0, posY);
        }
#endif

        private static List<Renderer> cacheRender = new List<Renderer>();


        /// <summary>
        /// 展示A*瓷砖路径
        /// </summary>
        /// <param name="path">A*瓷砖路径列表</param>
        /// <param name="transitional">有过渡的显示</param>
        public static void ShowPath(List<long> path, bool transitional = false)
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("游戏不在运行中");
                return;
            }

            if (cacheRender.Count > 0)
            {
                foreach (var renderer in cacheRender)
                {
                    renderer.material.SetColor("_Color", Color.white);
                }
            }
            else
            {
                ResetMapState();
            }

            if (!transitional)
            {
                foreach (long point in path)
                {
                    if (MapCache.ContainsKey(point))
                    {
                        var renderer = MapCache.mapCache[point].renderer;
                        renderer.material.SetColor("_Color", Color.green);
                        cacheRender.Add(renderer);
                    }
                }
            }
            else
            {
                TransitionalCts.Cancel();
                TransitionalCts = new CancellationTokenSource();
                TransitionalDisplayPath(path);
            }
        }

        static CancellationTokenSource TransitionalCts = new CancellationTokenSource();

        static async void TransitionalDisplayPath(List<long> path)
        {
            try
            {
                foreach (long point in path)
                {
                    if (MapCache.CheckTileInfoValidity(point))
                    {
                        var renderer = MapCache.mapCache[point].renderer;
                        renderer.material.SetColor("_Color", Color.green);
                        cacheRender.Add(renderer);
                        await Task.Delay(100, TransitionalCts.Token);
                    }
                }
            }
            catch (System.OperationCanceledException)
            {
                Debug.Log("任务被取消");
            }
        }

        /// <summary>
        /// 重置地图上所有路径瓦片的显示
        /// </summary>
        public static void ResetMapState()
        {
            if (MapCache.mapPrefab == null) MapCache.InitLunaMap();

            if (!Application.isPlaying) return;

            foreach (var tileInfo in MapCache.mapCache.Values)
            {
                if (tileInfo.renderer == null)
                {
                    Debug.Log("null renderer");
                    return;
                }
                tileInfo.renderer.material.SetColor("_Color", Color.white);
            }
        }
    }
}
