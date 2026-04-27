using AirStack.Pathfinding;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mosnter : MonoBehaviour
{
    public float MoveSpeed = 3f;

    void Update()
    {
        //transform.MoveToTile(Player.Instance.playerTile, MoveSpeed * Time.deltaTime, PathfindingAlgorithm.JPS, -1, true);
        FlowFieldLogic.MoveWithFlow(transform, Player.Instance.transform, MoveSpeed * Time.deltaTime, false);
    }
}
