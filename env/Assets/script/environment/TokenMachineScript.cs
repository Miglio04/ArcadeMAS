using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Events;
using WebSocketSharp;
using Newtonsoft.Json.Linq;
using TMPro;

public class TokenMachineScript : Artifact
{
    private TextMeshProUGUI tokenScreenText;
    private TextMeshProUGUI priceScreenText;
    protected override void HandleTriggeredEvent(ArtifactMessage message)
    {
        lastArtifactMessage = message;
        switch (message.AgentEvent)
        {
            case "pay":
                HandlePayAction();
                break;
            case "incrementTokens":
                HandleIncrementTokens();
                break;
            case "decrementTokens":
                HandleDecrementTokens();
                break;
            case "clearTokenMachine":
                HandleClearTokenMachine();
                break;
            default:
                Debug.LogWarning($"Unknown agent event: {message.AgentEvent}");
                break;
        }
    }

    private void HandlePayAction()
    {
        Debug.Log($"[{gameObject.name}] Pay action triggered");
        // Implement token machine pay logic here
    }

    private void HandleIncrementTokens()
    {
        Debug.Log($"[{gameObject.name}] Increment tokens triggered");
        UpdateTokenMachineDisplayFromMessage();
    }

    private void HandleDecrementTokens()
    {
        Debug.Log($"[{gameObject.name}] Decrement tokens triggered");
        UpdateTokenMachineDisplayFromMessage();
    }

    private void HandleClearTokenMachine()
    {
        Debug.Log($"[{gameObject.name}] Clear token machine triggered");
        SetTokenMachineDisplay(0);
    }

    private void UpdateTokenMachineDisplayFromMessage()
    {
        if (TryReadTokenCountFromMessage(out var tokens))
        {
            SetTokenMachineDisplay(tokens);
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] Token count missing or invalid in artifact message.");
        }
    }

    private bool TryReadTokenCountFromMessage(out int tokens)
    {
        tokens = 0;

        if (lastArtifactMessage == null || lastArtifactMessage.Param == null)
        {
            return false;
        }

        try
        {
            var paramObject = lastArtifactMessage.Param as JObject ?? JObject.FromObject(lastArtifactMessage.Param);
            var tokenValue = paramObject["tokens"];

            if (tokenValue == null)
            {
                return false;
            }

            tokens = tokenValue.Value<int>();
            return true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{gameObject.name}] Failed to read token count from message: {ex.Message}");
            return false;
        }
    }

    private void SetTokenMachineDisplay(int tokens)
    {
        EnsureTokenMachineTextReferences();

        if (tokenScreenText != null)
        {
            tokenScreenText.text = $"Tokens: {tokens}";
        }

        if (priceScreenText != null)
        {
            priceScreenText.text = $"Price: {(tokens * 2)}";
        }
    }

    private void EnsureTokenMachineTextReferences()
    {
        if (tokenScreenText == null)
        {
            var tokensScreen = FindDeepChild(transform, "TokensScreen");
            if (tokensScreen != null)
            {
                tokenScreenText = tokensScreen.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        if (priceScreenText == null)
        {
            var priceScreen = FindDeepChild(transform, "PriceScreen");
            if (priceScreen != null)
            {
                priceScreenText = priceScreen.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }
    }
}
