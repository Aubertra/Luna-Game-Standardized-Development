using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace AirStack.Pathfinding
{
    public class Tile : MonoBehaviour
    {
        public Vector2Int identifier;

        public TileInfo Info
        {
            get
            {
                return info;
            }
        }

        [SerializeField] private TileInfo info;

        public void InitTile(long identifier)
        {
            info = new TileInfo
            {
                identifier = identifier,
                position = transform.position,
                tileObj = gameObject,
                renderer = gameObject.GetComponentInChildren<Renderer>(),
                cost = 1,
                occpuyer = null
            };
        }

        private void Awake()
        {
            identifier = AStarLogic.DecodeKey(info.identifier);
            info.position = transform.position;
        }

        bool needRestColor = true;

        private void Update()
        {
            /*if (first)
            {
                MapCache.AddTile(info.identifier, info);
                first = false;
            }*/

            if (info.occpuyer)
            {
                if (info.occpuyer == Player.Instance.transform)
                {
                    info.renderer.material.SetColor("_Color", Color.blue);
                }
                else
                {
                    //info.renderer.material.SetColor("_Color", Color.red);
                }
            }
            else if((info.renderer.material.GetColor("_Color") != Color.white && info.renderer.material.GetColor("_Color") != Color.yellow) && needRestColor)
            {
                needRestColor = false;
                _ = ResetColor();
            }
        }

        private async Task ResetColor()
        {
            await Task.Delay(500);
            if (info.renderer.material.GetColor("_Color") != Color.white && info.renderer.material.GetColor("_Color") != Color.yellow)
            {
                info.renderer.material.SetColor("_Color", Color.white);
            }
            needRestColor = true;
        }
    }
}