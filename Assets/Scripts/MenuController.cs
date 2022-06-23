using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;
using BayatGames.SaveGameFree;
using UnityEngine.Audio;
using UnityEngine.Rendering;


public class MenuController : MonoBehaviour
{
    public RenderPipelineAsset[] qualitylevels;
    public AudioMixer uiMixer;
    public AudioMixer gameMixer;
    public AudioMixer vehicleMixer;
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown qualityDropdown;
    Resolution[] resolutions;


    void Start()
    {
        resolutions = Screen.resolutions;

        resolutionDropdown.ClearOptions();

        List<string> options = new List<string>();

        int currentResolutionIndex = 0;
        for (int i = 0; i < resolutions.Length; i++)
        {
            string option = resolutions[i].width + "x" + resolutions[i].height;
            options.Add(option);

            if (resolutions[i].width == Screen.currentResolution.width && resolutions[i].height == Screen.currentResolution.height)
            {
                currentResolutionIndex = i;
            }
        }

        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentResolutionIndex;
        resolutionDropdown.RefreshShownValue();

        qualityDropdown.value = QualitySettings.GetQualityLevel();
    }

    //audio
    public void uiVolume(float uiVolume)
    {
        uiMixer.SetFloat("uiVolume", uiVolume);
    }
    public void GameMusic(float ingameMusic)
    {
        gameMixer.SetFloat("ingameMusic", ingameMusic);
    }
    public void VehicleVolume(float vehicleVolume)
    {
        vehicleMixer.SetFloat("vehicleVolume", vehicleVolume);
    }


    public void SetResolution(int resolutionIndex)
    {
        Resolution resolution = resolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, Screen.fullScreen);
    }

    public void SetQuality(int value)
    {
        QualitySettings.SetQualityLevel(value);
        QualitySettings.renderPipeline = qualitylevels[value];
    }

    public void SetFullscreen(bool isFullscreen)
    {
        Screen.fullScreen = isFullscreen;
        SaveGame.Save<bool>("isFullscreen", isFullscreen);


    }

    public void quit()
    {
        Application.Quit();
    }


    public void GitHub()
    {
        Application.OpenURL("https://github.com/Harindulk");
    }
    public void Twitter()
    {
        Application.OpenURL("https://twitter.com/Harindu_Fonseka");
    }
    public void LinkedIN()
    {
        Application.OpenURL("https://www.linkedin.com/in/harindulk/");
    }
    public void Youtube()
    {
        Application.OpenURL("https://www.youtube.com/channel/UCRyQGxzCgFb5wmsp1XAlWpQ");
    }
    public void Website()
    {
        Application.OpenURL("https://www.harindu.dev/");
    }
    public void discord()
    {
        Application.OpenURL("https://discord.gg/MFXRfMeFB7");
    }
    public void itch()
    {
        Application.OpenURL("https://harindulk.itch.io/");
    }

    public void GreenForest()
    {
        SceneManager.LoadScene("Green Forest");
    }

}
