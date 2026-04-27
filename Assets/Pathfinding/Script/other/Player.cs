using AirStack.Pathfinding;
using UnityEngine;

public class Player : MonoSingleton<Player>
{
    public TileInfo playerTile
    {
        get
        {
            if(Time.time - lastUpdateTileTime < 0.5f && tileInfo != null)
            {
                return tileInfo;
            }
            else
            {
                lastUpdateTileTime = Time.time;
                if (tileInfo != null) tileInfo.occpuyer = null;
                tileInfo = MapCache.GetNearlyTile(transform.position);

                var goal = AStarLogic.DecodeKey(tileInfo.identifier);
                FlowFieldLogic.RequestBuild(transform);

                tileInfo.occpuyer = transform;
                return tileInfo;
            }
        }
    }

    public float moveSpeed = 5f;
    [SerializeField]private TileInfo tileInfo;
    public Vector2Int goal;

    private float lastUpdateTileTime = 0f;

    private void Update()
    {
        int h = 0;
        int v = 0;

        if (Input.GetKey(KeyCode.W)) v = 1;
        if (Input.GetKey(KeyCode.S)) v = -1;
        if (Input.GetKey(KeyCode.A)) h = -1;
        if (Input.GetKey(KeyCode.D)) h = 1;

        // 获取相机的右方向和前方向
        Vector3 cameraForward = Camera.main.transform.forward;
        Vector3 cameraRight = Camera.main.transform.right;

        // 忽略Y轴（保持水平移动）
        cameraForward.y = 0;
        cameraRight.y = 0;

        // 归一化
        cameraForward.Normalize();
        cameraRight.Normalize();

        // 计算移动方向
        Vector3 dir = (cameraForward * v + cameraRight * h).normalized;

        transform.position = Vector3.MoveTowards(transform.position, transform.position + dir, moveSpeed * Time.deltaTime);

        if (playerTile != null) { }
    }
}
