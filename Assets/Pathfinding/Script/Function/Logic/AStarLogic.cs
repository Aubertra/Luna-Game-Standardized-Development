using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public class AStarNode
    {
        public long GridPos;
        public long Parent;
        public float G;
        public float H;
        public float F => G + H;
        public bool IsClosed;

        public override string ToString()
        {
            Vector2Int v = AStarLogic.DecodeKey(GridPos);
            return $"[{v.x},{v.y}] G={G:F2} H={H:F2} F={F:F2} Parent={Parent}";
        }
    }

    public class PriorityQueue<T>
    {
        private List<(float priority, T item)> heap = new List<(float, T)>();

        public int Count => heap.Count;
        public bool IsEmpty => heap.Count == 0;

        public void Enqueue(T item, float priority)
        {
            heap.Add((priority, item));
            SiftUp(heap.Count - 1);
        }

        public T Dequeue()
        {
            if (heap.Count == 0)
                throw new InvalidOperationException("Queue is empty");

            var result = heap[0].item;

            heap[0] = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);

            if (heap.Count > 0)
                SiftDown(0);

            return result;
        }

        private void SiftUp(int index)
        {
            var item = heap[index];

            while (index > 0)
            {
                int parentIndex = (index - 1) / 2;

                if (item.priority >= heap[parentIndex].priority)
                    break;

                heap[index] = heap[parentIndex];
                index = parentIndex;
            }

            heap[index] = item;
        }

        private void SiftDown(int index)
        {
            var item = heap[index];
            int count = heap.Count;
            int half = count / 2;

            while (index < half)
            {
                int leftChild = index * 2 + 1;
                int rightChild = leftChild + 1;

                int smallerChild = leftChild;
                if (rightChild < count && heap[rightChild].priority < heap[leftChild].priority)
                    smallerChild = rightChild;

                if (item.priority <= heap[smallerChild].priority)
                    break;

                heap[index] = heap[smallerChild];
                index = smallerChild;
            }

            heap[index] = item;
        }
    }

    public static class AStarLogic
    {

        /// <summary>
        /// 将 Vector2Int 编码为 long (高32位存X，低32位存Y)
        /// </summary>
        public static long EncodeKey(int x, int y)
        {
            return ((long)x << 32) | (uint)y;
        }

        /// <summary>
        /// 将 long 解码为 Vector2Int
        /// </summary>
        public static Vector2Int DecodeKey(long key)
        {
            int x = (int)(key >> 32);
            int y = (int)(key & 0xFFFFFFFF);
            return new Vector2Int(x, y);
        }

        /// <summary>
        /// A* 寻路主函数（支持行动力限制）
        /// </summary>
        /// <param name="startX">起点X坐标</param>
        /// <param name="startY">起点Y坐标</param>
        /// <param name="goalX">终点X坐标</param>
        /// <param name="goalY">终点Y坐标</param>
        /// <param name="actionPoints">行动力上限（体力），-1 表示无限制</param>
        /// <param name="allowDiagonal">是否允许斜向移动</param>
        /// <returns>路径点列表（long 编码）</returns>
        public static List<long> FindPath(int startX, int startY, int goalX, int goalY, float actionPoints = -1f, bool allowDiagonal = true)
        {
            long start = EncodeKey(startX, startY);
            long goal = EncodeKey(goalX, goalY);

            // 验证
            if (!MapCache.ContainsKey(start) || !MapCache.ContainsKey(goal))
            {
                Debug.LogError("起点或终点不在 MapCache 中！");
                return new List<long>();
            }

            if (start == goal)
            {
                return new List<long> { start };
            }

            if (PathFindingConfig.DEBUG_MODE)
            {
                Debug.Log($"========== A* 开始 ==========");
                Debug.Log($"起点: ({startX},{startY}), 终点: ({goalX},{goalY}), 行动力: {(actionPoints < 0 ? "无限制" : actionPoints.ToString())}");
            }

            var openSet = new PriorityQueue<long>();
            var gScore = new Dictionary<long, float>();
            var cameFrom = new Dictionary<long, long>();
            var closedSet = new HashSet<long>();
            var hCache = new Dictionary<long, float>();

            // 初始化起点
            gScore[start] = 0;
            float startH = Heuristic(start, goal, allowDiagonal);
            hCache[start] = startH;
            openSet.Enqueue(start, startH);

            if (PathFindingConfig.DEBUG_MODE)
            {
                Debug.Log($"起点 H={startH:F2}");
            }

            var directions = allowDiagonal ? GetEightDirections() : GetFourDirections();

            // 记录：最接近目标的节点 和 体力范围内能到达的最远节点
            long bestNodeOverall = start;
            float bestHeuristicOverall = startH;

            long bestNodeInRange = start;
            float bestHeuristicInRange = startH;

            int iterations = 0;
            int maxIterations = 50000;

            while (!openSet.IsEmpty && iterations < maxIterations)
            {
                iterations++;
                long current = openSet.Dequeue();

                if (closedSet.Contains(current))
                    continue;

                float currentG = gScore[current];
                float currentH = hCache[current];

                if (PathFindingConfig.DEBUG_MODE && iterations <= 30)
                {
                    Vector2Int curPos = DecodeKey(current);
                    Vector2Int parentPos = cameFrom.ContainsKey(current) ? DecodeKey(cameFrom[current]) : curPos;
                    Debug.Log($"[{iterations}] 处理: ({curPos.x},{curPos.y}) G={currentG:F2} H={currentH:F2} F={(currentG + currentH):F2} AP剩余={(actionPoints < 0 ? -1 : actionPoints - currentG):F2} Parent=({parentPos.x},{parentPos.y})");
                }

                // 到达终点
                if (current == goal)
                {
                    if (actionPoints < 0 || currentG <= actionPoints + 0.0001f)
                    {
                        var path = ReconstructPath(cameFrom, start, goal);

                        if (PathFindingConfig.DEBUG_MODE)
                        {
                            Debug.Log($"找到路径！长度={path.Count}, 消耗={currentG:F2}/{actionPoints:F2}");
                            PrintPath(path);
                        }

                        return path;
                    }
                    else
                    {
                        if (PathFindingConfig.DEBUG_MODE)
                            Debug.Log($"终点 ({goalX},{goalY}) 需要体力 {currentG:F2}，超过上限 {actionPoints:F2}");

                        break;
                    }
                }

                closedSet.Add(current);

                // 更新全局最佳节点
                if (currentH < bestHeuristicOverall)
                {
                    bestHeuristicOverall = currentH;
                    bestNodeOverall = current;
                }

                // 更新体力范围内最佳节点
                if (actionPoints < 0 || currentG <= actionPoints + 0.0001f)
                {
                    if (currentH < bestHeuristicInRange)
                    {
                        bestHeuristicInRange = currentH;
                        bestNodeInRange = current;
                    }
                }

                // 探索邻居
                int curX = (int)(current >> 32);
                int curY = (int)(current & 0xFFFFFFFF);

                foreach (var dir in directions)
                {
                    int nx = curX + dir.x;
                    int ny = curY + dir.y;
                    long neighbor = EncodeKey(nx, ny);

                    if (!MapCache.ContainsKey(neighbor))
                        continue;

                    if (closedSet.Contains(neighbor))
                        continue;

                    TileInfo tileInfo = MapCache.GetTile(neighbor);
                    if (tileInfo == null || tileInfo.cost <= 0)
                        continue;

                    float dirMultiplier = (dir.x != 0 && dir.y != 0) ? 1.414f : 1f;
                    float stepCost = tileInfo.cost * dirMultiplier;
                    float tentativeG = currentG + stepCost;

                    // 体力限制剪枝
                    if (actionPoints >= 0 && tentativeG > actionPoints + 0.0001f)
                    {
                        continue;
                    }

                    // 如果已有更优 G 值，跳过
                    if (gScore.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG - 0.0001f)
                        continue;

                    // 更新或设置 G 值和父节点
                    gScore[neighbor] = tentativeG;
                    cameFrom[neighbor] = current;

                    // 计算 H 值（如果还没有）
                    if (!hCache.TryGetValue(neighbor, out float hValue))
                    {
                        hValue = Heuristic(neighbor, goal, allowDiagonal);
                        hCache[neighbor] = hValue;
                    }

                    // 入队
                    openSet.Enqueue(neighbor, tentativeG + hValue);
                }
            }

            // ========== 不可达处理 ==========

            // 优先返回体力范围内的最佳节点
            long finalBestNode = bestNodeInRange;

            if (bestNodeInRange == start && actionPoints >= 0)
            {
                if (PathFindingConfig.DEBUG_MODE)
                    Debug.LogWarning($"体力 {actionPoints:F2} 不足以到达任何邻居节点！");
                return new List<long> { start };
            }

            if (finalBestNode != start)
            {
                float finalG = gScore.ContainsKey(finalBestNode) ? gScore[finalBestNode] : 0;

                if (PathFindingConfig.DEBUG_MODE)
                {
                    Vector2Int bestPos = DecodeKey(finalBestNode);
                    Debug.LogWarning(
                        $"目标 ({goalX},{goalY}) 不可达！\n" +
                        $"  返回体力范围内最佳节点: ({bestPos.x},{bestPos.y})\n" +
                        $"  消耗体力: {finalG:F2}/{actionPoints:F2}\n" +
                        $"  剩余距离估计: H={bestHeuristicInRange:F2}"
                    );
                }

                return ReconstructPath(cameFrom, start, finalBestNode);
            }

            return new List<long> { start };
        }

        /// <summary>
        /// 计算路径消耗的总行动力
        /// </summary>
        public static float CalculatePathCost(List<long> path)
        {
            if (path == null || path.Count < 2)
                return 0;

            float total = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int a = DecodeKey(path[i]);
                Vector2Int b = DecodeKey(path[i + 1]);
                int dx = a.x - b.x;
                int dy = a.y - b.y;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;

                float stepCost = (dx == 1 && dy == 1) ? 1.414f : 1f;

                TileInfo tile = MapCache.GetTile(path[i + 1]);
                if (tile != null && tile.cost > 0)
                    stepCost *= tile.cost;

                total += stepCost;
            }
            return total;
        }

        // ==================== 辅助方法 ====================

        private static float Heuristic(long a, long b, bool allowDiagonal)
        {
            int ax = (int)(a >> 32);
            int ay = (int)(a & 0xFFFFFFFF);
            int bx = (int)(b >> 32);
            int by = (int)(b & 0xFFFFFFFF);

            int dx = ax - bx;
            int dy = ay - by;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;

            float h;
            if (allowDiagonal)
            {
                // 对角线距离：max(dx, dy) + (sqrt2 - 1) * min(dx, dy)
                if (dx > dy)
                    h = dx + dy * 0.41421356f;
                else
                    h = dy + dx * 0.41421356f;
            }
            else
            {
                h = dx + dy;
            }

            // 打破对称性：微小扰动让算法优先选择更直指目标的路径
            return h * 1.0001f;
        }

        private static List<long> ReconstructPath(Dictionary<long, long> cameFrom, long start, long goal)
        {
            var path = new List<long>();
            long current = goal;
            var visited = new HashSet<long>();
            int maxIterations = 10000;
            int iterations = 0;

            while (current != start && iterations < maxIterations)
            {
                iterations++;

                if (visited.Contains(current))
                {
                    Debug.LogError($"路径回溯循环: {current}");
                    break;
                }

                path.Add(current);
                visited.Add(current);

                if (!cameFrom.TryGetValue(current, out long parent))
                {
                    Vector2Int pos = DecodeKey(current);
                    Debug.LogError($"节点 ({pos.x},{pos.y}) 不在 cameFrom 中");
                    break;
                }

                if (parent == current)
                {
                    Vector2Int pos = DecodeKey(current);
                    Debug.LogError($"节点自引用: ({pos.x},{pos.y})");
                    break;
                }

                current = parent;
            }

            if (current == start)
                path.Add(start);

            path.Reverse();
            return path;
        }

        private static void PrintPath(List<long> path)
        {
            var parts = new List<string>();
            foreach (var key in path)
            {
                Vector2Int v = DecodeKey(key);
                parts.Add($"({v.x},{v.y})");
            }
            Debug.Log($"路径: {string.Join(" -> ", parts)}");
        }

        // ==================== 坐标转换（对外兼容） ====================

        /// <summary>
        /// 将 long 路径转为世界坐标列表
        /// </summary>
        public static List<Vector3> GetWorldPath(List<long> path)
        {
            if (path == null || path.Count == 0)
                return new List<Vector3>();

            List<Vector3> worldPath = new List<Vector3>();
            foreach (var key in path)
            {
                TileInfo info = MapCache.GetTile(key);
                if (info != null)
                    worldPath.Add(info.position);
            }
            return worldPath;
        }

        // ==================== 方向相关 ====================

        private static Vector2Int[] GetFourDirections()
        {
            return new Vector2Int[]
            {
                    new Vector2Int(0, 1), new Vector2Int(1, 0),
                    new Vector2Int(0, -1), new Vector2Int(-1, 0)
            };
        }

        private static Vector2Int[] GetEightDirections()
        {
            return new Vector2Int[]
            {
                    new Vector2Int(0, 1), new Vector2Int(1, 1),
                    new Vector2Int(1, 0), new Vector2Int(1, -1),
                    new Vector2Int(0, -1), new Vector2Int(-1, -1),
                    new Vector2Int(-1, 0), new Vector2Int(-1, 1)
            };
        }

        // ==================== 调试/诊断 ====================

        public static void RunDiagnostic()
        {
            if (!PathFindingConfig.DEBUG_MODE) return;

            Debug.Log("========== 创建测试地图 ==========");

            MapCache.mapCache.Clear();

            for (int x = 0; x <= 20; x++)
            {
                for (int y = 0; y <= 20; y++)
                {
                    long key = EncodeKey(x, y);
                    MapCache.mapCache[key] = new TileInfo
                    {
                        position = new Vector3(x, 0, y),
                        cost = 1
                    };
                }
            }

            Debug.Log($"地图格子总数: {MapCache.mapCache.Count}");

            int startX = 1, startY = 1;
            int goalX = 5, goalY = 5;

            Debug.Log($"========== 测试寻路: ({startX},{startY}) -> ({goalX},{goalY}) ==========");

            var path = FindPath(startX, startY, goalX, goalY);

            Debug.Log($"路径长度: {path.Count}");
            PrintPath(path);

            float totalG = 0;
            for (int i = 0; i < path.Count - 1; i++)
            {
                Vector2Int a = DecodeKey(path[i]);
                Vector2Int b = DecodeKey(path[i + 1]);
                int dx = a.x - b.x;
                int dy = a.y - b.y;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;
                float stepCost = (dx == 1 && dy == 1) ? 1.414f : 1f;
                totalG += stepCost;
            }

            int idealSteps = Math.Max(Math.Abs(goalX - startX), Math.Abs(goalY - startY));

            Debug.Log($"实际总代价 G: {totalG:F2}");
            Debug.Log($"对角线步数: {idealSteps}");
            Debug.Log($"理想对角线代价: {idealSteps * 1.414f:F2}");

            if (path.Count > 0 && path[0] == EncodeKey(startX, startY) && path[path.Count - 1] == EncodeKey(goalX, goalY))
            {
                Debug.Log("路径起点和终点正确");
            }
            else
            {
                Debug.LogError($"路径不正确！");
            }

            Debug.Log("========== 路径节点详情 ==========");
            for (int i = 0; i < path.Count; i++)
            {
                Vector2Int pos = DecodeKey(path[i]);
                Vector2Int prev = i > 0 ? DecodeKey(path[i - 1]) : pos;
                int dx = pos.x - prev.x;
                int dy = pos.y - prev.y;
                if (dx < 0) dx = -dx;
                if (dy < 0) dy = -dy;

                if (i == 0)
                    Debug.Log($"[{i}] ({pos.x},{pos.y}) - 起点");
                else if (i == path.Count - 1)
                    Debug.Log($"[{i}] ({pos.x},{pos.y}) - 终点 (从 ({prev.x},{prev.y}) 移动 dx={dx} dy={dy})");
                else
                    Debug.Log($"[{i}] ({pos.x},{pos.y}) (从 ({prev.x},{prev.y}) 移动 dx={dx} dy={dy})");
            }
        }
    }
}