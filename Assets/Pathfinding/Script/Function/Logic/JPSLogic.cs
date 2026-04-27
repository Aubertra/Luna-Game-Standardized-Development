using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public static class JPSLogic
    {
        // ===== 地图边界 =====
        private static int mapMinX, mapMinY, mapMaxX, mapMaxY;
        private static bool boundsInitialized = false;

        /// <summary>
        /// 初始化地图边界（在 MapCache.InitLunaMap 之后调用一次）
        /// </summary>
        public static void InitBounds()
        {
            mapMinX = MapCache.minX;
            mapMinY = MapCache.minY;
            mapMaxX = MapCache.maxX;
            mapMaxY = MapCache.maxY;
            boundsInitialized = true;
        }

        // ===== 安全的 Sign（Luna 兼容） =====
        private static int Sign(int v)
        {
            if (v > 0) return 1;
            if (v < 0) return -1;
            return 0;
        }

        // ===== 安全的 Abs =====
        private static int Abs(int v)
        {
            return v < 0 ? -v : v;
        }

        // ===== 安全的 Max =====
        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        // ===== IsWalkable 加上边界检查 =====
        private static bool IsWalkable(int x, int y)
        {
            // === 边界检查 ===
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
        /// JPS 寻路主函数
        /// </summary>
        public static List<long> FindPath(int startX, int startY, int goalX, int goalY,
            float actionPoints = -1f, bool allowDiagonal = true)
        {
            long start = AStarLogic.EncodeKey(startX, startY);
            long goal = AStarLogic.EncodeKey(goalX, goalY);

            if (!MapCache.ContainsKey(start) || !MapCache.ContainsKey(goal))
            {
                Debug.LogError("起点或终点不在 MapCache 中！");
                return new List<long>();
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

            int consecutiveFailures = 0;
            int maxFailuresBeforeFallback = 100;

            // ===== 时间保护 =====
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
                        return ReconstructPath(cameFrom, start, goal);
                    }
                    else
                    {
                        break;
                    }
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

                int curX = (int)(current >> 32);
                int curY = (int)(current & 0xFFFFFFFF);

                int successorsBefore = openSet.Count;

                if (current == start)
                {
                    IdentifySuccessors(current, curX, curY, goalX, goalY, gScore, cameFrom,
                        hCache, openSet, closedSet, actionPoints, allowDiagonal);
                }
                else
                {
                    long parent = cameFrom[current];
                    int parentX = (int)(parent >> 32);
                    int parentY = (int)(parent & 0xFFFFFFFF);
                    int dirX = Sign(curX - parentX);
                    int dirY = Sign(curY - parentY);

                    IdentifySuccessors(current, curX, curY, goalX, goalY, gScore, cameFrom,
                        hCache, openSet, closedSet, actionPoints, allowDiagonal, dirX, dirY);
                }

                if (openSet.Count == successorsBefore)
                {
                    consecutiveFailures++;
                    if (consecutiveFailures > maxFailuresBeforeFallback)
                    {
                        ExploreNeighborsFallback(current, curX, curY, goalX, goalY, gScore,
                            cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                        consecutiveFailures = 0;
                    }
                }
                else
                {
                    consecutiveFailures = 0;
                }

                // ===== 时间保护检查 =====
                if (iterations % 500 == 0)
                {
                    float elapsed = (Time.realtimeSinceStartup - startTime) * 1000f;
                    if (elapsed > maxTimeMs)
                    {
                        Debug.LogWarning($"[JPS] 超时: {iterations} 次迭代, {elapsed:F1}ms");
                        break;
                    }
                }
            }

            if (bestNodeInRange != start)
            {
                return ReconstructPath(cameFrom, start, bestNodeInRange);
            }

            return new List<long> { start };
        }

        private static void ExploreNeighborsFallback(long current, int curX, int curY,
            int goalX, int goalY, Dictionary<long, float> gScore,
            Dictionary<long, long> cameFrom, Dictionary<long, float> hCache,
            PriorityQueue<long> openSet, HashSet<long> closedSet,
            float actionPoints, bool allowDiagonal)
        {
            float currentG = gScore[current];
            int[] dirs;

            if (allowDiagonal)
            {
                dirs = new int[] { 0, 1, 1, 1, 1, 0, 1, -1, 0, -1, -1, -1, -1, 0, -1, 1 };
            }
            else
            {
                dirs = new int[] { 0, 1, 1, 0, 0, -1, -1, 0 };
            }

            for (int i = 0; i < dirs.Length; i += 2)
            {
                int nx = curX + dirs[i];
                int ny = curY + dirs[i + 1];

                if (!IsWalkable(nx, ny))
                    continue;

                long neighbor = AStarLogic.EncodeKey(nx, ny);

                if (closedSet.Contains(neighbor))
                    continue;

                // 对角线检查相邻格子
                if (dirs[i] != 0 && dirs[i + 1] != 0)
                {
                    if (!IsWalkable(curX + dirs[i], curY) || !IsWalkable(curX, curY + dirs[i + 1]))
                        continue;
                }

                TileInfo tile = MapCache.GetTile(neighbor);
                float stepCost = (dirs[i] != 0 && dirs[i + 1] != 0) ? 1.414f : 1f;
                float tentativeG = currentG + stepCost * (tile != null ? tile.cost : 1f);

                if (actionPoints >= 0 && tentativeG > actionPoints + 0.0001f)
                    continue;

                if (gScore.TryGetValue(neighbor, out float existingG) && tentativeG >= existingG - 0.0001f)
                    continue;

                gScore[neighbor] = tentativeG;
                cameFrom[neighbor] = current;

                if (!hCache.TryGetValue(neighbor, out float hValue))
                {
                    hValue = Heuristic(nx, ny, goalX, goalY, allowDiagonal);
                    hCache[neighbor] = hValue;
                }

                openSet.Enqueue(neighbor, tentativeG + hValue);
            }
        }

        private static void IdentifySuccessors(long current, int curX, int curY,
            int goalX, int goalY, Dictionary<long, float> gScore,
            Dictionary<long, long> cameFrom, Dictionary<long, float> hCache,
            PriorityQueue<long> openSet, HashSet<long> closedSet,
            float actionPoints, bool allowDiagonal, int parentDirX = 0, int parentDirY = 0)
        {
            if (parentDirX == 0 && parentDirY == 0)
            {
                int[] allDirs = allowDiagonal ?
                    new int[] { 1, 0, -1, 0, 0, 1, 0, -1, 1, 1, -1, 1, 1, -1, -1, -1 } :
                    new int[] { 1, 0, -1, 0, 0, 1, 0, -1 };

                for (int i = 0; i < allDirs.Length; i += 2)
                {
                    SearchJumpPoint(current, curX, curY, allDirs[i], allDirs[i + 1],
                        goalX, goalY, gScore, cameFrom, hCache, openSet, closedSet,
                        actionPoints, allowDiagonal);
                }
            }
            else if (parentDirX != 0 && parentDirY != 0)
            {
                SearchJumpPoint(current, curX, curY, parentDirX, parentDirY, goalX, goalY,
                    gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                SearchJumpPoint(current, curX, curY, parentDirX, 0, goalX, goalY,
                    gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                SearchJumpPoint(current, curX, curY, 0, parentDirY, goalX, goalY,
                    gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
            }
            else
            {
                SearchJumpPoint(current, curX, curY, parentDirX, parentDirY, goalX, goalY,
                    gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);

                if (allowDiagonal)
                {
                    if (parentDirX != 0)
                    {
                        if (IsWalkable(curX + parentDirX, curY + 1) && !IsWalkable(curX, curY + 1))
                            SearchJumpPoint(current, curX, curY, parentDirX, 1, goalX, goalY,
                                gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                        if (IsWalkable(curX + parentDirX, curY - 1) && !IsWalkable(curX, curY - 1))
                            SearchJumpPoint(current, curX, curY, parentDirX, -1, goalX, goalY,
                                gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                    }
                    else
                    {
                        if (IsWalkable(curX + 1, curY + parentDirY) && !IsWalkable(curX + 1, curY))
                            SearchJumpPoint(current, curX, curY, 1, parentDirY, goalX, goalY,
                                gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                        if (IsWalkable(curX - 1, curY + parentDirY) && !IsWalkable(curX - 1, curY))
                            SearchJumpPoint(current, curX, curY, -1, parentDirY, goalX, goalY,
                                gScore, cameFrom, hCache, openSet, closedSet, actionPoints, allowDiagonal);
                    }
                }
            }
        }

        private static void SearchJumpPoint(long fromNode, int fromX, int fromY,
            int dirX, int dirY, int goalX, int goalY,
            Dictionary<long, float> gScore, Dictionary<long, long> cameFrom,
            Dictionary<long, float> hCache, PriorityQueue<long> openSet,
            HashSet<long> closedSet, float actionPoints, bool allowDiagonal)
        {
            int jumpX = fromX + dirX;
            int jumpY = fromY + dirY;

            if (!IsWalkable(jumpX, jumpY))
                return;

            if (dirX != 0 && dirY != 0)
            {
                bool adjX = IsWalkable(fromX + dirX, fromY);
                bool adjY = IsWalkable(fromX, fromY + dirY);
                // 宽松通过（桥梁/窄道场景）
            }

            var jumpResult = Jump(jumpX, jumpY, dirX, dirY, goalX, goalY, allowDiagonal);
            if (jumpResult.HasValue)
            {
                long jumpNode = AStarLogic.EncodeKey(jumpResult.Value.x, jumpResult.Value.y);

                if (closedSet.Contains(jumpNode))
                    return;

                float fromG = gScore[fromNode];
                int dx = jumpResult.Value.x - fromX;
                int dy = jumpResult.Value.y - fromY;
                int steps = Max(Abs(dx), Abs(dy));
                float moveCost = (dirX != 0 && dirY != 0) ? steps * 1.414f : steps;

                float tentativeG = fromG + moveCost;

                if (actionPoints >= 0 && tentativeG > actionPoints + 0.0001f)
                    return;

                if (gScore.TryGetValue(jumpNode, out float existingG) && tentativeG >= existingG - 0.0001f)
                    return;

                gScore[jumpNode] = tentativeG;
                cameFrom[jumpNode] = fromNode;

                if (!hCache.TryGetValue(jumpNode, out float hValue))
                {
                    hValue = Heuristic(jumpResult.Value.x, jumpResult.Value.y, goalX, goalY, allowDiagonal);
                    hCache[jumpNode] = hValue;
                }

                openSet.Enqueue(jumpNode, tentativeG + hValue);
            }
        }

        private static Vector2Int? Jump(int x, int y, int dirX, int dirY,
            int goalX, int goalY, bool allowDiagonal)
        {
            // ===== 越界检查 =====
            if (!IsWalkable(x, y))
                return null;

            if (x == goalX && y == goalY)
                return new Vector2Int(x, y);

            if (dirX != 0 && dirY != 0)
            {
                bool hasHorizontalJump = Jump(x, y, dirX, 0, goalX, goalY, allowDiagonal).HasValue;
                bool hasVerticalJump = Jump(x, y, 0, dirY, goalX, goalY, allowDiagonal).HasValue;

                if (hasHorizontalJump || hasVerticalJump)
                    return new Vector2Int(x, y);

                if (HasForcedNeighbor(x, y, dirX, dirY))
                    return new Vector2Int(x, y);

                int nextX = x + dirX;
                int nextY = y + dirY;

                bool adjX = IsWalkable(x + dirX, y);
                bool adjY = IsWalkable(x, y + dirY);

                if (adjX || adjY)
                {
                    if (IsWalkable(nextX, nextY))
                    {
                        return Jump(nextX, nextY, dirX, dirY, goalX, goalY, allowDiagonal);
                    }
                }

                return null;
            }
            else
            {
                if (allowDiagonal && HasForcedNeighbor(x, y, dirX, dirY))
                    return new Vector2Int(x, y);

                return Jump(x + dirX, y + dirY, dirX, dirY, goalX, goalY, allowDiagonal);
            }
        }

        private static bool HasForcedNeighbor(int x, int y, int dirX, int dirY)
        {
            if (dirX != 0 && dirY != 0)
            {
                if (IsWalkable(x - dirX, y) && !IsWalkable(x - dirX, y + dirY))
                    return true;
                if (IsWalkable(x, y - dirY) && !IsWalkable(x + dirX, y - dirY))
                    return true;
            }
            else if (dirX != 0)
            {
                if (IsWalkable(x + dirX, y + 1) && !IsWalkable(x, y + 1))
                    return true;
                if (IsWalkable(x + dirX, y - 1) && !IsWalkable(x, y - 1))
                    return true;
            }
            else if (dirY != 0)
            {
                if (IsWalkable(x + 1, y + dirY) && !IsWalkable(x + 1, y))
                    return true;
                if (IsWalkable(x - 1, y + dirY) && !IsWalkable(x - 1, y))
                    return true;
            }

            return false;
        }

        public static List<long> ReconstructPath(Dictionary<long, long> cameFrom, long start, long goal)
        {
            var path = new List<long>();
            long current = goal;
            var visited = new HashSet<long>();
            int maxIterations = 10000;
            int iterations = 0;

            var jumpPoints = new List<long>();
            while (current != start && iterations < maxIterations)
            {
                iterations++;

                if (visited.Contains(current))
                    break;

                jumpPoints.Add(current);
                visited.Add(current);

                if (!cameFrom.TryGetValue(current, out long parent))
                    break;

                if (parent == current)
                    break;

                current = parent;
            }

            jumpPoints.Add(start);
            jumpPoints.Reverse();

            for (int i = 0; i < jumpPoints.Count - 1; i++)
            {
                long from = jumpPoints[i];
                long to = jumpPoints[i + 1];

                Vector2Int fromPos = AStarLogic.DecodeKey(from);
                Vector2Int toPos = AStarLogic.DecodeKey(to);

                ExpandPathSafe(fromPos, toPos, path);
            }

            return path;
        }

        private static void ExpandPathSafe(Vector2Int from, Vector2Int to, List<long> path)
        {
            int curX = from.x;
            int curY = from.y;
            int targetX = to.x;
            int targetY = to.y;

            if (curX == targetX && curY == targetY)
            {
                if (path.Count == 0 || path[path.Count - 1] != AStarLogic.EncodeKey(curX, curY))
                {
                    path.Add(AStarLogic.EncodeKey(curX, curY));
                }
                return;
            }

            if (path.Count == 0 || path[path.Count - 1] != AStarLogic.EncodeKey(curX, curY))
            {
                path.Add(AStarLogic.EncodeKey(curX, curY));
            }

            while (curX != targetX || curY != targetY)
            {
                int stepX = Sign(targetX - curX);
                int stepY = Sign(targetY - curY);

                if (stepX != 0 && stepY != 0)
                {
                    int diagX = curX + stepX;
                    int diagY = curY + stepY;

                    bool diagWalkable = IsWalkable(diagX, diagY);
                    bool adjXWalkable = IsWalkable(curX + stepX, curY);
                    bool adjYWalkable = IsWalkable(curX, curY + stepY);

                    if (diagWalkable && (adjXWalkable || adjYWalkable))
                    {
                        curX = diagX;
                        curY = diagY;
                        path.Add(AStarLogic.EncodeKey(curX, curY));
                        continue;
                    }
                    else
                    {
                        if (adjXWalkable)
                        {
                            curX += stepX;
                            path.Add(AStarLogic.EncodeKey(curX, curY));
                            continue;
                        }
                        else if (adjYWalkable)
                        {
                            curY += stepY;
                            path.Add(AStarLogic.EncodeKey(curX, curY));
                            continue;
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    curX += stepX;
                    curY += stepY;
                    path.Add(AStarLogic.EncodeKey(curX, curY));
                }
            }
        }

        public static float Heuristic(int x, int y, int goalX, int goalY, bool allowDiagonal)
        {
            int dx = x - goalX;
            int dy = y - goalY;
            if (dx < 0) dx = -dx;
            if (dy < 0) dy = -dy;

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

            return h * 1.0001f;
        }
    }
}