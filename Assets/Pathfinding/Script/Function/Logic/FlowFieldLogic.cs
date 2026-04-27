using System;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public static class FlowFieldLogic
    {
        internal static readonly int[] DirX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        internal static readonly int[] DirY = { 1, 1, 0, -1, -1, -1, 0, 1 };

        public const int DIR_N = 0;
        public const int DIR_NE = 1;
        public const int DIR_E = 2;
        public const int DIR_SE = 3;
        public const int DIR_S = 4;
        public const int DIR_SW = 5;
        public const int DIR_W = 6;
        public const int DIR_NW = 7;

        // === 多个流场实例，使用目标Transform的InstanceID作为Key ===
        private static Dictionary<int, FlowFieldInstance> flowFields = new Dictionary<int, FlowFieldInstance>();

        // === Agent历史记录 ===
        private static Dictionary<int, AgentHistory> agentHistory = new Dictionary<int, AgentHistory>();

        // === 目标Transform缓存（用于自动更新目标位置） ===
        private static Dictionary<int, Transform> targetTransforms = new Dictionary<int, Transform>();

        private static int mapMinX, mapMinY, mapMaxX, mapMaxY;
        private static bool boundsInitialized;

        private const int MAX_UPDATE_PER_FRAME = 3;

        #region 流场实例类

        public class FlowFieldInstance
        {
            public enum BuildState { Idle, BfsQueueing, FlowBuilding, Ready }

            public Dictionary<long, ushort> costField;
            public Dictionary<long, byte> flowField;
            public Transform target;  // 目标Transform
            public long currentGoalId;  // 当前目标Tile的ID
            public Vector2Int currentGoalPos;  // 当前目标位置
            public bool fieldValid;
            public BuildState state = BuildState.Idle;
            public Queue<long> bfsQueue;
            public List<long> allReachableNodes;
            public int flowBuildIndex;
            public int bfsFrameCount;
            public float createTime;
            public bool allowDiagonal;

            public const int BFS_BUDGET = 100;
            public const int FLOW_BUDGET = 150;
            public const int MAX_BFS_FRAMES = 100;

            public bool IsValid => fieldValid && flowField != null;
            public bool IsReady => state == BuildState.Ready;
            public bool IsBuilding => state == BuildState.BfsQueueing || state == BuildState.FlowBuilding;
            public Vector2Int GoalPosition => currentGoalPos;
            public Transform Target => target;

            public FlowFieldInstance(Transform targetTransform, bool diagonal)
            {
                target = targetTransform;
                allowDiagonal = diagonal;
                createTime = Time.time;

                int cap = MapCache.mapCache.Count;
                costField = new Dictionary<long, ushort>(cap);
                flowField = new Dictionary<long, byte>(cap);
                bfsQueue = new Queue<long>(cap);
                allReachableNodes = new List<long>(cap);
            }

            /// <summary>
            /// 根据目标Transform当前位置初始化BFS
            /// </summary>
            public bool InitFromTarget()
            {
                if (target == null) return false;

                var targetTile = MapCache.GetNearlyTile(target);
                if (targetTile == null) return false;

                currentGoalId = targetTile.identifier;
                currentGoalPos = AStarLogic.DecodeKey(currentGoalId);

                // 清空之前的数据
                if (costField != null)
                    costField.Clear();
                else
                    costField = new Dictionary<long, ushort>();

                if (flowField != null)
                    flowField.Clear();
                else
                    flowField = new Dictionary<long, byte>();

                if (bfsQueue != null)
                    bfsQueue.Clear();
                else
                    bfsQueue = new Queue<long>();

                if (allReachableNodes != null)
                    allReachableNodes.Clear();
                else
                    allReachableNodes = new List<long>();

                // 初始化目标点
                costField[currentGoalId] = 1;
                allReachableNodes.Add(currentGoalId);
                bfsQueue.Enqueue(currentGoalId);
                state = BuildState.BfsQueueing;
                fieldValid = false;
                bfsFrameCount = 0;
                flowBuildIndex = 0;

                return true;
            }

            /// <summary>
            /// 检查目标是否移动，如果移动则重新构建
            /// </summary>
            public bool CheckTargetMoved()
            {
                if (target == null) return false;

                var targetTile = MapCache.GetNearlyTile(target);
                if (targetTile == null) return false;

                if (targetTile.identifier != currentGoalId)
                {
                    // 目标移动到了新的Tile，重新初始化
                    return InitFromTarget();
                }
                return false;
            }

            public bool UpdateBuild()
            {
                switch (state)
                {
                    case BuildState.BfsQueueing:
                        ProcessBfsSlice();
                        return false;
                    case BuildState.FlowBuilding:
                        ProcessFlowSlice();
                        return false;
                    case BuildState.Ready:
                        return true;
                    default:
                        return false;
                }
            }

            private void ProcessBfsSlice()
            {
                if (bfsQueue == null || bfsQueue.Count == 0)
                {
                    state = BuildState.FlowBuilding;
                    flowBuildIndex = 0;
                    return;
                }

                bfsFrameCount++;
                if (bfsFrameCount > MAX_BFS_FRAMES)
                {
                    state = BuildState.FlowBuilding;
                    flowBuildIndex = 0;
                    return;
                }

                int maxDir = allowDiagonal ? 8 : 4;
                int budget = BFS_BUDGET;

                while (budget > 0 && bfsQueue.Count > 0)
                {
                    long current = bfsQueue.Dequeue();
                    ushort curCost = costField[current];
                    Vector2Int pos = AStarLogic.DecodeKey(current);

                    for (int dir = 0; dir < maxDir; dir++)
                    {
                        int nx = pos.x + DirX[dir];
                        int ny = pos.y + DirY[dir];

                        if (!IsWalkableStatic(nx, ny))
                            continue;

                        if ((dir & 1) == 1)
                        {
                            int hDir, vDir;
                            if (dir == DIR_NE) { hDir = DIR_E; vDir = DIR_N; }
                            else if (dir == DIR_SE) { hDir = DIR_E; vDir = DIR_S; }
                            else if (dir == DIR_SW) { hDir = DIR_W; vDir = DIR_S; }
                            else { hDir = DIR_W; vDir = DIR_N; }

                            if (!IsWalkableStatic(pos.x + DirX[hDir], pos.y + DirY[hDir]) ||
                                !IsWalkableStatic(pos.x + DirX[vDir], pos.y + DirY[vDir]))
                                continue;
                        }

                        long neighbor = AStarLogic.EncodeKey(nx, ny);

                        if (!costField.ContainsKey(neighbor))
                        {
                            costField[neighbor] = (ushort)(curCost + 1);
                            bfsQueue.Enqueue(neighbor);
                            allReachableNodes.Add(neighbor);
                        }
                    }

                    budget--;
                }
            }

            private void ProcessFlowSlice()
            {
                if (allReachableNodes == null || flowBuildIndex >= allReachableNodes.Count)
                {
                    state = BuildState.Ready;
                    fieldValid = true;
                    bfsQueue = null;
                    return;
                }

                int maxDir = allowDiagonal ? 8 : 4;
                int budget = FLOW_BUDGET;
                int end = flowBuildIndex + budget;
                if (end > allReachableNodes.Count) end = allReachableNodes.Count;

                for (int i = flowBuildIndex; i < end; i++)
                {
                    long key = allReachableNodes[i];
                    ushort curCost = costField[key];

                    if (curCost == 1)
                    {
                        flowField[key] = DIR_N;
                        continue;
                    }

                    Vector2Int pos = AStarLogic.DecodeKey(key);
                    ushort bestCost = ushort.MaxValue;
                    byte bestDir = DIR_N;

                    for (int dir = 0; dir < maxDir; dir++)
                    {
                        int nx = pos.x + DirX[dir];
                        int ny = pos.y + DirY[dir];

                        if (!IsWalkableStatic(nx, ny))
                            continue;

                        if ((dir & 1) == 1)
                        {
                            int hDir, vDir;
                            if (dir == DIR_NE) { hDir = DIR_E; vDir = DIR_N; }
                            else if (dir == DIR_SE) { hDir = DIR_E; vDir = DIR_S; }
                            else if (dir == DIR_SW) { hDir = DIR_W; vDir = DIR_S; }
                            else { hDir = DIR_W; vDir = DIR_N; }

                            if (!IsWalkableStatic(pos.x + DirX[hDir], pos.y + DirY[hDir]) ||
                                !IsWalkableStatic(pos.x + DirX[vDir], pos.y + DirY[vDir]))
                                continue;
                        }

                        long neighbor = AStarLogic.EncodeKey(nx, ny);

                        if (costField.TryGetValue(neighbor, out ushort nCost))
                        {
                            if (nCost < bestCost)
                            {
                                bestCost = nCost;
                                bestDir = (byte)dir;
                            }
                        }
                    }

                    flowField[key] = bestDir;
                }

                flowBuildIndex = end;
            }

            public int GetFlowDirection(int x, int y)
            {
                if (!fieldValid || flowField == null) return -1;
                if (flowField.TryGetValue(AStarLogic.EncodeKey(x, y), out byte dir)) return dir;
                return -1;
            }

            public bool CanReach(int x, int y)
            {
                return fieldValid && costField != null && costField.ContainsKey(AStarLogic.EncodeKey(x, y));
            }

            public int GetDistance(int x, int y)
            {
                if (!fieldValid || costField == null) return -1;
                if (costField.TryGetValue(AStarLogic.EncodeKey(x, y), out ushort c)) return c - 1;
                return -1;
            }

            public float BuildProgress
            {
                get
                {
                    if (state == BuildState.Ready) return 1f;
                    if (state == BuildState.Idle) return 0f;
                    if (state == BuildState.FlowBuilding && allReachableNodes != null && allReachableNodes.Count > 0)
                        return 0.5f + 0.5f * (flowBuildIndex / (float)allReachableNodes.Count);
                    return 0.1f;
                }
            }
        }

        #endregion

        #region Agent历史结构

        private struct AgentHistory
        {
            public int lastDirection;
            public long lastTileId;
            public int targetInstanceId;  // 目标Transform的InstanceID
            public long targetGoalId;     // 目标Tile的ID
            public float lastValidTime;
        }

        #endregion

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

        #region 多流场管理

        /// <summary>
        /// 为指定目标Transform请求构建流场
        /// </summary>
        public static void RequestBuild(Transform target, bool allowDiagonal = true)
        {
            if (target == null)
            {
                Debug.LogError("[FlowField] 目标Transform为null！");
                return;
            }

            int targetId = target.GetInstanceID();

            // 如果该目标的流场已经构建完成，检查是否需要更新
            if (flowFields.TryGetValue(targetId, out var existingField))
            {
                if (existingField.fieldValid && existingField.state == FlowFieldInstance.BuildState.Ready)
                {
                    // 检查目标是否移动
                    existingField.CheckTargetMoved();
                    return;
                }
            }

            // 验证目标位置
            var targetTile = MapCache.GetNearlyTile(target);
            if (targetTile == null)
            {
                Debug.LogError($"[FlowField] 目标 {target.name} 不在MapCache中！");
                return;
            }

            // 限制同时构建的流场数量
            if (flowFields.Count >= 10)
            {
                // 移除最旧的构建中流场
                int oldestId = -1;
                float oldestTime = float.MaxValue;
                foreach (var kvp in flowFields)
                {
                    if (kvp.Value.state != FlowFieldInstance.BuildState.Ready &&
                        kvp.Value.createTime < oldestTime)
                    {
                        oldestTime = kvp.Value.createTime;
                        oldestId = kvp.Key;
                    }
                }
                if (oldestId != -1)
                {
                    RemoveFlowField(oldestId);
                }
            }

            // 创建新的流场实例
            var instance = new FlowFieldInstance(target, allowDiagonal);
            if (instance.InitFromTarget())
            {
                flowFields[targetId] = instance;
                targetTransforms[targetId] = target;  // 缓存Transform引用

                if (PathFindingConfig.DEBUG_MODE)
                {
                    Vector2Int pos = instance.currentGoalPos;
                    Debug.Log($"[FlowField] 开始为目标 {target.name} ({pos.x},{pos.y}) 构建流场，当前流场数量={flowFields.Count}");
                }
            }
        }

        /// <summary>
        /// 更新所有流场（每帧调用）
        /// </summary>
        public static void UpdateAllBuilds()
        {
            int updateCount = 0;
            var idsToRemove = new List<int>();

            foreach (var kvp in flowFields)
            {
                if (updateCount >= MAX_UPDATE_PER_FRAME)
                    break;

                var field = kvp.Value;

                // 检查目标是否还存在
                if (field.target == null)
                {
                    idsToRemove.Add(kvp.Key);
                    continue;
                }

                // 检查目标是否移动（仅在流场Ready时检查）
                if (field.state == FlowFieldInstance.BuildState.Ready)
                {
                    if (field.CheckTargetMoved())
                    {
                        if (PathFindingConfig.DEBUG_MODE)
                        {
                            Vector2Int pos = field.currentGoalPos;
                            Debug.Log($"[FlowField] 目标 {field.target.name} 移动到 ({pos.x},{pos.y})，重新构建流场");
                        }
                    }
                }

                // 更新构建
                if (field.state != FlowFieldInstance.BuildState.Ready)
                {
                    bool finished = field.UpdateBuild();
                    updateCount++;

                    if (finished && PathFindingConfig.DEBUG_MODE)
                    {
                        Vector2Int pos = field.currentGoalPos;
                        Debug.Log($"[FlowField] 目标 {field.target.name} ({pos.x},{pos.y}) 的流场构建完成！");
                    }
                }

                // 清理超时的构建中流场（30秒）
                if (field.state != FlowFieldInstance.BuildState.Ready &&
                    Time.time - field.createTime > 30f)
                {
                    idsToRemove.Add(kvp.Key);
                }
            }

            foreach (var id in idsToRemove)
            {
                RemoveFlowField(id);
            }
        }

        /// <summary>
        /// 获取指定目标Transform的流场实例
        /// </summary>
        public static FlowFieldInstance GetFlowField(Transform target)
        {
            if (target == null) return null;
            flowFields.TryGetValue(target.GetInstanceID(), out var field);
            return field;
        }

        /// <summary>
        /// 移除指定目标的流场
        /// </summary>
        public static void RemoveFlowField(Transform target)
        {
            if (target == null) return;
            RemoveFlowField(target.GetInstanceID());
        }

        private static void RemoveFlowField(int targetInstanceId)
        {
            flowFields.Remove(targetInstanceId);
            targetTransforms.Remove(targetInstanceId);

            // 清理相关Agent的历史记录
            var idsToRemove = new List<int>();
            foreach (var kvp in agentHistory)
            {
                if (kvp.Value.targetInstanceId == targetInstanceId)
                {
                    idsToRemove.Add(kvp.Key);
                }
            }
            foreach (var id in idsToRemove)
            {
                agentHistory.Remove(id);
            }
        }

        #endregion

        #region 查询接口

        public static int GetFlowDirection(Transform target, int x, int y)
        {
            var field = GetFlowField(target);
            if (field == null) return -1;
            return field.GetFlowDirection(x, y);
        }

        public static bool CanReach(Transform target, int x, int y)
        {
            var field = GetFlowField(target);
            if (field == null) return false;
            return field.CanReach(x, y);
        }

        public static int GetDistance(Transform target, int x, int y)
        {
            var field = GetFlowField(target);
            if (field == null) return -1;
            return field.GetDistance(x, y);
        }

        /// <summary>
        /// 获取目标Transform当前所在的Tile位置
        /// </summary>
        public static Vector2Int GetTargetTilePosition(Transform target)
        {
            var field = GetFlowField(target);
            if (field == null)
            {
                var tile = MapCache.GetNearlyTile(target);
                return tile != null ? AStarLogic.DecodeKey(tile.identifier) : Vector2Int.zero;
            }
            return field.currentGoalPos;
        }

        #endregion

        #region Agent移动集成

        /// <summary>
        /// Agent向目标移动
        /// </summary>
        public static void MoveWithFlow(this Transform agent, Transform target, float moveDist, bool allowDiagonal = true)
        {
            if (agent == null || target == null) return;

            var curTile = MapCache.GetNearlyTile(agent);
            if (curTile == null) return;

            Vector2Int pos = AStarLogic.DecodeKey(curTile.identifier);
            int agentId = agent.GetInstanceID();
            int targetId = target.GetInstanceID();

            // 自动请求构建流场
            if (!flowFields.ContainsKey(targetId))
            {
                RequestBuild(target, allowDiagonal);
            }

            // 获取该目标的流场
            var field = GetFlowField(target);
            if (field == null) return;

            // 获取流场方向
            int dir = field.GetFlowDirection(pos.x, pos.y);

            if (dir < 0 || dir >= DirX.Length)
            {
                // 流场无效，尝试使用历史方向
                if (!agentHistory.TryGetValue(agentId, out var history))
                    return;

                if(PathFindingConfig.DEBUG_MODE)
                    Debug.Log($"[FlowField] Agent {agent.name} 在 ({pos.x},{pos.y}) 的流场无效，尝试使用历史方向");

                // 检查历史记录是否匹配当前目标
                if (history.targetInstanceId != targetId)
                {
                    agentHistory.Remove(agentId);
                    if(PathFindingConfig.DEBUG_MODE)
                        Debug.Log($"[FlowField] Agent {agent.name} 的历史记录目标不匹配，丢弃历史记录");
                    return;
                }

                // 检查目标是否还在原位置
                /*if (history.targetGoalId != field.currentGoalId)
                {
                    agentHistory.Remove(agentId);
                    Debug.Log($"[FlowField] Agent {agent.name} 的历史记录目标位置已变更，丢弃历史记录");
                    return;
                }*/

                // 使用之前的方向继续移动，但最多持续0.5秒
                if (Time.time - history.lastValidTime > 0.5f)
                {
                    agentHistory.Remove(agentId);
                    if(PathFindingConfig.DEBUG_MODE)
                        Debug.Log($"[FlowField] Agent {agent.name} 的历史记录已过期，丢弃历史记录");
                    return;
                }

                dir = history.lastDirection;
                pos = AStarLogic.DecodeKey(history.lastTileId);
            }
            else
            {
                // 更新历史记录
                agentHistory[agentId] = new AgentHistory
                {
                    lastDirection = dir,
                    lastTileId = curTile.identifier,
                    targetInstanceId = targetId,
                    targetGoalId = field.currentGoalId,
                    lastValidTime = Time.time
                };
            }

            int nx = pos.x + DirX[dir];
            int ny = pos.y + DirY[dir];
            var next = MapCache.GetTile(AStarLogic.EncodeKey(nx, ny));

            if (next == null) return;
            float spd = moveDist * TransformExtensions.SpeedMul(next.cost);
            agent.position = Vector3.MoveTowards(agent.position, next.position, spd);
        }

        /// <summary>
        /// 获取Agent的下一个路径点
        /// </summary>
        public static TileInfo GetNextTileForAgent(Transform agent, Transform target)
        {
            if (agent == null || target == null) return null;

            var curTile = MapCache.GetNearlyTile(agent);
            if (curTile == null) return null;

            Vector2Int pos = AStarLogic.DecodeKey(curTile.identifier);
            var field = GetFlowField(target);
            if (field == null) return null;

            int dir = field.GetFlowDirection(pos.x, pos.y);
            if (dir < 0 || dir >= DirX.Length) return null;

            int nx = pos.x + DirX[dir];
            int ny = pos.y + DirY[dir];
            return MapCache.GetTile(AStarLogic.EncodeKey(nx, ny));
        }

        #endregion

        #region 全局查询

        /// <summary>
        /// 获取Agent到目标的距离
        /// </summary>
        public static int GetAgentDistance(Transform agent, Transform target)
        {
            if (agent == null || target == null) return -1;

            var curTile = MapCache.GetNearlyTile(agent);
            if (curTile == null) return -1;

            Vector2Int pos = AStarLogic.DecodeKey(curTile.identifier);
            return GetDistance(target, pos.x, pos.y);
        }

        /// <summary>
        /// 检查Agent是否可以到达目标
        /// </summary>
        public static bool CanAgentReach(Transform agent, Transform target)
        {
            if (agent == null || target == null) return false;

            var curTile = MapCache.GetNearlyTile(agent);
            if (curTile == null) return false;

            Vector2Int pos = AStarLogic.DecodeKey(curTile.identifier);
            return CanReach(target, pos.x, pos.y);
        }

        /// <summary>
        /// 检查Agent是否到达目标
        /// </summary>
        public static bool HasAgentReachedTarget(Transform agent, Transform target, float threshold = 0.5f)
        {
            if (agent == null || target == null) return false;

            var agentTile = MapCache.GetNearlyTile(agent);
            var targetTile = MapCache.GetNearlyTile(target);

            if (agentTile == null || targetTile == null) return false;

            // 首先检查是否在同一个Tile
            if (agentTile.identifier == targetTile.identifier)
                return Vector3.Distance(agent.position, target.position) < threshold;

            return false;
        }

        #endregion

        #region 缓存管理

        public static void ClearAllFields()
        {
            flowFields.Clear();
            agentHistory.Clear();
            targetTransforms.Clear();
        }

        public static void ClearAgentHistory(Transform agent)
        {
            if (agent == null) return;
            agentHistory.Remove(agent.GetInstanceID());
        }

        /// <summary>
        /// 获取当前流场数量
        /// </summary>
        public static int FieldCount => flowFields.Count;

        /// <summary>
        /// 获取指定目标的流场状态
        /// </summary>
        public static string GetFieldState(Transform target)
        {
            var field = GetFlowField(target);
            if (field == null) return "Not Found";
            return field.state.ToString();
        }

        /// <summary>
        /// 获取指定目标的构建进度
        /// </summary>
        public static float GetFieldProgress(Transform target)
        {
            var field = GetFlowField(target);
            if (field == null) return 0f;
            return field.BuildProgress;
        }

        /// <summary>
        /// 获取目标Transform当前对应的Tile ID
        /// </summary>
        public static long GetTargetTileId(Transform target)
        {
            var field = GetFlowField(target);
            if (field != null) return field.currentGoalId;

            var tile = MapCache.GetNearlyTile(target);
            return tile?.identifier ?? 0;
        }

        #endregion

        #region 工具方法

        private static bool IsWalkableStatic(int x, int y)
        {
            if (boundsInitialized)
                if (x < mapMinX || x > mapMaxX || y < mapMinY || y > mapMaxY)
                    return false;
            long key = AStarLogic.EncodeKey(x, y);
            if (!MapCache.ContainsKey(key)) return false;
            TileInfo t = MapCache.GetTile(key);
            return t != null && t.cost > 0;
        }

        #endregion
    }
}