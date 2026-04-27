using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MonoSingleton<T> : MonoBehaviour where T : MonoSingleton<T>
{
    public static bool IsInitialized => Instance != null;

    public static T Instance;

    protected virtual void Awake()
    {
        if(Instance != null)
        {
            Debug.LogError($"Multiple instances of {typeof(T).Name} detected! Destroying the new one.");
            Destroy(gameObject);
            return;
        }
        Instance = (T)this;
    }

    protected virtual void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
}
