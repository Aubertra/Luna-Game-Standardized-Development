using System.Collections;
using System.Collections.Generic;
using UnityEngine;


namespace AirStack.Pathfinding
{
    public class Manager : MonoSingleton<Manager>
    {
        public static bool FlowFieldMapReady = false;

        protected override void Awake()
        {
            base.Awake();
            MapCache.InitLunaMap();
        }

        private void Update()
        {
            FlowFieldLogic.UpdateAllBuilds();
        }
    }
}