using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ReSpawner : MonoBehaviour
{
    [SerializeField] private Transform player;
    [SerializeField] private Rigidbody CarRb;
    [SerializeField] private Transform respawnPoint;
    [SerializeField] private GameObject blackPanel;
    [SerializeField] private AutoRespawner AutoRespawner;

    void OnTriggerEnter(Collider other)
    {
        blackPanel.SetActive(true);
        StartCoroutine(blackPanelWait());
    }

    IEnumerator blackPanelWait()
    {
        yield return new WaitForSeconds(1.4f);
        player.transform.position = AutoRespawner.positionTransform.transform.position;
        player.transform.rotation = AutoRespawner.positionTransform.transform.rotation;

        CarRb.velocity = Vector3.zero;
        CarRb.angularVelocity = Vector3.zero;

        yield return new WaitForSeconds(2.15f);
        blackPanel.SetActive(false);
  
    }

}
