using Luna.Unity;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AirStack.Pathfinding
{
    [System.Serializable]
    public class TileInfo
    {
        // 编号（long编码）
        public long identifier;
        // 物体
        public GameObject tileObj;
        // 渲染器
        public Renderer renderer;
        // 世界位置
        public Vector3 position;
        // 路过花费
        public int cost;
        // 占领信息
        public Transform occpuyer;
    }

    public static class MapCache
    {
        /// <summary>
        /// 场景中地图的预制体
        /// </summary>
        public static GameObject mapPrefab;

        public static Vector3 perTileSize;
        public static Vector3 tilePrefabSize;

        public static Dictionary<long, TileInfo> mapCache = new Dictionary<long, TileInfo>();

        /// <summary>
        /// 初始化地图
        /// </summary>
        public static bool InitLunaMap()
        {
            mapPrefab = GameObject.Find("Map");

            if (mapPrefab == null) return false;

            ClearMap();

            minX = int.MaxValue;
            minY = int.MaxValue;
            maxY = int.MinValue;
            maxX = int.MinValue;

            for (int i = 0; i < mapPrefab.transform.childCount; i++)
            {
                var tile = mapPrefab.transform.GetChild(i).gameObject.GetComponent<Tile>();

                if(i == 0)
                {
                    perTileSize = Vector3.Scale(tile.gameObject.GetComponentInChildren<MeshFilter>().sharedMesh.bounds.size, tile.gameObject.GetComponentInChildren<MeshFilter>().transform.localScale);
                    tilePrefabSize = tile.transform.localScale;
                }

                if (tile == null || tile.Info == null) continue;

                // 如果 Tile 里还有旧的字符串 identifier，在这里转换
                // 假设 Tile 提供的是 int x, int y，请根据实际情况调整
                long key = tile.Info.identifier;

                AddTile(key, tile.Info);

                Vector2Int pos = AStarLogic.DecodeKey(key);
                minX = Mathf.Min(minX, pos.x);
                maxX = Mathf.Max(maxX, pos.x);
                minY = Mathf.Min(minY, pos.y);
                maxY = Mathf.Max(maxY, pos.y);
            }

            MapTool.ResetMapState();

            JPSPlusLogic.PreprocessMap(maxX, maxY);

            Debug.Log($"初始化Luna地图Count:{mapCache.Count}");

            return true;
        }

        /// <summary>
        /// 添加Tile到地图中
        /// </summary>
        /// <param name="key">Tile编号（long编码）</param>
        /// <param name="info">Tile信息</param>
        public static void AddTile(long key, TileInfo info)
        {
            if (!mapCache.ContainsKey(key))
            {
                info.identifier = key;
                mapCache.Add(key, info);
            }
            else
            {
                info.identifier = key;
                mapCache[key] = info;
            }
        }

        /// <summary>
        /// 获取指定瓷砖
        /// </summary>
        /// <param name="key">Tile编号（long编码）</param>
        /// <returns></returns>
        public static TileInfo GetTile(long key)
        {
            return mapCache.TryGetValue(key, out TileInfo info) ? info : null;
        }

        /// <summary>
        /// 检查地图中是否包含指定瓷砖
        /// </summary>
        public static bool ContainsKey(long key)
        {
            return mapCache.ContainsKey(key) && mapCache[key].cost >= 0;
        }

        /// <summary>
        /// 变更瓷砖经过花费
        /// </summary>
        /// <param name="key">瓷砖编号（long编码）</param>
        /// <param name="changeCost">变动花费</param>
        /// <param name="setMode">是否直接设置</param>
        public static void ChangeTileCost(long key, int changeCost = 0, bool setMode = false)
        {
            if (mapCache.TryGetValue(key, out TileInfo info))
            {
                if (!setMode) info.cost += changeCost;
                else info.cost = changeCost;
            }
        }

        /// <summary>
        /// 获取最近瓷砖
        /// </summary>
        /// <param name="pos">世界位置</param>
        /// <returns>瓷砖的Info</returns>
        /// <summary>
        /// 获取最近瓷砖（螺旋搜索）
        /// </summary>
        public static TileInfo GetNearlyTile(Vector3 pos)
        {
            int startY = 0;
            int startX = 0;

            if (PathFindingConfig.CreateMeshScale == MeshScaleType.Hexagonal)
            {
                startY = Mathf.RoundToInt(pos.z / (tilePrefabSize.y * 0.8f * perTileSize.y));
                startX = Mathf.RoundToInt(pos.x / (tilePrefabSize.x * 1.05f * perTileSize.x) - (startY & 1) * 0.5f);
            }
            else if(PathFindingConfig.CreateMeshScale == MeshScaleType.Square)
            {
                startX = Mathf.RoundToInt(pos.x / (tilePrefabSize.x * 1.05f * perTileSize.x));
                startY = Mathf.RoundToInt(pos.z / (tilePrefabSize.y * 1.05f * perTileSize.y));
            }

            startX = Mathf.Clamp(startX, 0, maxX);
            startY = Mathf.Clamp(startY, 0, maxY);

            //Debug.Log($"({startX}, {startY}) ({(tilePrefabSize.x * 1.05f * perTileSize.x)}, {(tilePrefabSize.y * 0.8f * perTileSize.y)})");

            // 先检查中心
            long checkKey = AStarLogic.EncodeKey(startX, startY);
            if (mapCache.TryGetValue(checkKey, out TileInfo info) && info.cost >= 0)
                return info;

            // 小半径优先，但在同一半径内找到所有候选后比较距离
            TileInfo bestTile = null;
            float bestDist = float.MaxValue;

            for (int radius = 0; radius <= 50; radius++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    for (int y = -radius; y <= radius; y++)
                    {
                        // 只检查当前半径层（外壳）
                        if (Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)) != radius) continue;

                        long key = AStarLogic.EncodeKey(startX + x, startY + y);
                        if (mapCache.TryGetValue(key, out info) && info.cost >= 0)
                        {
                            float dist = x * x + y * y;  // 用平方比较，避免 sqrt
                            if (dist < bestDist)
                            {
                                bestDist = dist;
                                bestTile = info;
                            }
                        }
                    }
                }

                // 如果这一层找到了，直接返回（它一定比外层所有格子近）
                if (bestTile != null)
                    return bestTile;
            }
            return bestTile;
        }

        /// <summary>
        /// 获取最近瓷砖
        /// </summary>
        /// <param name="transform">物体Transform</param>
        /// <returns>瓷砖的编号（long编码）</returns>
        public static TileInfo GetNearlyTile(Transform transform)
        {
            return GetNearlyTile(transform.position);
        }

        /// <summary>
        /// 展示地图信息
        /// </summary>
        public static void DisplayMap()
        {
            string mapInfo = GetMapDisplayString();
            Debug.Log(mapInfo);
        }

        public static int maxX;
        public static int maxY;
        public static int minX;
        public static int minY;

        public static string GetMapDisplayString()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            sb.AppendLine("========== 地图排布信息 ==========");
            sb.AppendLine($"总瓦片数: {mapCache.Count}");

            if (mapCache.Count == 0)
            {
                sb.AppendLine("地图为空，没有任何瓦片数据！");
                return sb.ToString();
            }

            sb.AppendLine($"地图范围: X({minX}~{maxX}), Y({minY}~{maxY})");
            sb.AppendLine();

            // 逐行显示（从上到下）
            for (int y = maxY; y >= minY; y--)
            {
                string row = "";
                for (int x = minX; x <= maxX; x++)
                {
                    long coord = AStarLogic.EncodeKey(x, y);
                    if (mapCache.ContainsKey(coord))
                    {
                        row += "■ ";
                    }
                    else
                    {
                        row += "□ ";
                    }
                }
                sb.AppendLine($"第{y:00}行: {row}");
            }

            // 详细位置信息
            sb.AppendLine();
            sb.AppendLine("========== 详细位置信息 ==========");
            foreach (var kvp in mapCache)
            {
                Vector2Int pos = AStarLogic.DecodeKey(kvp.Key);
                sb.AppendLine($"瓦片 ({pos.x},{pos.y}) 位置: ({kvp.Value.position.x:F2}, {kvp.Value.position.y:F2}, {kvp.Value.position.z:F2}; 花费：{kvp.Value.cost})");
            }

            return sb.ToString();
        }

        /// <summary>
        /// 验证一个Tile在地图中是否有效
        /// </summary>
        /// <param name="key">Tile编号（long编码）</param>
        /// <returns></returns>
        public static bool CheckTileInfoValidity(long key)
        {
            if (mapCache.Count == 0)
            {
                Debug.LogWarning($"地图不存在");
                return false;
            }

            if (!mapCache.ContainsKey(key) && mapCache[key].cost >= 0)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 清除地图缓存数据
        /// </summary>
        public static void ClearMap()
        {
            mapCache.Clear();
        }
    }
}