# A* 寻路算法修复记录

## 版本信息

| 项目 | 内容 |
|------|------|
| 修复日期 | 2026-04-23 |
| 修复版本 | v2.0 |
| 修复范围 | `PriorityQueue<T>`、`AStarLogic.FindPath()` |

---

## 问题清单

### 问题 1：优先队列 SiftDown 缺陷

| 属性 | 描述 |
|------|------|
| **严重程度** | 🔴 严重 |
| **现象** | `(2,2)` F=4.41 比 `(1,2)` F=5.00 小，但 `(1,2)` 先出队 |
| **根因** | `Swap` 方法交换元素后未正确维护堆性质，最小值未上浮至堆顶 |
| **影响** | 全局最优节点无法优先展开，导致路径偏离理论最优解 |

### 问题 2：AStarNode 值类型陷阱

| 属性 | 描述 |
|------|------|
| **严重程度** | 🔴 严重 |
| **现象** | `existingNode.Parent = current` 赋值后字典中数据未变更 |
| **根因** | `AStarNode` 为 `struct` 值类型，字典索引器返回副本，修改丢失 |
| **影响** | 路径回溯时父节点链断裂，产生循环或错误路径 |

### 问题 3：节点更新逻辑混乱

| 属性 | 描述 |
|------|------|
| **严重程度** | 🟡 中等 |
| **现象** | 同一节点多次入队，旧版本出队时仍污染邻居节点 |
| **根因** | `Dequeue` 未校验节点是否为最新版本 |
| **影响** | 劣质 G 值传播至邻居，后续节点计算偏离最优 |


### 问题 4：状态标记双源不同步

| 属性 | 描述 |
|------|------|
| **严重程度** | 🟢 轻微 |
| **现象** | `closedSet` 与 `nodeInfo.IsClosed` 两套状态可能不一致 |
| **根因** | 同时维护 `HashSet<string>` 和节点内部 `IsClosed` 字段 |
| **影响** | 极少数情况下节点被重复处理或遗漏 |

---

## 修复方案

### 修复 1：重写优先队列 SiftDown

| 属性 | 描述 |
|------|------|
| **修改前** | `Swap` 直接交换两个元素 |
| **修改后** | 采用"空洞法"：暂存目标元素，沿路径下沉子节点，最后写入 |
| **效果** | 堆性质严格保证，最小值始终位于堆顶 |


    // 修改前(Swap 方式)
    private void Swap(int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
    }

    // 修改后(空洞法)
    private void SiftDown(int index)
    {
        var item = heap[index];
        int half = heap.Count / 2;

        while (index < half)
        {
            int leftChild = index * 2 + 1;
            int rightChild = leftChild + 1;
            
            int smallerChild = leftChild;
            if (rightChild < heap.Count && heap[rightChild].priority < heap[leftChild].priority)
                smallerChild = rightChild;

            if (item.priority <= heap[smallerChild].priority)
                break;

            heap[index] = heap[smallerChild];
            index = smallerChild;
        }
        
        heap[index] = item;
    }