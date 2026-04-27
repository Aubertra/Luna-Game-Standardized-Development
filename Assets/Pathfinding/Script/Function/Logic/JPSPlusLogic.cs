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

        // 反向方向映射
        private static readonly int[] OppositeDir = { DIR_S, DIR_SW, DIR_W, DIR_NW, DIR_N, DIR_NE, DIR_E, DIR_SE };

        // 跳距表
        public static Dictionary<long, int[]> jumpDistances;

        // 标记哪些格子是"跳点"（主跳点）
        private static HashSet<long> primaryJumpPoints;

        // 地图边界缓存
        private static int mapMinX, mapMinY, mapMaxX, mapMaxY;
        private static bool boundsInitialized = false;

        public static bool IsPreprocessed => jumpDistances != null && jumpDistances.Count > 0;

        #region 初始化

        public static void InitBounds()
        {
            mapMinX = MapCache.minX;
            mapMinY = MapCache.minY;
            mapMaxX = MapCache.maxX;
            mapMaxY = MapCache.maxY;
            boundsInitialized = true;
        }

        #endregion

        #region 预处理

        public static void PreprocessMap(int maxX, int maxY)
        {
            if (!boundsInitialized)
            {
                InitBounds();
            }

            jumpDistances = new Dictionary<long, int[]>();
            primaryJumpPoints = new HashSet<long>();

            Debug.Log($"[JPS+] 开始预处理地图: {maxX}x{maxY}");

            // 第一步：标记所有主跳点（包括对角线情况）
            MarkPrimaryJumpPoints(maxX, maxY);
            Debug.Log($"[JPS+] 主跳点标记完成: {primaryJumpPoints.Count} 个跳点");

            // 第二步：计算直线方向的跳距
            int[] straightDirs = { DIR_N, DIR_S, DIR_E, DIR_W };
            foreach (int dir in straightDirs)
            {
                CalculateStraightJumpDistances(dir, maxX, maxY);
            }
            Debug.Log($"[JPS+] 直线方向跳距计算完成");

            // 第三步：计算对角线方向的跳距（依赖直线方向结果）
            int[] diagonalDirs = { DIR_NE, DIR_NW, DIR_SE, DIR_SW };
            foreach (int dir in diagonalDirs)
            {
                CalculateDiagonalJumpDistances(dir, maxX, maxY);
            }
            Debug.Log($"[JPS+] 对角线方向跳距计算完成");

            // 第四步：确保所有可行走格子都有跳距表
            int missingCount = 0;
            foreach (var kvp in MapCache.mapCache)
            {
                long key = kvp.Key;
                if (kvp.Value.cost <= 0) continue;

                if (!jumpDistances.ContainsKey(key))
                {
                    jumpDistances[key] = new int[8];
                    missingCount++;
                }
            }

            Debug.Log($"[JPS+] 预处理完成: {jumpDistances.Count} 格子, {primaryJumpPoints.Count} 主跳点, {missingCount} 补充格子");
        }

        /// <summary>
        /// 标记所有主跳点（包含所有强制邻居情况）
        /// </summary>
        private static void MarkPrimaryJumpPoints(int maxX, int maxY)
        {
            for (int y = mapMinY; y <= maxY && y <= mapMaxY; y++)
            {
                for (int x = mapMinX; x <= maxX && x <= mapMaxX; x++)
                {
                    if (!IsWalkable(x, y)) continue;

                    // 检查任何方向上是否有强制邻居
                    if (HasAnyForcedNeighbor(x, y))
                    {
                        primaryJumpPoints.Add(AStarLogic.EncodeKey(x, y));
                        if (PathFindingConfig.DEBUG_MODE)
                        {
                            MapCache.GetTile(AStarLogic.EncodeKey(x, y)).renderer.material.color = Color.yellow;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 检查一个格子在任何方向上是否有强制邻居
        /// </summary>
        private static bool HasAnyForcedNeighbor(int x, int y)
        {
            // 检查8个移动方向上的强制邻居
            for (int dir = 0; dir < 8; dir++)
            {
                if (HasForcedNeighborInDirection(x, y, dir))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 检查特定方向上的强制邻居
        /// </summary>
        private static bool HasForcedNeighborInDirection(int x, int y, int dir)
        {
            int dx = DirX[dir];
            int dy = DirY[dir];

            if (dx != 0 && dy != 0)
            {
                // === 对角线移动的强制邻居检查 ===

                // 前提：必须能进行对角线移动
                if (!IsWalkable(x + dx, y) && !IsWalkable(x, y + dy))
                    return false;

                // 情况1：后方有障碍物导致必须转向
                // 检查垂直于对角线方向的后方
                if (IsWalkable(x - dx, y + dy) && !IsWalkable(x - dx, y))
                    return true;
                if (IsWalkable(x + dx, y - dy) && !IsWalkable(x, y - dy))
                    return true;

                // 情况2：前方存在强制邻居（递归检查）
                // 检查前上方和前右方
                int forwardX = x + dx;
                int forwardY = y + dy;
                if (IsWalkable(forwardX, forwardY))
                {
                    // 检查该点的水平和垂直方向
                    if (HasForcedNeighborInDirection(forwardX, forwardY, GetCardinalDir(dx, 0)))
                        return true;
                    if (HasForcedNeighborInDirection(forwardX, forwardY, GetCardinalDir(0, dy)))
                        return true;
                }
            }
            else if (dx != 0)
            {
                // === 水平移动的强制邻居检查 ===

                // 右侧前进方向
                if (!IsWalkable(x + dx, y))
                    return false;

                // 检查上方是否有强制邻居
                if (IsWalkable(x + dx, y + 1) && !IsWalkable(x, y + 1))
                {
                    // 额外检查：确保这不是通道情况
                    if (IsWalkable(x, y - 1) || IsWalkable(x + dx, y - 1))
                        return true;
                }

                // 检查下方是否有强制邻居
                if (IsWalkable(x + dx, y - 1) && !IsWalkable(x, y - 1))
                {
                    // 额外检查：确保这不是通道情况
                    if (IsWalkable(x, y + 1) || IsWalkable(x + dx, y + 1))
                        return true;
                }
            }
            else if (dy != 0)
            {
                // === 垂直移动的强制邻居检查 ===

                // 上方前进方向
                if (!IsWalkable(x, y + dy))
                    return false;

                // 检查右侧是否有强制邻居
                if (IsWalkable(x + 1, y + dy) && !IsWalkable(x + 1, y))
                {
                    // 额外检查：确保这不是通道情况
                    if (IsWalkable(x - 1, y) || IsWalkable(x - 1, y + dy))
                        return true;
                }

                // 检查左侧是否有强制邻居
                if (IsWalkable(x - 1, y + dy) && !IsWalkable(x - 1, y))
                {
                    // 额外检查：确保这不是通道情况
                    if (IsWalkable(x + 1, y) || IsWalkable(x + 1, y + dy))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 计算直线方向的跳距
        /// </summary>
        private static void CalculateStraightJumpDistances(int dir, int maxX, int maxY)
        {
            int dx = DirX[dir];
            int dy = DirY[dir];

            // 根据方向确定扫描顺序（从边界向内扫描）
            int xStart, xEnd, xStep;
            int yStart, yEnd, yStep;

            if (dx > 0) { xStart = maxX; xEnd = mapMinX - 1; xStep = -1; }
            else if (dx < 0) { xStart = mapMinX; xEnd = maxX + 1; xStep = 1; }
            else { xStart = mapMinX; xEnd = maxX; xStep = 1; }

            if (dy > 0) { yStart = maxY; yEnd = mapMinY - 1; yStep = -1; }
            else if (dy < 0) { yStart = mapMinY; yEnd = maxY + 1; yStep = 1; }
            else { yStart = mapMinY; yEnd = maxY; yStep = 1; }

            if (dx != 0)
            {
                // 水平扫描：每行独立
                for (int y = yStart; IsInRange(y, yStart, yEnd, yStep); y += yStep)
                {
                    if (y < mapMinY || y > mapMaxY) continue;

                    int distance = 0;
                    for (int x = xStart; IsInRange(x, xStart, xEnd, xStep); x += xStep)
                    {
                        if (x < mapMinX || x > mapMaxX) continue;

                        if (!IsWalkable(x, y))
                        {
                            distance = 0;
                            continue;
                        }

                        long key = AStarLogic.EncodeKey(x, y);
                        EnsureJumpDistances(key);

                        int nextX = x - dx; // 上一个格子的位置

                        if (!IsWalkable(nextX, y))
                        {
                            // 紧挨障碍物或边界
                            distance = 1;
                        }
                        else
                        {
                            long prevKey = AStarLogic.EncodeKey(nextX, y);
                            if (jumpDistances.ContainsKey(prevKey))
                            {
                                int prevDist = jumpDistances[prevKey][dir];
                                if (primaryJumpPoints.Contains(key))
                                {
                                    // 主跳点的距离为1
                                    distance = 1;
                                }
                                else if (prevDist > 0)
                                {
                                    // 继承前一个格子的距离+1
                                    distance = prevDist + 1;
                                }
                                else
                                {
                                    distance = 1;
                                }
                            }
                            else
                            {
                                distance = 1;
                            }
                        }

                        jumpDistances[key][dir] = distance;
                    }
                }
            }
            else
            {
                // 垂直扫描：每列独立
                for (int x = xStart; IsInRange(x, xStart, xEnd, xStep); x += xStep)
                {
                    if (x < mapMinX || x > mapMaxX) continue;

                    int distance = 0;
                    for (int y = yStart; IsInRange(y, yStart, yEnd, yStep); y += yStep)
                    {
                        if (y < mapMinY || y > mapMaxY) continue;

                        if (!IsWalkable(x, y))
                        {
                            distance = 0;
                            continue;
                        }

                        long key = AStarLogic.EncodeKey(x, y);
                        EnsureJumpDistances(key);

                        int nextY = y - dy; // 上一个格子的位置

                        if (!IsWalkable(x, nextY))
                        {
                            distance = 1;
                        }
                        else
                        {
                            long prevKey = AStarLogic.EncodeKey(x, nextY);
                            if (jumpDistances.ContainsKey(prevKey))
                            {
                                int prevDist = jumpDistances[prevKey][dir];
                                if (primaryJumpPoints.Contains(key))
                                {
                                    distance = 1;
                                }
                                else if (prevDist > 0)
                                {
                                    distance = prevDist + 1;
                                }
                                else
                                {
                                    distance = 1;
                                }
                            }
                            else
                            {
                                distance = 1;
                            }
                        }

                        jumpDistances[key][dir] = distance;
                    }
                }
            }
        }

        /// <summary>
        /// 计算对角线方向的跳距
        /// </summary>
        private static void CalculateDiagonalJumpDistances(int dir, int maxX, int maxY)
        {
            int dx = DirX[dir];
            int dy = DirY[dir];

            // 从边界向内扫描
            int xStart, xEnd, xStep;
            int yStart, yEnd, yStep;

            if (dx > 0) { xStart = maxX; xEnd = mapMinX - 1; xStep = -1; }
            else { xStart = mapMinX; xEnd = maxX + 1; xStep = 1; }

            if (dy > 0) { yStart = maxY; yEnd = mapMinY - 1; yStep = -1; }
            else { yStart = mapMinY; yEnd = maxY + 1; yStep = 1; }

            for (int y = yStart; IsInRange(y, yStart, yEnd, yStep); y += yStep)
            {
                if (y < mapMinY || y > mapMaxY) continue;

                for (int x = xStart; IsInRange(x, xStart, xEnd, xStep); x += xStep)
                {
                    if (x < mapMinX || x > mapMaxX) continue;

                    if (!IsWalkable(x, y))
                        continue;

                    long key = AStarLogic.EncodeKey(x, y);
                    EnsureJumpDistances(key);

                    int nextX = x + dx;
                    int nextY = y + dy;

                    // 下一个格子出界或不可走 -> 距离0
                    if (nextX < mapMinX || nextX > mapMaxX || nextY < mapMinY || nextY > mapMaxY ||
                        !IsWalkable(nextX, nextY))
                    {
                        jumpDistances[key][dir] = 0;
                        continue;
                    }

                    // 对角线移动：需要至少一个相邻方向可行走
                    bool adj1 = IsWalkable(x + dx, y);
                    bool adj2 = IsWalkable(x, y + dy);

                    if (!adj1 && !adj2)
                    {
                        // 墙角，不能对角线移动
                        jumpDistances[key][dir] = 0;
                        continue;
                    }

                    long nextKey = AStarLogic.EncodeKey(nextX, nextY);

                    if (!jumpDistances.ContainsKey(nextKey))
                    {
                        jumpDistances[key][dir] = 0;
                        continue;
                    }

                    // 获取水平和垂直分量方向的跳距
                    int horizDir = GetCardinalDir(dx, 0);
                    int vertDir = GetCardinalDir(0, dy);

                    int horizDist = jumpDistances[nextKey][horizDir];
                    int vertDist = jumpDistances[nextKey][vertDir];

                    if (primaryJumpPoints.Contains(key))
                    {
                        // 主跳点：检查是否需要在此转向
                        if (horizDist > 0 || vertDist > 0)
                        {
                            jumpDistances[key][dir] = 1;
                        }
                        else
                        {
                            int nextDist = jumpDistances[nextKey][dir];
                            jumpDistances[key][dir] = nextDist > 0 ? nextDist + 1 : 0;
                        }
                    }
                    else
                    {
                        // 非主跳点
                        if (horizDist > 0 || vertDist > 0)
                        {
                            // 直线方向有跳点，需要在此转向
                            jumpDistances[key][dir] = 1;
                        }
                        else
                        {
                            // 继承下一个格子的距离+1
                            int nextDist = jumpDistances[nextKey][dir];
                            jumpDistances[key][dir] = nextDist > 0 ? nextDist + 1 : 0;
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

        /// <summary>
        /// 检查是否在扫描范围内
        /// </summary>
        private static bool IsInRange(int value, int start, int end, int step)
        {
            if (step > 0)
                return value <= end;
            else
                return value >= end;
        }

        #endregion

        #region 寻路

        public static List<long> FindPath(int startX, int startY, int goalX, int goalY,
            float actionPoints = -1f, bool allowDiagonal = true)
        {
            if (jumpDistances == null || jumpDistances.Count == 0)
            {
                Debug.LogError("[JPS+] 跳距表未初始化！");
                return new List<long> { AStarLogic.EncodeKey(startX, startY) };
            }

            long start = AStarLogic.EncodeKey(startX, startY);
            long goal = AStarLogic.EncodeKey(goalX, goalY);

            if (!jumpDistances.ContainsKey(start))
            {
                Debug.LogError($"[JPS+] 起点不在跳距表中: ({startX},{startY})");
                return new List<long> { start };
            }

            if (!jumpDistances.ContainsKey(goal))
            {
                goal = FindNearestWalkable(goalX, goalY);
                if (goal == 0)
                {
                    Debug.LogWarning($"[JPS+] 找不到终点附近的可行走节点");
                    return new List<long> { start };
                }
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

            // 时间保护
            float startTime = Time.realtimeSinceStartup;
            float maxTimeMs = 5f;

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
                        Debug.Log($"[JPS+] 寻路成功: {iterations} 次迭代");
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

                // 获取父节点方向进行剪枝
                long parent = 0;
                if (current != start)
                {
                    cameFrom.TryGetValue(current, out parent);
                }

                IdentifySuccessors(current, parent, goalX, goalY, gScore, cameFrom,
                    hCache, openSet, closedSet, actionPoints, allowDiagonal);

                // 时间保护检查
                if (iterations % 500 == 0)
                {
                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                    if (elapsed > maxTimeMs)
                    {
                        Debug.LogWarning($"[JPS+] 超时: {iterations} 次迭代, {elapsed:F1}ms");
                        break;
                    }
                }
            }

            if (bestNodeInRange != start)
            {
                Debug.Log($"[JPS+] 返回最优可达节点 (距离目标最近)");
                return ReconstructPath(cameFrom, start, bestNodeInRange);
            }

            Debug.LogWarning("[JPS+] 寻路失败，返回起点");
            return new List<long> { start };
        }

        /// <summary>
        /// 识别后继节点（带方向剪枝）
        /// </summary>
        private static void IdentifySuccessors(long current, long parent, int goalX, int goalY,
            Dictionary<long, float> gScore, Dictionary<long, long> cameFrom,
            Dictionary<long, float> hCache, PriorityQueue<long> openSet,
            HashSet<long> closedSet, float actionPoints, bool allowDiagonal)
        {
            if (!jumpDistances.TryGetValue(current, out int[] distances))
                return;

            float currentG = gScore[current];
            Vector2Int curPos = AStarLogic.DecodeKey(current);

            if (parent == 0)
            {
                // 起点：探索所有方向
                int maxDir = allowDiagonal ? 8 : 4;
                for (int dir = 0; dir < maxDir; dir++)
                {
                    TryJumpInDirection(current, curPos, dir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
            }
            else
            {
                // 非起点：根据来向进行剪枝
                Vector2Int parentPos = AStarLogic.DecodeKey(parent);
                int dirX = (int)Mathf.Sign(curPos.x - parentPos.x);
                int dirY = (int)Mathf.Sign(curPos.y - parentPos.y);

                int parentDir = GetDirectionIndex(dirX, dirY);

                if (dirX != 0 && dirY != 0)
                {
                    // 对角线来向
                    // 1. 继续对角线方向
                    TryJumpInDirection(current, curPos, parentDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);

                    // 2. 水平分量方向
                    int horizDir = GetCardinalDir(dirX, 0);
                    TryJumpInDirection(current, curPos, horizDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);

                    // 3. 垂直分量方向
                    int vertDir = GetCardinalDir(0, dirY);
                    TryJumpInDirection(current, curPos, vertDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
                else
                {
                    // 直线来向
                    // 1. 继续直线方向
                    TryJumpInDirection(current, curPos, parentDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);

                    // 2. 检查强制邻居方向
                    if (allowDiagonal)
                    {
                        CheckForcedNeighborDirections(current, curPos, dirX, dirY, distances,
                            goalX, goalY, currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                    }
                }
            }
        }

        /// <summary>
        /// 尝试在指定方向跳跃并添加后继节点
        /// </summary>
        private static void TryJumpInDirection(long current, Vector2Int curPos, int dir,
            int[] distances, int goalX, int goalY, float currentG,
            Dictionary<long, float> gScore, Dictionary<long, long> cameFrom,
            Dictionary<long, float> hCache, PriorityQueue<long> openSet,
            HashSet<long> closedSet, float actionPoints)
        {
            int jumpDist = distances[dir];

            if (jumpDist <= 0)
                return;

            int jumpX = curPos.x + DirX[dir] * jumpDist;
            int jumpY = curPos.y + DirY[dir] * jumpDist;
            long jumpNode = AStarLogic.EncodeKey(jumpX, jumpY);

            if (!IsWalkable(jumpX, jumpY))
                return;

            if (closedSet.Contains(jumpNode))
                return;

            bool isDiagonal = (DirX[dir] != 0 && DirY[dir] != 0);
            float moveCost = isDiagonal ? jumpDist * 1.41421356f : jumpDist;
            float tentativeG = currentG + moveCost;

            if (actionPoints >= 0 && tentativeG > actionPoints + 0.0001f)
                return;

            if (gScore.TryGetValue(jumpNode, out float existingG) &&
                tentativeG >= existingG - 0.0001f)
                return;

            gScore[jumpNode] = tentativeG;
            cameFrom[jumpNode] = current;

            if (!hCache.TryGetValue(jumpNode, out float hValue))
            {
                hValue = Heuristic(jumpX, jumpY, goalX, goalY, true);
                hCache[jumpNode] = hValue;
            }

            openSet.Enqueue(jumpNode, tentativeG + hValue);
        }

        /// <summary>
        /// 检查强制邻居方向并添加跳点
        /// </summary>
        private static void CheckForcedNeighborDirections(long current, Vector2Int curPos,
            int dirX, int dirY, int[] distances, int goalX, int goalY, float currentG,
            Dictionary<long, float> gScore, Dictionary<long, long> cameFrom,
            Dictionary<long, float> hCache, PriorityQueue<long> openSet,
            HashSet<long> closedSet, float actionPoints)
        {
            if (dirX != 0)
            {
                // 水平移动，检查上下对角线方向
                if (IsWalkable(curPos.x + dirX, curPos.y + 1) && !IsWalkable(curPos.x, curPos.y + 1))
                {
                    int forcedDir = GetDirectionIndex(dirX, 1);
                    TryJumpInDirection(current, curPos, forcedDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
                if (IsWalkable(curPos.x + dirX, curPos.y - 1) && !IsWalkable(curPos.x, curPos.y - 1))
                {
                    int forcedDir = GetDirectionIndex(dirX, -1);
                    TryJumpInDirection(current, curPos, forcedDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
            }
            else if (dirY != 0)
            {
                // 垂直移动，检查左右对角线方向
                if (IsWalkable(curPos.x + 1, curPos.y + dirY) && !IsWalkable(curPos.x + 1, curPos.y))
                {
                    int forcedDir = GetDirectionIndex(1, dirY);
                    TryJumpInDirection(current, curPos, forcedDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
                if (IsWalkable(curPos.x - 1, curPos.y + dirY) && !IsWalkable(curPos.x - 1, curPos.y))
                {
                    int forcedDir = GetDirectionIndex(-1, dirY);
                    TryJumpInDirection(current, curPos, forcedDir, distances, goalX, goalY,
                        currentG, gScore, cameFrom, hCache, openSet, closedSet, actionPoints);
                }
            }
        }

        /// <summary>
        /// 根据方向向量获取方向索引
        /// </summary>
        private static int GetDirectionIndex(int dx, int dy)
        {
            if (dx > 0 && dy == 0) return DIR_E;
            if (dx < 0 && dy == 0) return DIR_W;
            if (dx == 0 && dy > 0) return DIR_N;
            if (dx == 0 && dy < 0) return DIR_S;
            if (dx > 0 && dy > 0) return DIR_NE;
            if (dx > 0 && dy < 0) return DIR_SE;
            if (dx < 0 && dy > 0) return DIR_NW;
            if (dx < 0 && dy < 0) return DIR_SW;
            return -1;
        }

        #endregion

        #region 路径重构

        /// <summary>
        /// 重构完整路径（包含插值）
        /// </summary>
        private static List<long> ReconstructPath(Dictionary<long, long> cameFrom, long start, long goal)
        {
            // 第一步：收集跳点
            var jumpPoints = new List<long>();
            long current = goal;
            var visited = new HashSet<long>();
            int iterations = 0;

            while (current != start && iterations < 10000)
            {
                iterations++;
                if (visited.Contains(current))
                {
                    Debug.LogWarning("[JPS+] 路径重构检测到循环");
                    break;
                }

                jumpPoints.Add(current);
                visited.Add(current);

                if (!cameFrom.TryGetValue(current, out long parent))
                {
                    Debug.LogWarning($"[JPS+] 路径重构断链: 找不到父节点");
                    break;
                }

                if (parent == current)
                {
                    Debug.LogWarning("[JPS+] 路径重构异常: 父节点等于当前节点");
                    break;
                }

                current = parent;
            }

            jumpPoints.Add(start);
            jumpPoints.Reverse();

            Debug.Log($"[JPS+] 跳点数量: {jumpPoints.Count}");

            // 第二步：在跳点间插值生成完整路径
            var fullPath = new List<long>();
            for (int i = 0; i < jumpPoints.Count - 1; i++)
            {
                Vector2Int from = AStarLogic.DecodeKey(jumpPoints[i]);
                Vector2Int to = AStarLogic.DecodeKey(jumpPoints[i + 1]);
                InterpolatePath(from, to, fullPath);
            }

            // 确保终点在路径中
            if (fullPath.Count == 0 || fullPath[fullPath.Count - 1] != goal)
            {
                fullPath.Add(goal);
            }

            Debug.Log($"[JPS+] 完整路径长度: {fullPath.Count}");
            return fullPath;
        }

        /// <summary>
        /// 在两个跳点之间插值生成路径
        /// </summary>
        private static void InterpolatePath(Vector2Int from, Vector2Int to, List<long> path)
        {
            int curX = from.x;
            int curY = from.y;
            int targetX = to.x;
            int targetY = to.y;

            // 添加起点
            if (path.Count == 0 || path[path.Count - 1] != AStarLogic.EncodeKey(curX, curY))
            {
                path.Add(AStarLogic.EncodeKey(curX, curY));
            }

            while (curX != targetX || curY != targetY)
            {
                int stepX = (int)Mathf.Sign(targetX - curX);
                int stepY = (int)Mathf.Sign(targetY - curY);

                if (stepX != 0 && stepY != 0)
                {
                    // 对角线移动
                    int diagX = curX + stepX;
                    int diagY = curY + stepY;

                    bool diagWalkable = IsWalkable(diagX, diagY);
                    bool adjXWalkable = IsWalkable(curX + stepX, curY);
                    bool adjYWalkable = IsWalkable(curX, curY + stepY);

                    if (diagWalkable && (adjXWalkable || adjYWalkable))
                    {
                        curX = diagX;
                        curY = diagY;
                    }
                    else if (adjXWalkable)
                    {
                        curX += stepX;
                    }
                    else if (adjYWalkable)
                    {
                        curY += stepY;
                    }
                    else
                    {
                        Debug.LogWarning($"[JPS+] 路径插值失败: ({curX},{curY}) -> ({targetX},{targetY})");
                        break;
                    }
                }
                else
                {
                    // 直线移动
                    curX += stepX;
                    curY += stepY;

                    // 安全检查
                    if (!IsWalkable(curX, curY))
                    {
                        Debug.LogWarning($"[JPS+] 路径插值遇到障碍物: ({curX},{curY})");
                        break;
                    }
                }

                path.Add(AStarLogic.EncodeKey(curX, curY));
            }
        }

        #endregion

        #region 辅助方法

        /// <summary>
        /// 启发式函数
        /// </summary>
        private static float Heuristic(int x, int y, int goalX, int goalY, bool allowDiagonal)
        {
            int dx = Math.Abs(x - goalX);
            int dy = Math.Abs(y - goalY);

            float h;
            if (allowDiagonal)
            {
                if (dx > dy)
                    h = dx + dy * 0.41421356f;
                else
                    h = dy + dx * 0.41421356f;
            }
            else
            {
                h = dx + dy;
            }

            // 轻微打破平局，减少搜索节点
            return h * 1.0001f;
        }

        /// <summary>
        /// 检查节点是否可行走
        /// </summary>
        private static bool IsWalkable(int x, int y)
        {
            // 边界检查
            if (boundsInitialized)
            {
                if (x < mapMinX || x > mapMaxX || y < mapMinY || y > mapMaxY)
                    return false;
            }

            long key = AStarLogic.EncodeKey(x, y);
            if (!MapCache.ContainsKey(key))
                return false;

            TileInfo tile = MapCache.GetTile(key);
            return tile != null && tile.cost > 0;
        }

        /// <summary>
        /// 查找最近的可行走节点
        /// </summary>
        private static long FindNearestWalkable(int x, int y)
        {
            // 螺旋搜索
            for (int r = 1; r <= 10; r++)
            {
                for (int dx = -r; dx <= r; dx++)
                {
                    for (int dy = -r; dy <= r; dy++)
                    {
                        if (Math.Abs(dx) + Math.Abs(dy) > r * 2)
                            continue;

                        int nx = x + dx;
                        int ny = y + dy;

                        if (IsWalkable(nx, ny))
                        {
                            Debug.Log($"[JPS+] 找到最近可行走节点: ({nx},{ny}) 距离 ({x},{y}) 为 {r}");
                            return AStarLogic.EncodeKey(nx, ny);
                        }
                    }
                }
            }

            Debug.LogWarning($"[JPS+] 未找到终点附近的可行走节点");
            return 0;
        }

        /// <summary>
        /// 清理预处理数据
        /// </summary>
        public static void ClearPreprocessedData()
        {
            jumpDistances?.Clear();
            jumpDistances = null;
            primaryJumpPoints?.Clear();
            primaryJumpPoints = null;
            Debug.Log("[JPS+] 预处理数据已清理");
        }

        #endregion
    }
}