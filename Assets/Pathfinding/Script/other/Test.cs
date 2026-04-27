using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public class Test : MonoBehaviour
    {
        public Vector2Int start;
        public Vector2Int end;
        public Transform startPoint, monsterGroup;
        public bool allowDiagonal;

        public GameObject monster;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.K)) { MapCache.DisplayMap(); }
            if (Input.GetKeyDown(KeyCode.I)) { var obj = Instantiate(monster); obj.SetActive(true); obj.transform.SetParent(monsterGroup); }
            if (startPoint != null)
            {
                var path = FlowFieldLogic.GetNextTileForAgent(startPoint, Player.Instance.transform);
                if (path != null)
                {
                    path.renderer.material.SetColor("_Color", Color.red);
                }
            }
        }
    }
}