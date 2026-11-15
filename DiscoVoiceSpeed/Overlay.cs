using System;
using Il2CppInterop.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DiscoVoiceSpeed;

/// <summary>
/// A small on-screen message (e.g. "Voice speed 1.75x") near the top of the screen,
/// in the game's TextMeshPro font, that fades out after a moment.
/// Built from uGUI + TextMeshPro, which the game itself uses.
/// </summary>
internal static class Overlay
{
    const float ShowSeconds = 1.6f, FadeSeconds = 0.5f;

    static GameObject _root;
    static CanvasGroup _group;
    static RectTransform _panel;
    static TextMeshProUGUI _text;
    static float _shownAt = -100f;
    static bool _failed;

    public static void Show(string message)
    {
        if (!Plugin.ShowMessages.Value || _failed) return;
        try
        {
            if (_root == null) Build();
            else if (!_onGameFont)
            {
                // The first message came before the game font was loaded: try again.
                var f = FindGameFont();
                if (f != null) { _text.font = f; _onGameFont = true; }
            }
            _text.text = message;
            var size = _text.GetPreferredValues(message);
            _panel.sizeDelta = new Vector2(size.x + 48f, size.y + 20f);
            _group.alpha = 1f;
            _root.SetActive(true);
            _shownAt = Time.unscaledTime;
        }
        catch (Exception e)
        {
            _failed = true; // don't retry every keypress; the log message still goes out
            Plugin.L.LogError($"On-screen message unavailable: {e}");
        }
    }

    /// <summary>Called every frame by VoWatcher.</summary>
    public static void Tick()
    {
        if (_root == null || !_root.activeSelf) return;
        float t = Time.unscaledTime - _shownAt;
        if (t < ShowSeconds) return;
        float a = 1f - (t - ShowSeconds) / FadeSeconds;
        if (a <= 0f) { _root.SetActive(false); return; }
        _group.alpha = a;
    }

    static void Build()
    {
        _root = new GameObject("DiscoVoiceSpeed Overlay");
        Object.DontDestroyOnLoad(_root);

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _group = _root.AddComponent<CanvasGroup>();
        _group.interactable = false;
        _group.blocksRaycasts = false;

        // Dark rounded-ish panel behind the text.
        var panelGo = new GameObject("Panel");
        panelGo.transform.SetParent(_root.transform, false);
        var bg = panelGo.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.72f);
        bg.raycastTarget = false;
        _panel = panelGo.GetComponent<RectTransform>();
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 1f);
        _panel.pivot = new Vector2(0.5f, 1f);
        _panel.anchoredPosition = new Vector2(0f, -96f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panelGo.transform, false);
        _text = textGo.AddComponent<TextMeshProUGUI>();
        var font = FindGameFont();
        _onGameFont = font != null;
        if (font == null)
        {
            Plugin.L.LogWarning($"Font {GameFont} not loaded yet; using TextMeshPro's default font for now");
            try { font = TMP_Settings.defaultFontAsset; } catch { font = null; }
        }
        if (font != null) _text.font = font;
        _text.fontSize = 30f;
        _text.color = new Color(0.93f, 0.90f, 0.82f, 1f);
        _text.alignment = TextAlignmentOptions.Center;
        _text.enableWordWrapping = false;
        _text.raycastTarget = false;
        var tr = textGo.GetComponent<RectTransform>();
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = tr.offsetMax = Vector2.zero;

        _root.SetActive(false);
    }

    const string GameFont = "Dobra-Book SDF"; // the game's main UI font

    static bool _onGameFont;

    /// <summary>The game's UI font, or null if it isn't loaded (yet, or after a game update).</summary>
    static TMP_FontAsset FindGameFont()
    {
        foreach (var o in Resources.FindObjectsOfTypeAll(Il2CppType.Of<TMP_FontAsset>()))
        {
            var f = o.TryCast<TMP_FontAsset>();
            if (f != null && f.name == GameFont) return f;
        }
        return null;
    }
}
