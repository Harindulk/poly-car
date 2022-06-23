using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using PlayFab;
using PlayFab.ClientModels;

public class StopWatch : MonoBehaviour
{
    public TMP_Text currentTimeText;
    public TMP_Text scoreText;
    float currentTime;
    int score = 0;
    public float multiplier = 5;

    public GameObject completedPanel;
    public TMP_Text completeTimeEnd;

    public static StopWatch instance;
    public PlayFabManager playFabManager;
    public GameObject hidegamepanel;
    public AudioSource winSound;
    public AudioSource engineSound;
    public AudioSource TireSound;
    public GameObject winDisable;

    private void Awake()
    {
        instance = this;
    }

    // Start is called before the first frame update
    void Start()
    {
        currentTime = 0;
    }

    // Update is called once per frame
    void Update()
    {
        currentTime = currentTime + Time.deltaTime;
        TimeSpan time = TimeSpan.FromSeconds(currentTime);
        currentTimeText.text = time.ToString(@"mm\:ss\:ff");

        score = Mathf.RoundToInt(currentTime * multiplier);
        scoreText.text = score.ToString();
    }

    void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.tag == "Player")
        {
            Debug.Log("Finish");
            playFabManager.SendLeaderboard(score);
            completedPanel.SetActive(true);
            completeTimeEnd.text = currentTimeText.text;
            Time.timeScale = 0.5f;
            hidegamepanel.SetActive(false);
            engineSound.Pause();
            TireSound.Pause();

            winDisable.SetActive(false);
        }
    }

}
