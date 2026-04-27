using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class PerformanceMonitorUI : MonoBehaviour
{
    [Header("显示设置")]
    public bool showInGame = true;
    public Font font;
    public Vector2 position = new Vector2(10, 10);
    public Vector2 size = new Vector2(250, 200);

    [Header("文字设置")]
    public int fontSize = 14;
    public Color textColor = Color.green;
    public Color backgroundColor = new Color(0, 0, 0, 0.8f);

    [Header("监控选项")]
    public bool showFPS = true;
    public bool showAvgFPS = true;
    public bool showMemory = true;
    public bool showDrawCalls = true;
    public bool showFrameTime = true;

    [Header("FPS 设置")]
    public float updateInterval = 0.5f;
    public int targetFrameRate = 60;

    [Header("警告阈值")]
    public int lowFPSWarning = 30;
    public int highMemoryWarningMB = 512;

    // UI 组件
    private GameObject panelObj;
    private RectTransform rectTransform;
    private Image backgroundImage;
    private Text displayText;

    // FPS 计算
    private float deltaTime = 0.0f;
    private float fps = 0f;
    private float avgFPS = 0f;
    private float[] fpsBuffer;
    private int fpsBufferIndex = 0;
    private int fpsBufferSize = 60;

    // 内存监控
    private float memoryUsage = 0f;
    private float peakMemory = 0f;

    // 渲染统计
    private int drawCalls = 0;

    void Start()
    {
        //Application.targetFrameRate = targetFrameRate;

        // 初始化 FPS 缓冲区
        fpsBuffer = new float[fpsBufferSize];
        for (int i = 0; i < fpsBufferSize; i++)
        {
            fpsBuffer[i] = targetFrameRate;
        }

        // 创建 UI
        CreateUI();

        StartCoroutine(UpdateStats());
    }

    void CreateUI()
    {
        // 创建面板
        panelObj = new GameObject("PerformancePanel");
        rectTransform = panelObj.AddComponent<RectTransform>();
        panelObj.transform.SetParent(transform);

        // 设置位置和大小
        rectTransform.anchorMin = new Vector2(0, 1);  // 左上角
        rectTransform.anchorMax = new Vector2(0, 1);
        rectTransform.pivot = new Vector2(0, 1);
        rectTransform.anchoredPosition = position;
        rectTransform.sizeDelta = size;

        // 添加背景
        backgroundImage = panelObj.AddComponent<Image>();
        backgroundImage.color = backgroundColor;

        // 创建文字对象
        GameObject textObj = new GameObject("DisplayText");
        textObj.transform.SetParent(panelObj.transform);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10, 10);
        textRect.offsetMax = new Vector2(-10, -10);

        displayText = textObj.AddComponent<Text>();
        displayText.font = font;
        displayText.fontSize = fontSize;
        displayText.color = textColor;
        displayText.alignment = TextAnchor.UpperLeft;
        displayText.supportRichText = true;

        panelObj.SetActive(showInGame);
    }

    void Update()
    {
        if (!showInGame) return;

        // 计算实时 FPS
        deltaTime += (Time.unscaledDeltaTime - deltaTime) * 0.1f;

        if (Time.timeScale > 0)
        {
            fps = 1.0f / Time.deltaTime;
        }
        else
        {
            fps = 0;
        }

        // 更新 FPS 缓冲区
        fpsBuffer[fpsBufferIndex] = fps;
        fpsBufferIndex = (fpsBufferIndex + 1) % fpsBufferSize;

        // 计算平均 FPS
        float sum = 0;
        for (int i = 0; i < fpsBufferSize; i++)
        {
            sum += fpsBuffer[i];
        }
        avgFPS = sum / fpsBufferSize;
    }

    IEnumerator UpdateStats()
    {
        while (true)
        {
            yield return new WaitForSeconds(updateInterval);

            if (!showInGame) continue;

            // 更新内存
            memoryUsage = System.GC.GetTotalMemory(false) / (1024f * 1024f);
            if (memoryUsage > peakMemory)
            {
                peakMemory = memoryUsage;
            }

            // 更新显示文字
            UpdateDisplayText();
        }
    }

    void UpdateDisplayText()
    {
        if (displayText == null) return;

        System.Text.StringBuilder sb = new System.Text.StringBuilder();

        // 帧率颜色
        string fpsColor = GetFPSColor(fps);
        string avgFpsColor = GetFPSColor(avgFPS);

        sb.AppendLine("════ 性能监视器 ════");

        if (showFPS)
        {
            sb.AppendLine($"<color={fpsColor}>FPS: {fps:F1}</color>");
        }

        if (showAvgFPS)
        {
            sb.AppendLine($"平均 FPS: <color={avgFpsColor}>{avgFPS:F1}</color>");
        }

        if (showFrameTime)
        {
            sb.AppendLine($"帧时间: {deltaTime * 1000:F2} ms");
        }

        if (showMemory)
        {
            string memColor = memoryUsage > highMemoryWarningMB ? "red" : "white";
            sb.AppendLine($"内存: <color={memColor}>{memoryUsage:F1} MB</color>");
            sb.AppendLine($"峰值: {peakMemory:F1} MB");
        }

        if (showDrawCalls)
        {
            sb.AppendLine($"Draw Calls: {drawCalls}");
        }

        sb.AppendLine($"目标帧率: {targetFrameRate}");

        displayText.text = sb.ToString();
    }

    private string GetFPSColor(float fpsValue)
    {
        if (fpsValue >= 50) return "green";
        if (fpsValue >= 30) return "yellow";
        return "red";
    }

    // 公共方法
    public void ToggleDisplay()
    {
        showInGame = !showInGame;
        if (panelObj != null)
        {
            panelObj.SetActive(showInGame);
        }
    }

    public void Show()
    {
        showInGame = true;
        if (panelObj != null)
        {
            panelObj.SetActive(true);
        }
    }

    public void Hide()
    {
        showInGame = false;
        if (panelObj != null)
        {
            panelObj.SetActive(false);
        }
    }
}