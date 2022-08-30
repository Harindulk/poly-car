using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AutoRespawner : MonoBehaviour
{
    public GameObject positionTransform;

    private void OnTriggerEnter(Collider point)
    {
        if (point.CompareTag("respawnPoint"))
        {
            positionTransform = point.gameObject;
            Debug.Log("collision");
        }
    }
}
