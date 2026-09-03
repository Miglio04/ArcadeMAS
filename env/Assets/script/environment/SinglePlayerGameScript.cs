using System;
using UnityEngine;
using UnityEngine.Video;

public class SinglePlayerGameScript : Artifact
{
    protected override void HandleTriggeredEvent(ArtifactMessage message)
    {
        base.HandleTriggeredEvent(message);
        switch (message.AgentEvent)
        {
            case "playGame":
                HandlePlayGame();
                break;
            case "stopGame":
                HandleStopGame();
                break;
            default:
                Debug.LogWarning($"Unknown agent event: {message.AgentEvent}");
                break;
        }
    }

    private void HandlePlayGame()
    {
        var screenOffChild = FindDeepChild(transform, "ScreenOff");
        var screenPlayChild = FindDeepChild(transform, "ScreenOn");

        if (screenOffChild != null)
        {
            screenOffChild.gameObject.SetActive(false);
        }

        if (screenPlayChild == null)
        {
            Debug.LogWarning($"[{gameObject.name}] ScreenOn not found");
            return;
        }

        screenPlayChild.gameObject.SetActive(true);

        var player = ResolveVideoPlayer(screenPlayChild);
        if (player == null)
        {
            Debug.LogWarning($"[{gameObject.name}] VideoPlayer not found");
            return;
        }

        ConfigureMaterialOverride(player, screenPlayChild);

        if (player.clip == null && string.IsNullOrEmpty(player.url))
        {
            Debug.LogWarning($"[{gameObject.name}] VideoPlayer has no clip or URL assigned");
            return;
        }

        player.errorReceived -= OnVideoError;
        player.errorReceived += OnVideoError;

        if (!player.isPrepared)
        {
            player.prepareCompleted -= OnVideoPrepared;
            player.prepareCompleted += OnVideoPrepared;
            player.Prepare();
            return;
        }

        StartPlayback(player);
    }

    private void ConfigureMaterialOverride(VideoPlayer player, Transform screenPlayChild)
    {
        if (player.renderMode != VideoRenderMode.MaterialOverride || player.targetMaterialRenderer != null)
        {
            return;
        }

        var renderer = screenPlayChild.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        player.targetMaterialRenderer = renderer;

        if (!string.IsNullOrEmpty(player.targetMaterialProperty))
        {
            return;
        }

        player.targetMaterialProperty = renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseMap")
            ? "_BaseMap"
            : "_MainTex";
    }

    private void OnVideoPrepared(VideoPlayer source)
    {
        source.prepareCompleted -= OnVideoPrepared;
        StartPlayback(source);
    }

    private void OnVideoError(VideoPlayer source, string message)
    {
        Debug.LogError($"[{gameObject.name}] VideoPlayer error: {message}");
    }

    private void StartPlayback(VideoPlayer player)
    {
        player.Play();
    }

    private void HandleStopGame()
    {
        var screenPlayChild = FindDeepChild(transform, "ScreenOn");
        var screenOffChild = FindDeepChild(transform, "ScreenOff");

        if (screenPlayChild != null)
        {
            var player = ResolveVideoPlayer(screenPlayChild);
            if (player != null)
            {
                player.Stop();
            }
        }

        if (screenOffChild != null)
        {
            screenOffChild.gameObject.SetActive(true);
        }
    }

    private VideoPlayer ResolveVideoPlayer(Transform screenOnTransform)
    {
        if (screenOnTransform == null)
        {
            return GetComponentInChildren<VideoPlayer>(true);
        }

        return screenOnTransform.GetComponentInChildren<VideoPlayer>(true)
            ?? GetComponentInChildren<VideoPlayer>(true);
    }
}