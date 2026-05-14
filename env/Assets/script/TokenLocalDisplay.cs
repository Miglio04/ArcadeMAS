using UnityEngine;
using TMPro;

public class TokenLocalDisplay : MonoBehaviour
{
    [Header("UI Reference")]
    public TextMeshProUGUI displayTesto;

    [Header("Settings")]
    [SerializeField] private int multiplier = 1;
    [SerializeField] private string text = "Tokens: ";


    private int currentTokens = 0;
    

    void Start()
    {
        UpdateScreen();
    }

    public void IncrementLocal()
    {
        currentTokens += multiplier;
        UpdateScreen();
    }

    public void DecrementLocal()
    {
        if (currentTokens >= multiplier)
        {
            currentTokens -= multiplier;
        }
        else
        {
            currentTokens = 0;
        }
        UpdateScreen();
    }

    public void ResetLocal()
    {
        currentTokens = 0;
        UpdateScreen();
    }

    private void UpdateScreen()
    {
        if (displayTesto != null)
        {
            displayTesto.text = text + currentTokens.ToString();
        }
    }
}