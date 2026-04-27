using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public enum PathfindingAlgorithm
    {
        AStar,
        JPS,
        JPSPlus
    }

    /// <summary>
    /// 路径缓存结构（内存紧凑）
    /// </summary>
    internal class PathCache
    {
        public long targetId;
        public List<long> nodes;        // 路径节点
        public int nextIdx;             // 下一个目标节点的索引
        public float nextRecalcTime;    // 下次重算时间（绝对时间）
        public bool isComplete;
    }

    /// <summary>
    /// Tile缓存结构
    /// </summary>
    internal struct TileRef
    {
        public long tileId;
        public Vector3 position;
        public float speedMul;          // 预计算的速度倍率
    }

    /// <summary>
    /// Transform 扩展方法类
    /// </summary>
    public static class TransformExtensions
    {
        // 用 int(instanceID) 做键，比 Transform 哈希快 3-5 倍
        private static readonly Dictionary<int, TileRef> tileCache = new Dictionary<int, TileRef>(64);
        private static readonly Dictionary<int, PathCache> pathCaches = new Dictionary<int, PathCache>(64);

        private const float RECALC_INTERVAL = 0.2f;
        private const float DEVIATION_SQ = 0.25f;

        #region 公共接口

        public static void MoveToTile(this Transform t, TileInfo target, float moveDist,
            PathfindingAlgorithm algo = PathfindingAlgorithm.JPS)
        {
            if (target == null) return;

            var curTile = MapCache.GetNearlyTile(t);
            if (curTile == null) return;

            long curId = curTile.identifier;
            long tarId = target.identifier;
            int id = t.GetInstanceID();

            // 已在目标
            if (curId == tarId)
            {
                TryOccupy(t, curTile);
                MoveTo(t, ref curTile.position, moveDist, curTile.cost);
                return;
            }

            // 获取/创建缓存
            if (!pathCaches.TryGetValue(id, out var pc))
            {
                pc = new PathCache();
                pathCaches[id] = pc;
            }

            // 重算路径
            if (NeedRecalc(pc, tarId, Time.time))
            {
                var startV = Decode(curId);
                var endV = Decode(tarId);
                var path = Find(startV.x, startV.y, endV.x, endV.y, algo);

                if (path == null || path.Count == 0)
                {
                    pc.nodes = null;
                    return;
                }

                pc.nodes = path;
                pc.targetId = tarId;
                pc.nextIdx = 0;
                pc.nextRecalcTime = Time.time + RECALC_INTERVAL;
                pc.isComplete = false;
            }

            var nodes = pc.nodes;
            if (nodes == null) return;

            // 更新进度
            UpdateProgress(t, pc, curId);

            int ni = pc.nextIdx;
            if (ni >= nodes.Count)
            {
                pc.isComplete = true;
                TryOccupy(t, curTile);
                MoveTo(t, ref target.position, moveDist, target.cost);
                return;
            }

            long nextId = nodes[ni];
            var nextTile = MapCache.GetTile(nextId);
            if (nextTile == null || nextTile.cost <= 0)
            {
                pc.nodes = null; // 下一帧重算
                return;
            }

            bool isLast = (ni == nodes.Count - 1);
            UpdateTileCache(id, curTile);

            var tgtPos = isLast ? target.position : nextTile.position;
            float cost = isLast ? target.cost : nextTile.cost;

            if (!isLast) TryRelease(t, curTile);
            else TryOccupy(t, curTile);

            MoveTo(t, ref tgtPos, moveDist, cost);
        }

        public static void MoveToTile(this Transform t, Transform targetT, float moveDist,
            PathfindingAlgorithm algo = PathfindingAlgorithm.JPS)
        {
            if (targetT == null) return;
            var tile = MapCache.GetNearlyTile(targetT.position);
            MoveToTile(t, tile, moveDist, algo);
        }

        public static void AssignTarget(this Transform t, TileInfo target,
            PathfindingAlgorithm algo = PathfindingAlgorithm.JPS)
        {
            if (target == null) { ClearPathCache(t); return; }

            var cur = MapCache.GetNearlyTile(t);
            if (cur == null) return;

            int id = t.GetInstanceID();
            var sv = Decode(cur.identifier);
            var ev = Decode(target.identifier);
            var path = Find(sv.x, sv.y, ev.x, ev.y, algo);

            pathCaches[id] = new PathCache
            {
                targetId = target.identifier,
                nodes = path,
                nextIdx = 0,
                nextRecalcTime = Time.time + RECALC_INTERVAL
            };
        }

        public static void ClearPathCache(this Transform t) => pathCaches.Remove(t.GetInstanceID());
        public static void ClearAllPathCaches() { pathCaches.Clear(); tileCache.Clear(); }

        #endregion

        #region 路径计算

        private static List<long> Find(int sx, int sy, int gx, int gy, PathfindingAlgorithm algo)
        {
            switch (algo)
            {
                case PathfindingAlgorithm.JPSPlus: return JPSPlusLogic.FindPath(sx, sy, gx, gy);
                case PathfindingAlgorithm.JPS: return JPSLogic.FindPath(sx, sy, gx, gy);
                default: return AStarLogic.FindPath(sx, sy, gx, gy);
            }
        }

        private static bool NeedRecalc(PathCache pc, long targetId, float now)
        {
            var nodes = pc.nodes;
            if (nodes == null || nodes.Count == 0) return true;
            if (pc.targetId != targetId) return true;
            if (pc.isComplete) return true;
            if (now > pc.nextRecalcTime) return true;
            if (pc.nextIdx >= nodes.Count) return true;

            // 校验下一个节点是否有效
            long nid = nodes[pc.nextIdx];
            if (!MapCache.ContainsKey(nid)) return true;
            var t = MapCache.GetTile(nid);
            if (t == null || t.cost <= 0) return true;

            return false;
        }

        #endregion

        #region 进度追踪

        private static void UpdateProgress(Transform t, PathCache pc, long curId)
        {
            var nodes = pc.nodes;
            int count = nodes.Count;
            int idx = pc.nextIdx;

            // 快速路径：精确命中
            if (idx < count && nodes[idx] == curId)
            {
                pc.nextIdx = idx + 1;
                return;
            }

            // 线性扫描（路径通常不会跳太多）
            int scanEnd = count < idx + 6 ? count : idx + 6;
            for (int i = idx; i < scanEnd; i++)
            {
                if (nodes[i] == curId)
                {
                    pc.nextIdx = i + 1;
                    return;
                }
            }

            // 偏离：找最近节点
            RecoverIndex(t, pc, idx, scanEnd);
        }

        private static void RecoverIndex(Transform t, PathCache pc, int start, int end)
        {
            var nodes = pc.nodes;
            Vector3 pos = t.position;
            float bestSq = float.MaxValue;
            int bestI = start;

            for (int i = start; i < end; i++)
            {
                var tile = MapCache.GetTile(nodes[i]);
                if (tile == null) continue;
                float dx = tile.position.x - pos.x;
                float dy = tile.position.y - pos.y;
                float dz = tile.position.z - pos.z;
                float sq = dx * dx + dy * dy + dz * dz;
                if (sq < bestSq) { bestSq = sq; bestI = i; }
            }

            if (bestSq > DEVIATION_SQ)
                pc.nodes = null; // 太远，强制重算
            else
                pc.nextIdx = bestI + 1;
        }

        #endregion

        #region 移动

        private static void MoveTo(Transform t, ref Vector3 target, float maxDist, float cost)
        {
            float speed = maxDist * SpeedMul(cost);
            // 手动 Vector3.MoveTowards 内联，减少函数调用
            float dx = target.x - t.position.x;
            float dy = target.y - t.position.y;
            float dz = target.z - t.position.z;
            float sq = dx * dx + dy * dy + dz * dz;

            if (sq <= speed * speed)
            {
                t.position = target;
            }
            else
            {
                float invDist = 1f / (float)Math.Sqrt(sq); // Luna 用 MathF.Sqrt
                t.position = new Vector3(
                    t.position.x + dx * invDist * speed,
                    t.position.y + dy * invDist * speed,
                    t.position.z + dz * invDist * speed
                );
            }
        }

        public static float SpeedMul(float cost)
        {
            if (!PathFindingConfig.CostDecideSpeed) return 1f;
            // 内联 Clamp
            if (cost < 0f) cost = 0f;
            if (cost > 10f) cost = 10f;
            float t = cost * 0.1f;
            return 1f - t * t;
        }

        #endregion

        #region 占领/释放

        private static void TryOccupy(Transform t, TileInfo tile)
        {
            if (!PathFindingConfig.AllowOccpuyTile) return;
            if (tile.occpuyer != null) return;
            tile.cost = 999;
            tile.occpuyer = t;
        }

        private static void TryRelease(Transform t, TileInfo tile)
        {
            if (!PathFindingConfig.AllowOccpuyTile) return;
            if (tile.occpuyer != t) return;
            tile.cost = 1;
            tile.occpuyer = null;
        }

        #endregion

        #region 工具

        private static Vector2Int Decode(long key)
        {
            return new Vector2Int((int)(key >> 32), (int)(key & 0xFFFFFFFF));
        }

        private static void UpdateTileCache(int id, TileInfo tile)
        {
            tileCache[id] = new TileRef
            {
                tileId = tile.identifier,
                position = tile.position
            };
        }

        #endregion

        // 定义寻路策略映射
        private static readonly Dictionary<PathfindingAlgorithm, Func<int, int, int, int, List<long>>> Pathfinders =
            new Dictionary<PathfindingAlgorithm, Func<int, int, int, int, List<long>>>
            {
        { PathfindingAlgorithm.AStar,   (sx, sy, ex, ey) => AStarLogic.FindPath(sx, sy, ex, ey) },
        { PathfindingAlgorithm.JPS,     (sx, sy, ex, ey) => JPSLogic.FindPath(sx, sy, ex, ey) },
        { PathfindingAlgorithm.JPSPlus, (sx, sy, ex, ey) => JPSPlusLogic.FindPath(sx, sy, ex, ey) },
            };

        public static void MoveByLazer(this Transform transform, TileInfo target, float moveDistance, PathfindingAlgorithm algorithm = PathfindingAlgorithm.AStar)
        {
            var cur = MapCache.GetNearlyTile(transform);
            var start = AStarLogic.DecodeKey(cur.identifier);
            var end = AStarLogic.DecodeKey(target.identifier);
            var path = Pathfinders[algorithm](start.x, start.y, end.x, end.y);

            if (path.Count > 2) 
            {
                //Debug.Log($"{start} , {AStarLogic.DecodeKey(path[1])}");
                //Debug.Log($"{transform.position} , {MapCache.GetTile(path[1]).position}");
                transform.position = Vector3.MoveTowards(transform.position, MapCache.GetTile(path[1]).position, moveDistance);
            }
        }
    }
}