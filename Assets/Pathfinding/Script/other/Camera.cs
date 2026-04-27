using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class mCamera : MonoBehaviour
{
    [Header("目标")]
    public Transform target;

    [Header("距离设置")]
    public float distance = 5f;      // 相机距离
    public float height = 2f;        // 相机高度

    [Header("旋转设置")]
    public float xSpeed = 120f;      // 水平旋转速度
    public float ySpeed = 120f;      // 垂直旋转速度
    public float yMinLimit = -20f;   // 垂直最低角度
    public float yMaxLimit = 80f;    // 垂直最高角度

    [Header("平滑设置")]
    public float smoothTime = 0.1f;  // 平滑时间

    [Header("鼠标控制")]
    public bool requireMouseDrag = true;   // 是否要求按住鼠标拖拽
    public int dragButton = 0;             // 拖拽按钮：0-左键，1-右键，2-中键

    private float x = 0f;
    private float y = 0f;
    private float currentX = 0f;
    private float currentY = 0f;
    private float velocityX = 0f;
    private float velocityY = 0f;

    private Vector2 lastMousePosition;
    private bool isDragging = false;

    void Start()
    {
        // 初始化角度
        Vector3 angles = transform.eulerAngles;
        x = angles.y;
        y = angles.x;
        currentX = x;
        currentY = y;
    }

    void LateUpdate()
    {
        if (target == null) return;

        // 处理鼠标拖拽旋转
        HandleMouseDrag();

        // 平滑跟随
        currentX = Mathf.SmoothDamp(currentX, x, ref velocityX, smoothTime);
        currentY = Mathf.SmoothDamp(currentY, y, ref velocityY, smoothTime);

        // 计算相机位置
        Quaternion rotation = Quaternion.Euler(currentY, currentX, 0);
        Vector3 position = target.position - rotation * Vector3.forward * distance + Vector3.up * height;

        // 更新相机
        transform.position = position;
        transform.LookAt(target.position + Vector3.up * height / 1f);
    }

    private void HandleMouseDrag()
    {
        if (!requireMouseDrag)
        {
            // 原始模式：直接响应鼠标移动
            x += Input.GetAxis("Mouse X") * xSpeed * Time.deltaTime;
            y -= Input.GetAxis("Mouse Y") * ySpeed * Time.deltaTime;
            y = ClampAngle(y, yMinLimit, yMaxLimit);
            return;
        }

        // 鼠标按下开始拖拽
        if (Input.GetMouseButtonDown(dragButton))
        {
            isDragging = true;
            lastMousePosition = Input.mousePosition;
        }

        // 鼠标释放结束拖拽
        if (Input.GetMouseButtonUp(dragButton))
        {
            isDragging = false;
        }

        // 拖拽中处理旋转
        if (isDragging)
        {
            Vector2 currentMousePosition = Input.mousePosition;
            Vector2 delta = currentMousePosition - lastMousePosition;

            if (delta != Vector2.zero)
            {
                x += delta.x * xSpeed * Time.deltaTime;
                y -= delta.y * ySpeed * Time.deltaTime;
                y = ClampAngle(y, yMinLimit, yMaxLimit);
            }

            lastMousePosition = currentMousePosition;
        }
    }

    private float ClampAngle(float angle, float min, float max)
    {
        if (angle < -360f) angle += 360f;
        if (angle > 360f) angle -= 360f;
        return Mathf.Clamp(angle, min, max);
    }
}