using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public static class JPSPlusLogic
    {
        public const int DIR_N = 0;
        public const int DIR_NE = 1;
        public const int DIR_E = 2;
        public const int DIR_SE = 3;
        public const int DIR_S = 4;
        public const int DIR_SW = 5;
        public const int DIR_W = 6;
        public const int DIR_NW = 7;

        private static readonly int[] DirX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] DirY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        // 跳距表
        public static Dictionary<long, int[]> jumpDistances;

        // 标记哪些格子是"跳点"（主跳点）
        private static HashSet<long> primaryJumpPoints;

        public static bool IsPreprocessed => jumpDistances != null && jumpDistances.Count > 0;

        #region 预处理

        public static void PreprocessMap(int maxX, int maxY)
        {
            jumpDistances = new Dictionary<long, int[]>();
            primaryJumpPoints = new HashSet<long>();

            // 第一步：标记所有主跳点
            MarkPrimaryJumpPoints(maxX, maxY);

            // 第二步：对每个方向，计算跳距
            int[][] allDirs = new int[][] {
                new int[] { DIR_N, DIR_S, DIR_E, DIR_W },    // 直线方向
                new int[] { DIR_NE, DIR_NW, DIR_SE, DIR_SW }  // 对角线方向
            };

            foreach (var dirGroup in allDirs)
            {
                foreach (int dir in dirGroup)
                {
                    CalculateJumpDistancesForDirection(dir, maxX, maxY);
                }
            }

            // 第三步：确保所有可行走格子都有跳距表
            foreach (var kvp in MapCache.mapCache)
            {
                long key = kvp.Key;
                if (kvp.Value.cost <= 0) continue;

                if (!jumpDistances.ContainsKey(key))
                {
                    jumpDistances[key] = new int[8];
                }
            }

            Debug.Log($"JPS+ 预处理完成: {jumpDistances.Count} 格子, {primaryJumpPoints.Count} 主跳点");
        }

        /// <summary>
        /// 标记所有主跳点
        /// 主跳点 = 有强制邻居的格子
        /// </summary>
        private static void MarkPrimaryJumpPoints(int maxX, int maxY)
        {
            for (int y = 0; y <= maxY; y++)
            {
                for (int x = 0; x <= maxX; x++)
                {
                    if (!IsWalkable(x, y)) continue;

                    // 检查是否有强制邻居
                    if (HasAnyForcedNeighbor(x, y))
                    {
                        primaryJumpPoints.Add(AStarLogic.EncodeKey(x, y));
                    }
                }
            }
        }

        /// <summary>
        /// 检查一个格子在任何方向上是否有强制邻居
        /// </summary>
        private static bool HasAnyForcedNeighbor(int x, int y)
        {
            // 水平方向的强制邻居
            if ((IsWalkable(x + 1, y + 1) && !IsWalkable(x, y + 1)) ||
                (IsWalkable(x + 1, y - 1) && !IsWalkable(x, y - 1)))
                return true;

            if ((IsWalkable(x - 1, y + 1) && !IsWalkable(x, y + 1)) ||
                (IsWalkable(x - 1, y - 1) && !IsWalkable(x, y - 1)))
                return true;

            // 垂直方向的强制邻居
            if ((IsWalkable(x + 1, y + 1) && !IsWalkable(x + 1, y)) ||
                (IsWalkable(x - 1, y + 1) && !IsWalkable(x - 1, y)))
                return true;

            if ((IsWalkable(x + 1, y - 1) && !IsWalkable(x + 1, y)) ||
                (IsWalkable(x - 1, y - 1) && !IsWalkable(x - 1, y)))
                return true;

            return false;
        }

        /// <summary>
        /// 计算某个方向所有格子的跳距
        /// 核心算法：从边界/障碍物反向扫描
        /// </summary>
        private static void CalculateJumpDistancesForDirection(int dir, int maxX, int maxY)
        {
            int dx = DirX[dir];
            int dy = DirY[dir];
            bool isDiagonal = (dx != 0 && dy != 0);

            // 根据方向确定扫描顺序
            int xStart, xEnd, xStep;
            int yStart, yEnd, yStep;

            if (dx >= 0) { xStart = maxX; xEnd = 0; xStep = -1; }
            else { xStart = 0; xEnd = maxX; xStep = 1; }

            if (dy >= 0) { yStart = maxY; yEnd = 0; yStep = -1; }
            else { yStart = 0; yEnd = maxY; yStep = 1; }

            // 直线方向：简单的一维扫描
            if (!isDiagonal)
            {
                // 沿着正交方向扫描线
                if (dx != 0)
                {
                    // 水平扫描：每行独立
                    for (int y = 0; y <= maxY; y++)
                    {
                        int distance = 0;
                        for (int x = xStart; x >= 0 && x <= maxX; x += xStep)
                        {
                            if (!IsWalkable(x, y))
                            {
                                distance = 0;
                                continue;
                            }

                            long key = AStarLogic.EncodeKey(x, y);
                            EnsureJumpDistances(key);

                            if (x == xStart || !IsWalkable(x - xStep, y))
                            {
                                // 紧挨边界或障碍物
                                distance = 0;
                            }
                            else if (primaryJumpPoints.Contains(key))
                            {
                                // 主跳点：距离为1（跳到自己）
                                distance = 1;
                            }
                            else
                            {
                                // 从上一个格子继承+1
                                distance++;
                            }

                            jumpDistances[key][dir] = distance;
                        }
                    }
                }
                else
                {
                    // 垂直扫描：每列独立
                    for (int x = 0; x <= maxX; x++)
                    {
                        int distance = 0;
                        for (int y = yStart; y >= 0 && y <= maxY; y += yStep)
                        {
                            if (!IsWalkable(x, y))
                            {
                                distance = 0;
                                continue;
                            }

                            long key = AStarLogic.EncodeKey(x, y);
                            EnsureJumpDistances(key);

                            if (y == yStart || !IsWalkable(x, y - yStep))
                            {
                                distance = 0;
                            }
                            else if (primaryJumpPoints.Contains(key))
                            {
                                distance = 1;
                            }
                            else
                            {
                                distance++;
                            }

                            jumpDistances[key][dir] = distance;
                        }
                    }
                }
            }
            else
            {
                // 对角线方向：需要特殊处理
                // 从"右下角"开始扫描
                for (int y = yStart; y >= 0 && y <= maxY; y += yStep)
                {
                    for (int x = xStart; x >= 0 && x <= maxX; x += xStep)
                    {
                        if (!IsWalkable(x, y))
                            continue;

                        long key = AStarLogic.EncodeKey(x, y);
                        EnsureJumpDistances(key);

                        int nextX = x + dx;
                        int nextY = y + dy;

                        // 下一个格子出界或不可走 -> 距离0
                        if (nextX < 0 || nextX > maxX || nextY < 0 || nextY > maxY ||
                            !IsWalkable(nextX, nextY))
                        {
                            jumpDistances[key][dir] = 0;
                            continue;
                        }

                        // 对角线移动：需要相邻开口
                        bool adj1 = IsWalkable(x + dx, y);
                        bool adj2 = IsWalkable(x, y + dy);

                        if (!adj1 && !adj2)
                        {
                            // 墙角，不能对角线移动
                            jumpDistances[key][dir] = 0;
                            continue;
                        }

                        // 如果自己是主跳点 -> 距离1
                        if (primaryJumpPoints.Contains(key))
                        {
                            jumpDistances[key][dir] = 1;
                            continue;
                        }

                        // 检查直线分量方向是否有跳点
                        long nextKey = AStarLogic.EncodeKey(nextX, nextY);
                        if (!jumpDistances.ContainsKey(nextKey))
                            continue;

                        int dist1 = jumpDistances[nextKey][GetCardinalDir(dx, 0)]; // 水平分量
                        int dist2 = jumpDistances[nextKey][GetCardinalDir(0, dy)]; // 垂直分量

                        if (dist1 > 0 || dist2 > 0)
                        {
                            // 下一个格子的直线方向有跳点 -> 距离1
                            jumpDistances[key][dir] = 1;
                        }
                        else
                        {
                            // 继承下一个格子的距离+1
                            int nextDist = jumpDistances[nextKey][dir];
                            jumpDistances[key][dir] = (nextDist > 0) ? nextDist + 1 : 0;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 获取直线方向索引
        /// </summary>
        private static int GetCardinalDir(int dx, int dy)
        {
            if (dx > 0) return DIR_E;
            if (dx < 0) return DIR_W;
            if (dy > 0) return DIR_N;
            if (dy < 0) return DIR_S;
            return -1;
        }

        /// <summary>
        /// 确保格子有跳距数组
        /// </summary>
        private static void EnsureJumpDistances(long key)
        {
            if (!jumpDistances.ContainsKey(key))
            {
                jumpDistances[key] = new int[8];
            }
        }

        #endregion

        #region 寻路

        public static List<long> FindPath(int startX, int startY, int goalX, int goalY,
            float actionPoints = -1f, bool allowDiagonal = true)
        {
            if (jumpDistances == null || jumpDistances.Count == 0)
            {
                Debug.LogError("跳距表未初始化！");
                return new List<long> { AStarLogic.EncodeKey(startX, startY) };
            }

            long start = AStarLogic.EncodeKey(startX, startY);
            long goal = AStarLogic.EncodeKey(goalX, goalY);

            if (!jumpDistances.ContainsKey(start))
            {
                Debug.LogError($"起点不在跳距表中: ({startX},{startY})");
                return new List<long> { start };
            }

            if (!jumpDistances.ContainsKey(goal))
            {
                goal = FindNearestWalkable(goalX, goalY);
                if (goal == 0) return new List<long> { start };
            }

            if (start == goal)
                return new List<long> { start };

            var openSet = new PriorityQueue<long>();
            var gScore = new Dictionary<long, float>();
            var cameFrom = new Dictionary<long, long>();
            var closedSet = new HashSet<long>();
            var hCache = new Dictionary<long, float>();

            gScore[start] = 0;
            float startH = Heuristic(startX, startY, goalX, goalY, allowDiagonal);
            hCache[start] = startH;
            openSet.Enqueue(start, startH);

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

                if (current == goal)
                {
                    if (actionPoints < 0 || currentG <= actionPoints + 0.0001f)
                    {
                        return ReconstructPath(cameFrom, start, goal);
                    }
                    break;
                }

                closedSet.Add(current);

                float currentH = hCache[current];
                if (actionPoints < 0 || currentG <= actionPoints + 0.0001f)
                {
                    if (currentH < bestHeuristicInRange)
                    {
                        bestHeuristicInRange = currentH;
                        bestNodeInRange = current;
                    }
                }

                IdentifySuccessors(current, goalX, goalY, gScore, cameFrom,
                    hCache, openSet, closedSet, actionPoints, allowDiagonal);
            }

            if (bestNodeInRange != start)
            {
                return ReconstructPath(cameFrom, start, bestNodeInRange);
            }

            return new List<long> { start };
        }

        private static void IdentifySuccessors(long current, int goalX, int goalY,
            Dictionary<long, float> gScore, Dictionary<long, long> cameFrom,
            Dictionary<long, float> hCache, PriorityQueue<long> openSet,
            HashSet<long> closedSet, float actionPoints, bool allowDiagonal)
        {
            if (!jumpDistances.TryGetValue(current, out int[] distances))
                return;

            float currentG = gScore[current];
            Vector2Int curPos = AStarLogic.DecodeKey(current);

            int maxDir = allowDiagonal ? 8 : 4;

            for (int dir = 0; dir < maxDir; dir++)
            {
                int jumpDist = distances[dir];

                if (jumpDist <= 0)
                    continue;

                int jumpX = curPos.x + DirX[dir] * jumpDist;
                int jumpY = curPos.y + DirY[dir] * jumpDist;
                long jumpNode = AStarLogic.EncodeKey(jumpX, jumpY);

                if (!IsWalkable(jumpX, jumpY))
                    continue;

                if (closedSet.Contains(jumpNode))
                    continue;

                bool isDiagonal = (DirX[dir] != 0 && DirY[dir] != 0);
                float moveCost = isDiagonal ? jumpDist * 1.414f : jumpDist;
                float tentativeG = currentG + moveCost;

                if (actionPoints >= 0 && tentativeG > actionPoints + 0.0001f)
                    continue;

                if (gScore.TryGetValue(jumpNode, out float existingG) &&
                    tentativeG >= existingG - 0.0001f)
                    continue;

                gScore[jumpNode] = tentativeG;
                cameFrom[jumpNode] = current;

                if (!hCache.TryGetValue(jumpNode, out float hValue))
                {
                    hValue = Heuristic(jumpX, jumpY, goalX, goalY, allowDiagonal);
                    hCache[jumpNode] = hValue;
                }

                openSet.Enqueue(jumpNode, tentativeG + hValue);
            }
        }

        #endregion

        #region 辅助方法

        private static float Heuristic(int x, int y, int goalX, int goalY, bool allowDiagonal)
        {
            int dx = x - goalX;
            int dy = y - goalY;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;

            float h;
            if (allowDiagonal)
            {
                if (dx > dy) h = dx + dy * 0.41421356f;
                else h = dy + dx * 0.41421356f;
            }
            else
            {
                h = dx + dy;
            }
            return h * 1.0001f;
        }

        private static List<long> ReconstructPath(Dictionary<long, long> cameFrom, long start, long goal)
        {
            var path = new List<long>();
            long current = goal;
            var visited = new HashSet<long>();
            int iterations = 0;

            while (current != start && iterations < 10000)
            {
                iterations++;
                if (visited.Contains(current)) break;
                path.Add(current);
                visited.Add(current);
                if (!cameFrom.TryGetValue(current, out long parent)) break;
                if (parent == current) break;
                current = parent;
            }

            if (current == start) path.Add(start);
            path.Reverse();
            return path;
        }

        private static bool IsWalkable(int x, int y)
        {
            long key = AStarLogic.EncodeKey(x, y);
            if (!MapCache.ContainsKey(key)) return false;
            TileInfo tile = MapCache.GetTile(key);
            return tile != null && tile.cost > 0;
        }

        private static long FindNearestWalkable(int x, int y)
        {
            for (int r = 1; r <= 5; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > r * 2) continue;
                        int nx = x + dx, ny = y + dy;
                        if (IsWalkable(nx, ny))
                            return AStarLogic.EncodeKey(nx, ny);
                    }
                }
            }
            return 0;
        }

        #endregion
    }
}