# Luna WebGL 环境下 Dictionary 键哈希码问题

## 一、问题背景

### 1.1 问题现象

在 Unity 编辑器中正常运行，但打包到 **Luna WebGL** 环境后出现以下问题：

- `Dictionary.ContainsKey()` 返回 `false`，即使键明显存在
- 哈希码打印结果不一致：
  - 编辑器：`(1,1)` 哈希码为 `5`
  - Luna 环境：`(1,1)` 哈希码为 `0` 或 `1`

### 1.2 根本原因

| 原因 | 说明 |
|------|------|
| **平台差异** | Unity API 在不同平台的实现可能有差异 |
| **代码裁剪** | WebGL 打包时可能优化掉某些方法 |
| **序列化问题** | `Vector2Int` 在 WebGL 环境下序列化行为不一致 |
| **自定义类型** | 项目中存在自定义的 `Vector2Int` 结构与官方冲突 |

### 1.3 典型错误日志

```
Error: The given key was not present in the dictionary.
TypeError: Cannot read properties of null (reading 'renderer')
起点或终点不在 MapCache.mapCache 中！
```

---

## 二、解决方案

### 2.1 方案一：使用字符串键（推荐 ★★★★★）

**原理**：使用字符串作为字典键，完全避免哈希码问题。

```csharp
// TileInfo 定义
[System.Serializable]
public class TileInfo
{
    public int x;
    public int y;
    public GameObject tileObj;
    public Renderer renderer;
    public Vector3 position;
    public int cost;
    
    // 字符串键属性
    public string Key => $"{x},{y}";
    
    // 便捷方法
    public void SetCoordinates(Vector2Int coord)
    {
        x = coord.x;
        y = coord.y;
    }
    
    public Vector2Int GetCoordinates()
    {
        return new Vector2Int(x, y);
    }
}

// MapCache 实现
public static class MapCache
{
    // 使用字符串作为键
    public static Dictionary<string, TileInfo> mapCache = new Dictionary<string, TileInfo>();
    
    public static void AddTile(Vector2Int identifier, TileInfo info)
    {
        string key = $"{identifier.x},{identifier.y}";
        info.x = identifier.x;
        info.y = identifier.y;
        mapCache[key] = info;
    }
    
    public static bool CheckTileInfoValidity(Vector2Int key)
    {
        string searchKey = $"{key.x},{key.y}";
        return mapCache.ContainsKey(searchKey);
    }
    
    public static TileInfo GetTile(Vector2Int key)
    {
        string searchKey = $"{key.x},{key.y}";
        return mapCache.TryGetValue(searchKey, out TileInfo info) ? info : null;
    }
}
```

**优点**：
- ✅ 100% 稳定，无平台差异
- ✅ 易于调试（键值直观可见）
- ✅ 无需担心 GetHashCode 实现

**缺点**：
- ⚠️ 轻微性能开销（字符串拼接）
- ⚠️ 内存占用略高

---

### 2.2 方案二：自定义稳定比较器（推荐 ★★★★）

**原理**：实现自己的 `IEqualityComparer<T>`，使用稳定的哈希算法。

```csharp
public class StableVector2IntComparer : IEqualityComparer<Vector2Int>
{
    public bool Equals(Vector2Int a, Vector2Int b)
    {
        return a.x == b.x && a.y == b.y;
    }
    
    public int GetHashCode(Vector2Int obj)
    {
        // 使用稳定的哈希算法，不依赖 Unity 内部实现
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + obj.x;
            hash = hash * 31 + obj.y;
            return hash;
        }
    }
}

// 创建字典时使用自定义比较器
public static Dictionary<Vector2Int, TileInfo> mapCache = 
    new Dictionary<Vector2Int, TileInfo>(new StableVector2IntComparer());
```

**优点**：
- ✅ 保持 Vector2Int 作为键
- ✅ 哈希算法可预测、跨平台一致

**缺点**：
- ⚠️ 需要确保自定义类不被裁剪

---

### 2.3 方案三：int 组合键（推荐 ★★★★）

**原理**：将两个 int 合并为一个 long 作为键。

```csharp
public static class MapCache
{
    public static Dictionary<long, TileInfo> mapCache = new Dictionary<long, TileInfo>();
    
    private static long MakeKey(Vector2Int pos)
    {
        // 高32位存 x，低32位存 y
        return ((long)pos.x << 32) | (uint)pos.y;
    }
    
    private static long MakeKey(int x, int y)
    {
        return ((long)x << 32) | (uint)y;
    }
    
    public static void AddTile(Vector2Int identifier, TileInfo info)
    {
        long key = MakeKey(identifier);
        mapCache[key] = info;
    }
    
    public static bool CheckTileInfoValidity(Vector2Int key)
    {
        long searchKey = MakeKey(key);
        return mapCache.ContainsKey(searchKey);
    }
}
```

**优点**：
- ✅ 性能最佳（整数运算）
- ✅ 无哈希冲突
- ✅ 内存占用小

**缺点**：
- ⚠️ 代码稍复杂

---

### 2.4 方案四：使用 Precomputed 数据（编辑器预生成）

**原理**：在编辑器中预先生成稳定的地图数据资产。

```csharp
// 创建 ScriptableObject 资产
[CreateAssetMenu(fileName = "MapData", menuName = "Game/Map Data")]
public class MapData : ScriptableObject
{
    [System.Serializable]
    public class TileData
    {
        public int x;
        public int y;
        public Vector3 position;
        public int cost;
        public string Key => $"{x},{y}";
    }
    
    public List<TileData> tiles = new List<TileData>();
}

// 运行时加载
public static class MapCache
{
    public static Dictionary<string, TileInfo> mapCache = new Dictionary<string, TileInfo>();
    
    public static void LoadFromAsset(MapData mapData)
    {
        foreach (var data in mapData.tiles)
        {
            var info = new TileInfo
            {
                x = data.x,
                y = data.y,
                position = data.position,
                cost = data.cost
            };
            mapCache[data.Key] = info;
        }
    }
}
```

**优点**：
- ✅ 数据完全可控
- ✅ 运行时无解析开销
- ✅ 可跨平台

**缺点**：
- ⚠️ 需要编辑器工具生成数据

---

## 三、A* 寻路适配方案

### 3.1 统一使用字符串键的 A* 实现

```csharp
public class AStarLogic
{
    public static List<string> FindPath(string start, string goal, bool allowDiagonal = true)
    {
        // 验证起点和终点
        if (!MapCache.CheckTileInfoValidity(start) || !MapCache.CheckTileInfoValidity(goal))
        {
            Debug.LogError($"起点 {start} 或终点 {goal} 不在 MapCache 中！");
            return new List<string>();
        }
        
        if (start == goal)
        {
            return new List<string> { start };
        }
        
        // 节点信息
        var cameFrom = new Dictionary<string, string>();
        var gScore = new Dictionary<string, float>();
        var hScore = new Dictionary<string, float>();
        
        // 优先队列（使用自定义比较器）
        var openSet = new SortedSet<string>(new FScoreComparer(gScore, hScore));
        var closedSet = new HashSet<string>();
        
        // 初始化
        gScore[start] = 0;
        hScore[start] = Heuristic(start, goal);
        openSet.Add(start);
        
        var directions = allowDiagonal ? GetEightDirections() : GetFourDirections();
        
        string bestNode = start;
        float bestHeuristic = hScore[start];
        
        while (openSet.Count > 0)
        {
            string current = openSet.Min;
            
            if (current == goal)
            {
                return ReconstructPath(cameFrom, start, goal);
            }
            
            openSet.Remove(current);
            closedSet.Add(current);
            
            float currentG = gScore[current];
            
            foreach (var dir in directions)
            {
                string neighbor = AddDirection(current, dir);
                
                if (!MapCache.CheckTileInfoValidity(neighbor))
                    continue;
                    
                if (closedSet.Contains(neighbor))
                    continue;
                
                TileInfo tileInfo = MapCache.GetTile(neighbor);
                float moveCost = tileInfo?.cost ?? 1f;
                
                float dirMultiplier = IsDiagonal(dir) ? 1.414f : 1f;
                float tentativeG = currentG + (moveCost * dirMultiplier);
                
                if (!hScore.ContainsKey(neighbor))
                {
                    hScore[neighbor] = Heuristic(neighbor, goal);
                }
                
                if (hScore[neighbor] < bestHeuristic)
                {
                    bestHeuristic = hScore[neighbor];
                    bestNode = neighbor;
                }
                
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor] - 0.0001f)
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    
                    if (openSet.Contains(neighbor))
                        openSet.Remove(neighbor);
                    openSet.Add(neighbor);
                }
            }
        }
        
        // 不可达，返回到最近点
        if (bestNode != start && cameFrom.ContainsKey(bestNode))
        {
            Debug.LogWarning($"目标 {goal} 不可达，返回到最近点 {bestNode}");
            return ReconstructPath(cameFrom, start, bestNode);
        }
        
        return new List<string> { start };
    }
    
    // 自定义比较器
    private class FScoreComparer : IComparer<string>
    {
        private Dictionary<string, float> gScore;
        private Dictionary<string, float> hScore;
        
        public FScoreComparer(Dictionary<string, float> g, Dictionary<string, float> h)
        {
            gScore = g;
            hScore = h;
        }
        
        public int Compare(string a, string b)
        {
            if (a == b) return 0;
            
            float fA = (gScore.ContainsKey(a) ? gScore[a] : float.MaxValue) + 
                       (hScore.ContainsKey(a) ? hScore[a] : float.MaxValue);
            float fB = (gScore.ContainsKey(b) ? gScore[b] : float.MaxValue) + 
                       (hScore.ContainsKey(b) ? hScore[b] : float.MaxValue);
            
            int cmp = fA.CompareTo(fB);
            if (cmp == 0)
                cmp = string.Compare(a, b, StringComparison.Ordinal);
            return cmp;
        }
    }
    
    private static float Heuristic(string a, string b)
    {
        var aPos = ParseKey(a);
        var bPos = ParseKey(b);
        
        int dx = Math.Abs(aPos.x - bPos.x);
        int dy = Math.Abs(aPos.y - bPos.y);
        return Math.Max(dx, dy);
    }
    
    private static List<string> ReconstructPath(Dictionary<string, string> cameFrom, 
                                                 string start, string goal)
    {
        var path = new List<string>();
        string current = goal;
        var visited = new HashSet<string>();
        
        while (current != start && visited.Count < 10000)
        {
            if (visited.Contains(current))
            {
                Debug.LogError("检测到循环引用！");
                break;
            }
            
            path.Add(current);
            visited.Add(current);
            
            if (!cameFrom.ContainsKey(current))
                break;
            
            current = cameFrom[current];
        }
        
        if (current == start)
            path.Add(start);
        
        path.Reverse();
        return path;
    }
    
    private static List<Vector2Int> GetFourDirections()
    {
        return new List<Vector2Int>
        {
            new Vector2Int(0, 1), new Vector2Int(1, 0),
            new Vector2Int(0, -1), new Vector2Int(-1, 0)
        };
    }
    
    private static List<Vector2Int> GetEightDirections()
    {
        return new List<Vector2Int>
        {
            new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(1, 0),
            new Vector2Int(1, -1), new Vector2Int(0, -1), new Vector2Int(-1, -1),
            new Vector2Int(-1, 0), new Vector2Int(-1, 1)
        };
    }
    
    private static string AddDirection(string pos, Vector2Int dir)
    {
        var current = ParseKey(pos);
        var next = new Vector2Int(current.x + dir.x, current.y + dir.y);
        return $"{next.x},{next.y}";
    }
    
    private static bool IsDiagonal(Vector2Int dir) => Math.Abs(dir.x) == 1 && Math.Abs(dir.y) == 1;
    
    private static Vector2Int ParseKey(string key)
    {
        var parts = key.Split(',');
        return parts.Length == 2 ? new Vector2Int(int.Parse(parts[0]), int.Parse(parts[1])) : Vector2Int.zero;
    }
}
```

---

## 四、调试工具

### 4.1 打印字典内容

```csharp
public static void DebugMapContents()
{
    Debug.Log($"========== MapCache 内容 (共 {mapCache.Count} 项) ==========");
    
    foreach (var kvp in mapCache)
    {
        Debug.Log($"键: {kvp.Key} -> 位置: {kvp.Value.position}");
    }
}
```

### 4.2 验证键的存在性

```csharp
public static void VerifyKey(string key)
{
    bool exists = mapCache.ContainsKey(key);
    Debug.Log($"键 '{key}' 存在: {exists}");
    
    if (!exists)
    {
        Debug.Log("可用的键:");
        foreach (var k in mapCache.Keys)
        {
            Debug.Log($"  {k}");
        }
    }
}
```

---

## 五、最佳实践总结

| 场景 | 推荐方案 | 理由 |
|------|---------|------|
| 新项目 | 字符串键 | 简单可靠，无坑 |
| 现有项目迁移 | 自定义比较器 | 改动最小 |
| 性能敏感 | int 组合键 | 最快速度 |
| 数据驱动 | ScriptableObject | 可编辑、可持久化 |

### 5.1 核心原则

1. **避免依赖平台相关的 GetHashCode 实现**
2. **使用稳定、可预测的键类型（如 string）**
3. **添加充分的空值和有效性检查**
4. **使用自定义比较器确保跨平台一致性**

### 5.2 检查清单

- [ ] 字典键是否使用了稳定的类型（string/long/自定义比较器）
- [ ] 是否添加了空值检查
- [ ] 是否在初始化后验证了地图数据
- [ ] 是否添加了调试工具输出键值
- [ ] 是否测试过打包后的环境

---

## 六、常见问题 FAQ

### Q1: 为什么 Unity 编辑器正常但 WebGL 不正常？

**A**: WebGL 环境使用 IL2CPP 后端，代码裁剪和优化可能导致某些 API 行为与编辑器 Mono 运行时不同。

### Q2: 字符串键的性能影响大吗？

**A**: 对于一般大小的地图（< 10000 瓦片），性能影响可忽略。每帧频繁查找才需要考虑优化。

### Q3: 如何确保自定义比较器不被裁剪？

**A**: 在代码中显式引用该类，或使用 `[Preserve]` 特性。

```csharp
[Preserve]
public class StableVector2IntComparer : IEqualityComparer<Vector2Int>
{ ... }
```

---

*手册版本：1.0 | 更新日期：2026-04-24*