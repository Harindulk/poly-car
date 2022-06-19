using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class StartCount : MonoBehaviour
{
    public GameObject Count1;
    public GameObject Count2;
    public GameObject Count3;
    public AudioSource audioCount1;
    public AudioSource audioCount2;
    public AudioSource audioCount3;
    public GameObject countPanel;

    private void Start()
    {
        StartCoroutine(Count());
    }

    IEnumerator Count()
    {
        yield return new WaitForSeconds(0.4f);
        Count1.SetActive(true);
        audioCount1.Play();

        yield return new WaitForSeconds(0.6f);
        Count1.SetActive(false);
        Count2.SetActive(true);
        audioCount2.Play();

        yield return new WaitForSeconds(0.75f);
        Count2.SetActive(false);
        Count3.SetActive(true);
        audioCount3.Play();

        yield return new WaitForSeconds(0.8f);
        countPanel.SetActive(false);
        GameObject.Find("DriftCar").GetComponent<DriftCarController>().enabled = true;
        GameObject.Find("GameManager").GetComponent<StopWatch>().enabled = true;

    }   

}
