using UnityEngine;
using UnityEngine.SceneManagement; 

public class MainMenuManager : MonoBehaviour
{
    [Header("Scene Settings")]
    [Tooltip("Type the exact name of your game scene here")]
    [SerializeField] private string gameSceneName = "Forest Scene";

    [Header("UI Panels")]
    [Tooltip("Drag your Settings Panel object here")]
    [SerializeField] private GameObject settingsPanel;

    private void Start()
    {
        // Ensure the settings menu is hidden when the game first loads
        if (settingsPanel != null)
        {
            settingsPanel.SetActive(false);
        }
    }

    public void PlayGame()
    {
        LoadingScreen.Load(gameSceneName);
    }

    public void OpenSettings()
    {
        // This turns on the Settings pop-up
        if (settingsPanel != null)
        {
            var panelUI = settingsPanel.GetComponent<SettingsPanelUI>();
            if (panelUI != null)
            {
                panelUI.OpenPanel();
            }
            else
            {
                settingsPanel.SetActive(true);
            }
        }
        else
        {
            Debug.LogWarning("You forgot to drag the Settings Panel into the MenuManager!");
        }
    }

    public void CloseSettings()
    {
        // This turns off the Settings pop-up
        if (settingsPanel != null)
        {
            var panelUI = settingsPanel.GetComponent<SettingsPanelUI>();
            if (panelUI != null && settingsPanel.activeInHierarchy)
            {
                panelUI.ClosePanel();
            }
            else
            {
                settingsPanel.SetActive(false);
            }
        }
    }

    public void QuitGame()
    {
        // This closes the game application
        Debug.Log("QUIT GAME!"); 
        Application.Quit();
    }
}