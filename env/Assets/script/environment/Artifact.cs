using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;
using WebSocketSharp;
using script.core.util;
using Unity.VisualScripting;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

[ExecuteAlways]
public class Artifact : AbstractArtifact
{
    // All artifact properties
    //TODO: Find out what subclasses are needed and remove the rest
    // public List<CoffeeInfo> barProperties;
    // public List<FruitInfo> fruitShopProperties;
    // public List<ClothesInfo> dressShopProperties;
    // public bool doorProperties;
    public Dictionary<string, object> Properties { get; private set; }

    private Renderer objectRenderer;
    private TextMeshProUGUI tokenScreenText;
    private TextMeshProUGUI priceScreenText;
    private ArtifactMessage lastArtifactMessage;

    private void OnValidate()
    {
        ResolveProperties();
    }

    private void OnDestroy()
    {
        var grabbable = gameObject.GetComponent<XRGrabInteractable>();

        if (grabbable != null)
        {
            grabbable.selectEntered.RemoveAllListeners();
            grabbable.selectExited.RemoveAllListeners();
        }
    }

    protected virtual void Awake()
    {
        objectRenderer = GetComponent<Renderer>();

        ResolveProperties();
        propertyNames ??= new List<string>(); // If null, initialize the list

        propertyNames.Clear();
        // Retrieve all fields
        var fields = GetType().GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (var field in fields)
        {
            if (field.Name != "port" && field.Name != "objInUse")
            {
                propertyNames.Add(field.Name);
            }
        }

        objInUse = gameObject;

        if (Application.IsPlaying(gameObject))
        {
            // Play logic
            // Retrieve the property that belongs to the artifact       
            var artifactPropertyName = artifactType.ToString();
            artifactPropertyName = char.ToLower(artifactPropertyName[0]) + artifactPropertyName[1..] + "Properties";

            // Find the field with the specified name
            var filteredField = Array.Find(fields, f => f.Name == artifactPropertyName);
            if (filteredField != null)
            {
                // Map in JSON the artifact property        
                artifactProperties = EscapeJson(convertObjectIntoJson(filteredField.GetValue(this)));
                Debug.Log("Artifact property: " + artifactProperties);
            }

            initializeWebSocketConnection(OnMessage);
        }
        else
        {
            // Editor logic
            foreach (var prop in propertyNames)
            {
                print(prop);
            }
        }
    }

    protected virtual void OnMessage(object sender, MessageEventArgs e)
    {
        var data = e.Data;

        ArtifactMessage message = null;
        try
        {
            message = JsonConvert.DeserializeObject<ArtifactMessage>(data);
        }
        catch (Exception)
        {
            Debug.LogError(data);
            Debug.LogError("Message could not be converted.");
            return;
        }

        try
        {
            var messagePayload = message.MessagePayload;
            // Ensure Unity API calls run on main thread
            UnityMainThreadDispatcher.Instance().Enqueue(() =>
            {
                switch (messagePayload)
                {
                    case "is_grabbable":
                        RetrieveGrabbableStatus(message.AgentName);
                        break;
                    case "triggered":
                        HandleTriggeredEvent(message);
                        break;
                }
            });
        }
        catch (Exception ex)
        {
            Debug.LogError($"[{message.AgentName} Artifact] Exception occurred OnMessage " + ex);
        }
    }

    private async void RetrieveGrabbableStatus(string artifactName)
    {
        // Create a TaskCompletionSource to await the result
        var tcs = new TaskCompletionSource<bool>();
        await UnityMainThreadDispatcher.Instance()
            .EnqueueAsync(() =>
            {
                var artifact = GameObject.Find(artifactName);

                if (artifact == null)
                {
                    Debug.LogError($"Artifact {artifactName} not found.");
                    tcs.SetResult(false);
                    return;
                }

                var artifactComponent = artifact.GetComponent<Artifact>();
                if (artifactComponent == null)
                {
                    Debug.LogError($"Artifact component not found on {artifactName}.");
                    tcs.SetResult(false);
                    return;
                }

                var grabbable = artifactComponent.isGrabbable;
                tcs.SetResult(grabbable);
            });
        var grabbableStatus = await tcs.Task;

        wsChannel.sendMessage(UnityJacamoIntegrationUtil.CreateAndConvertJacamoMessageIntoJsonString(
            "grabbableStatus", null, "is_grabbable", artifactName, grabbableStatus));
    }

    private void HandleTriggeredEvent(ArtifactMessage message)
    {
        lastArtifactMessage = message;

        switch (message.AgentEvent)
        {
            case "playGame":
                HandlePlayGame();
                break;
            case "stopGame":
                HandleStopGame();
                break;
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

    private void HandlePlayGame()
    {
        Transform screenOffChild = FindDeepChild(transform, "ScreenOff");
        Transform screenPlayChild = FindDeepChild(transform, "ScreenOn");

        if (screenOffChild != null)
        {
            screenOffChild.gameObject.SetActive(false);
            Debug.Log($"[{gameObject.name}] ScreenOff deactivated");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] ScreenOff child not found");
        }

        if (screenPlayChild != null)
        {
            if (!screenPlayChild.gameObject.activeSelf)
            {
                screenPlayChild.gameObject.SetActive(true);
                Debug.Log($"[{gameObject.name}] ScreenPlay activated");
            }

            var player = screenPlayChild.GetComponent<VideoPlayer>();
            if (player == null)
            {
                player = screenPlayChild.GetComponentInChildren<VideoPlayer>(true);
            }

            if (player != null)
            {
                if (!player.isPrepared)
                {
                    player.Prepare();
                    Debug.Log($"[{gameObject.name}] Preparing VideoPlayer for playback");
                }

                player.Play();
                Debug.Log($"[{gameObject.name}] ScreenPlay video started; isPlaying={player.isPlaying}");
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] VideoPlayer not found on ScreenPlay or its children");
            }
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] ScreenPlay child not found");
        }
    }

    private void HandleStopGame()
    {
        Transform screenPlayChild = FindDeepChild(transform, "ScreenOn");
        Transform screenOffChild = FindDeepChild(transform, "ScreenOff");

        if (screenPlayChild != null)
        {
            var player = screenPlayChild.GetComponent<VideoPlayer>();
            if (player == null)
            {
                player = screenPlayChild.GetComponentInChildren<VideoPlayer>(true);
            }

            if (player != null)
            {
                player.Stop();
                Debug.Log($"[{gameObject.name}] ScreenPlay video stopped; isPlaying={player.isPlaying}");
            }
            else
            {
                Debug.LogWarning($"[{gameObject.name}] VideoPlayer not found on ScreenPlay or its children");
            }
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] ScreenPlay child not found");
        }

        if (screenOffChild != null)
        {
            screenOffChild.gameObject.SetActive(true);
            Debug.Log($"[{gameObject.name}] ScreenOff activated");
        }
        else
        {
            Debug.LogWarning($"[{gameObject.name}] ScreenOff child not found");
        }
    }

    private Transform FindDeepChild(Transform parent, string childName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == childName)
                return child;
            var result = FindDeepChild(child, childName);
            if (result != null)
                return result;
        }
        return null;
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

    private void ResolveProperties()
    {
        var props = ArtifactResolver.GetAllProperties($"{artifactType}Artifact");

        if (props is not { Count: > 0 }) return;

        Properties = new Dictionary<string, object>();
        foreach (var prop in props)
        {
            if (prop.Name == "canBeGrabbedByHumanUser")
            {
                CreateVRGrabbableComponent();
            }

            Properties[prop.Name] = prop.Value;
        }
    }
    private void CreateVRGrabbableComponent()
    {
        Debug.Log("[DEBUG] Creating VR Grabbable Component");

        var grabbable = GetXRGrabbable();
        SetupInteractionManager(grabbable);

        // Make sure the collider is set up for interaction
        var componentCollider = gameObject.GetComponent<Collider>();
        if (componentCollider == null)
        {
            gameObject.AddComponent<BoxCollider>();
            Debug.Log("[DEBUG] BoxCollider added to Artifact GameObject for XR grabbing.");
        }

        Debug.Log("[DEBUG] VR Grabbable successfully created.");

        // Listeners for grabbed and release events, we'll send the wsMessages from here
        grabbable.selectEntered.AddListener(OnGrabbedVR);
        grabbable.selectExited.AddListener(OnReleasedVR);
    }

    private void OnGrabbedVR(SelectEnterEventArgs arg)
    {
        Debug.Log("[DEBUG] Artifact grabbed in VR.");

        // Send a Brain Message to JaCaMo
        var msg = new BrainMessage("user", gameObject.name, "grabbed", null);
        wsChannel.sendMessage(JsonConvert.SerializeObject(msg));

    }

    private void OnReleasedVR(SelectExitEventArgs args)
    {
        Debug.Log("[DEBUG] Artifact released in VR.");

        // Send a Brain Message to JaCaMo
        var msg = new BrainMessage("user", gameObject.name, "grabbed", null);
        wsChannel.sendMessage(JsonConvert.SerializeObject(msg));
    }

    private void SetupInteractionManager(XRGrabInteractable grabbable)
    {
        grabbable.interactionManager = FindFirstObjectByType<XRInteractionManager>();
        if (grabbable.interactionManager != null) return;

        grabbable.interactionManager = gameObject.AddComponent<XRInteractionManager>();
        Debug.Log("[DEBUG] XRInteractionManager component added to Artifact GameObject.");
    }

    private XRGrabInteractable GetXRGrabbable()
    {
        var grabbable = gameObject.GetComponent<XRGrabInteractable>();
        if (grabbable != null) return grabbable;

        grabbable = gameObject.AddComponent<XRGrabInteractable>();
        Debug.Log("[DEBUG] XRGrabInteractable component added to Artifact GameObject.");

        return grabbable;
    }

    /*
    public void Toggle()
    {
        if (artifactType.ToString() == "Lever")
        {
            Debug.Log($"[DEBUG] Leva {gameObject.name} azionata!");

            if (objectRenderer != null)
            {
                objectRenderer.material.color = Color.green;
            }

            string jsonMessage = "{\"type\": \"toggled\"}";

            if (wsChannel != null)
            {
                wsChannel.sendMessage(jsonMessage);
            }
            else
            {
                Debug.LogWarning("[WARNING] WebSocket non connessa.");
            }
        }
    }
    public void Untoggle()
    {
        if (artifactType.ToString() == "Lever")
        {
            Debug.Log($"[DEBUG] Leva {gameObject.name} deazionata!");

            if (objectRenderer != null)
            {
                objectRenderer.material.color = Color.red;
            }

            string jsonMessage = "{\"type\": \"untoggled\"}";

            if (wsChannel != null)
            {
                wsChannel.sendMessage(jsonMessage);
            }
            else
            {
                Debug.LogWarning("[WARNING] WebSocket non connessa.");
            }
        }
    }
    */

    [Header("JaCaMo Action Settings")]
    [HideInInspector] public string primaryActionToTrigger;
    [HideInInspector] public string secondaryActionToTrigger;
    [HideInInspector] public string tertiaryActionToTrigger;

    public void ExecutePrimaryAction()
    {
        SendActionToJacamo(primaryActionToTrigger);
    }

    public void ExecuteSecondaryAction()
    {
        SendActionToJacamo(secondaryActionToTrigger);
    }

    public void ExecuteTertiaryAction()
    {
        SendActionToJacamo(tertiaryActionToTrigger);
    }

    private void SendActionToJacamo(string actionName)
    {
        if (string.IsNullOrEmpty(actionName) || actionName == "None") return;

        Debug.Log($"[DEBUG] Esecuzione azione '{actionName}' su {gameObject.name} inviata a JaCaMo!");

        string jsonMessage = $"{{\"type\": \"{actionName}\"}}";

        if (wsChannel != null)
        {
            wsChannel.sendMessage(jsonMessage);
        }
        else
        {
            Debug.LogWarning("[WARNING] WebSocket non connessa.");
        }
    }
}