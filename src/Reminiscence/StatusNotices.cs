using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Reminiscence;

internal static class StatusNotices
{
    private const string ExtractingMessage = "Extracting MT1 contents...";

    private static Font? _font;
    private static bool _fontResolved;
    private static GameObject? _extractRoot;
    private static bool _extractAttached;
    private static GameObject? _sceneLabel;
    private static Behaviour? _sceneLocalize;
    private static bool _sceneLogged;
    private static bool _menuShown;
    private static GameObject? _menuRoot;
    private static float _menuHideAt;

    internal static void ShowExtraction()
    {
        ShowOnFlame();
    }

    internal static void ShowOnFlame()
    {
        try
        {
            Component? screen = LoadingScreenInstance();
            if (screen == null)
                return;
            EnsureFlamePlate(screen);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError("Extraction notice failed: " + ex.Message);
        }
    }

    internal static void HideExtraction()
    {
        if (_sceneLabel != null)
        {
            _sceneLabel.SetActive(false);
            _sceneLabel = null;
        }

        if (_sceneLocalize != null)
        {
            _sceneLocalize.enabled = true;
            _sceneLocalize = null;
        }

        if (_extractRoot != null)
            UnityEngine.Object.Destroy(_extractRoot);
        _extractRoot = null;
        _extractAttached = false;
    }

    internal static void WatchForMainMenu(MonoBehaviour host)
    {
        if (host == null || _menuShown)
            return;
        host.StartCoroutine(WaitForMainMenu());
    }

    internal static void Tick()
    {
        if (MusicInstaller.ExtractionRunning)
        {
            ShowExtraction();
            return;
        }

        HideExtraction();
        if (!_menuShown && MusicInstaller.ReadyToReport && MenuIsReady())
            ShowMenuNotice(MusicInstaller.MenuMessage());
        if (_menuRoot != null && Time.unscaledTime >= _menuHideAt)
        {
            UnityEngine.Object.Destroy(_menuRoot);
            _menuRoot = null;
        }
    }

    private static IEnumerator WaitForMainMenu()
    {
        float waited = 0f;
        while (waited < 90f && !_menuShown)
        {
            if (MusicInstaller.ReadyToReport && MenuIsReady())
            {
                ShowMenuNotice(MusicInstaller.MenuMessage());
                yield break;
            }

            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!_menuShown)
            Plugin.LogSource.LogInfo("Main menu notice was not shown.");
    }

    private static bool MenuIsReady()
    {
        if (LoadingDisplayed())
            return false;
        return FindMainMenu() != null;
    }

    private static void ShowMenuNotice(string message)
    {
        if (_menuShown)
            return;
        _menuShown = true;
        try
        {
            if (TryShowMusicToast(message))
            {
                Plugin.LogSource.LogInfo("Main menu notice: " + message);
                return;
            }

            GameObject? root = CreateNotice(message, bottomRight: false);
            if (root == null)
                return;

            Transform? plate = root.transform.Find("Plate");
            Component? menu = FindMainMenu();
            Canvas? canvas = menu == null ? null : menu.GetComponentInParent<Canvas>();
            if (plate != null && canvas != null)
            {
                plate.SetParent(canvas.transform, false);
                plate.SetAsLastSibling();
                Place((RectTransform)plate, bottomRight: false, screenPixels: false);
                UnityEngine.Object.Destroy(root);
                _menuRoot = plate.gameObject;
            }
            else
            {
                UnityEngine.Object.DontDestroyOnLoad(root);
                _menuRoot = root;
            }

            _menuHideAt = Time.unscaledTime + 4.2f;
            Plugin.LogSource.LogInfo("Main menu notice: " + message);
        }
        catch (Exception ex)
        {
            Plugin.LogSource.LogError("Main menu notice failed: " + ex.Message);
        }
    }

    private static void EnsureFlamePlate(Component screen)
    {
        Canvas? canvas = screen.GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        if (_extractAttached && _extractRoot != null)
        {
            _extractRoot.transform.SetAsLastSibling();
            return;
        }

        GameObject? created = CreateNotice(ExtractingMessage, bottomRight: true);
        Transform? plate = created == null ? null : created.transform.Find("Plate");
        if (created == null || plate == null)
            return;

        plate.SetParent(canvas.transform, false);
        plate.SetAsLastSibling();
        Place((RectTransform)plate, bottomRight: true, screenPixels: false);
        Canvas overlay = plate.gameObject.AddComponent<Canvas>();
        overlay.overrideSorting = true;
        overlay.sortingOrder = 5000;
        UnityEngine.Object.Destroy(created);
        _extractRoot = plate.gameObject;
        _extractAttached = true;
        if (!_sceneLogged)
        {
            _sceneLogged = true;
            Plugin.LogSource.LogInfo(ExtractingMessage);
        }
    }

    private static bool TryShowMusicToast(string message)
    {
        Type? handlerType = AccessTools.TypeByName("MusicNotificationHandler");
        MethodInfo? show = handlerType == null ? null : AccessTools.Method(handlerType, "ShowContent");
        FieldInfo? uiField = handlerType == null ? null : AccessTools.Field(handlerType, "notificationUI");
        if (handlerType == null || show == null || uiField == null)
            return false;

        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(handlerType))
        {
            if (obj is not Component component)
                continue;
            if (!component.gameObject.scene.IsValid() || !component.gameObject.activeInHierarchy)
                continue;
            try
            {
                show.Invoke(component, new object[] { message });
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Music toast failed: " + ex.GetBaseException().Message);
                continue;
            }

            if (uiField.GetValue(component) is Component ui && ui.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static GameObject? CreateNotice(string message, bool bottomRight)
    {
        Font? font = ResolveFont();
        Type? textType = AccessTools.TypeByName("UnityEngine.UI.Text");
        if (font == null || textType == null)
        {
            Plugin.LogSource.LogError("Cannot draw a notice. UI text is unavailable.");
            return null;
        }

        var root = new GameObject(bottomRight ? "ReminiscenceExtractNotice" : "ReminiscenceMenuNotice");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        var plate = new GameObject("Plate");
        plate.transform.SetParent(root.transform, false);
        RectTransform plateRt = plate.AddComponent<RectTransform>();
        Place(plateRt, bottomRight, screenPixels: true);
        plate.AddComponent<CanvasGroup>();

        Type? imageType = AccessTools.TypeByName("UnityEngine.UI.Image");
        if (imageType != null)
        {
            Component image = plate.AddComponent(imageType);
            SetProperty(image, "color", new Color(0.05f, 0.03f, 0.02f, 0.92f));
            SetProperty(image, "raycastTarget", false);
        }

        var label = new GameObject("Label");
        label.transform.SetParent(plate.transform, false);
        RectTransform labelRt = label.AddComponent<RectTransform>();
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = new Vector2(18f, 6f);
        labelRt.offsetMax = new Vector2(-18f, -6f);

        Component text = label.AddComponent(textType);
        SetProperty(text, "font", font);
        SetProperty(text, "fontSize", NoticeFontSize());
        SetProperty(text, "color", Color.white);
        SetProperty(text, "raycastTarget", false);
        SetProperty(text, "text", message);
        SetEnum(text, "alignment", bottomRight ? "MiddleRight" : "MiddleCenter");
        SetEnum(text, "horizontalOverflow", "Overflow");
        SetEnum(text, "verticalOverflow", "Overflow");
        return root;
    }

    private static void Place(RectTransform rt, bool bottomRight, bool screenPixels)
    {
        if (bottomRight)
        {
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(1f, 0f);
            rt.sizeDelta = screenPixels ? new Vector2(PixelWidth() * 0.4f, PixelHeight() * 0.055f) : new Vector2(680f, 64f);
            rt.anchoredPosition = screenPixels ? new Vector2(-PixelWidth() * 0.09f, PixelHeight() * 0.04f) : new Vector2(-170f, 36f);
            return;
        }

        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = screenPixels ? new Vector2(PixelWidth() * 0.46f, PixelHeight() * 0.05f) : new Vector2(820f, 58f);
        rt.anchoredPosition = screenPixels ? new Vector2(0f, -PixelHeight() * 0.1f) : new Vector2(0f, -120f);
    }

    private static int NoticeFontSize()
    {
        return Mathf.Clamp(Mathf.RoundToInt(PixelHeight() * 0.028f), 22, 48);
    }

    private static float PixelWidth()
    {
        return Screen.width > 100 ? Screen.width : 1920f;
    }

    private static float PixelHeight()
    {
        return Screen.height > 100 ? Screen.height : 1080f;
    }

    private static Font? ResolveFont()
    {
        if (_fontResolved)
            return _font;
        _fontResolved = true;
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
            ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (_font == null)
        {
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Liberation Sans", "DejaVu Sans" }, 32);
            }
            catch (Exception ex)
            {
                Plugin.LogSource.LogError("Notice font lookup failed: " + ex.Message);
            }
        }

        if (_font == null)
            Plugin.LogSource.LogError("No font available for the Reminiscence notice.");
        return _font;
    }

    private static Component? LoadingScreenInstance()
    {
        Type? screenType = AccessTools.TypeByName("ShinyShoe.Loading.LoadingScreen");
        FieldInfo? field = screenType == null ? null : AccessTools.Field(screenType, "_loadingScreenInst");
        return field?.GetValue(null) as Component;
    }

    private static bool LoadingDisplayed()
    {
        Type? screenType = AccessTools.TypeByName("ShinyShoe.Loading.LoadingScreen");
        MethodInfo? method = screenType == null ? null : AccessTools.Method(screenType, "IsDisplaying");
        return method?.Invoke(null, null) is true;
    }

    private static Component? FindMainMenu()
    {
        Type? menuType = AccessTools.TypeByName("MainMenuScreen");
        if (menuType == null)
            return null;
        foreach (UnityEngine.Object obj in Resources.FindObjectsOfTypeAll(menuType))
        {
            if (obj is Component component && component.gameObject.scene.IsValid() && component.gameObject.activeInHierarchy)
                return component;
        }

        return null;
    }

    private static void SetEnum(object target, string property, string name)
    {
        PropertyInfo? prop = AccessTools.Property(target.GetType(), property);
        if (prop == null || !prop.PropertyType.IsEnum)
            return;
        try
        {
            prop.SetValue(target, Enum.Parse(prop.PropertyType, name));
        }
        catch (ArgumentException)
        {
        }
    }

    [HarmonyPatch]
    private static class MainMenuPatch
    {
        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static MethodBase? TargetMethod()
        {
            Type? mainMenu = AccessTools.TypeByName("MainMenuScreen");
            return mainMenu == null ? null : AccessTools.Method(mainMenu, "Initialize");
        }

        private static void Postfix(MonoBehaviour __instance)
        {
            WatchForMainMenu(__instance);
        }
    }

    private static void SetProperty(object target, string property, object? value)
    {
        PropertyInfo? prop = AccessTools.Property(target.GetType(), property);
        if (prop == null || !prop.CanWrite)
            return;
        try
        {
            prop.SetValue(target, value);
        }
        catch (Exception)
        {
        }
    }
}
