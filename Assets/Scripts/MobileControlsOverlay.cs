using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Landscape-friendly, safe-area-aware touch controls for Wildrune. Reads touches directly so
/// multiple fingers can hold the joystick and a button at the same time, independent of UGUI.
/// A mouse press can exercise one control at a time on desktop builds for quick preview/testing.
/// </summary>
[DefaultExecutionOrder(-900)]
public sealed class MobileControlsOverlay : MonoBehaviour
{
    private enum Target { None, Joystick, Jump, Sword, Bow }

    private readonly Dictionary<int, Target> activePointers = new Dictionary<int, Target>();
    private readonly Dictionary<int, Vector2> pointerPositions = new Dictionary<int, Vector2>();
    private RectTransform safeAreaRoot;
    private RectTransform joystickRect;
    private RectTransform joystickRingRect;
    private RectTransform joystickThumb;
    private Image joystickBaseImage;
    private Image joystickRingImage;
    private Image joystickThumbImage;
    private Image jumpImage;
    private Image swordImage;
    private Image bowImage;
    private RectTransform jumpRingRect;
    private RectTransform swordRingRect;
    private RectTransform bowRingRect;
    private Text jumpLabel;
    private Text swordLabel;
    private Text bowLabel;
    private Canvas rootCanvas;
    private float lastCanvasScaleFactor = -1f;
    private Font controlFont;
    private int joystickPointerId = int.MinValue;
    private Rect lastSafeArea;
    private int lastScreenWidth;
    private int lastScreenHeight;

    public static MobileControlsOverlay Create(Transform parent)
    {
        GameObject overlay = new GameObject("Touch Controls", typeof(RectTransform), typeof(MobileControlsOverlay));
        overlay.transform.SetParent(parent, false);
        RectTransform rect = overlay.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        MobileControlsOverlay controls = overlay.GetComponent<MobileControlsOverlay>();
        controls.BuildVisuals();
        return controls;
    }

    private void BuildVisuals()
    {
        // The touch movement/action overlay relies on concurrent fingers (left stick + right action).
        Input.multiTouchEnabled = true;
        rootCanvas = GetComponentInParent<Canvas>();
        controlFont = Resources.Load<Font>("Art/m5x7");
        if (controlFont == null) controlFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        GameObject safe = new GameObject("Safe Touch Area", typeof(RectTransform));
        safe.transform.SetParent(transform, false);
        safeAreaRoot = safe.GetComponent<RectTransform>();
        safeAreaRoot.anchorMin = Vector2.zero;
        safeAreaRoot.anchorMax = Vector2.one;
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
        UpdateSafeArea();

        // Left thumb zone; only horizontal movement is needed for this side-scrolling platformer.
        joystickBaseImage = MakeCircle("Movement Joystick", safeAreaRoot, new Vector2(0.135f, 0.27f),
            new Vector2(176f, 176f), new Color32(12, 26, 42, 155), true, out joystickRect);
        joystickRingImage = MakeCircle("Joystick Ring", safeAreaRoot, new Vector2(0.135f, 0.27f),
            new Vector2(176f, 176f), new Color32(191, 220, 233, 80), false, out joystickRingRect);
        joystickRingImage.sprite = CircleRingSprite;
        joystickRingImage.raycastTarget = false;
        joystickThumbImage = MakeCircle("Joystick Thumb", safeAreaRoot, new Vector2(0.135f, 0.27f),
            new Vector2(70f, 70f), new Color32(75, 159, 165, 70), true, out joystickThumb);
        joystickThumb.SetAsLastSibling();

        // The right-hand buttons form one compact thumb cluster at the lower-right edge: sword left of jump, bow above jump.
        // Positions are calculated from actual button sizes so they stay close without hit-box overlap.
        Vector2 actionAnchor = new Vector2(1f, 0f);
        jumpImage = MakeButton("JUMP", Target.Jump, actionAnchor, new Vector2(118f, 118f),
            new Color32(204, 148, 57, 225), out jumpRingRect, out jumpLabel);
        swordImage = MakeButton("SWORD", Target.Sword, actionAnchor, new Vector2(94f, 94f),
            new Color32(49, 113, 151, 220), out swordRingRect, out swordLabel);
        bowImage = MakeButton("BOW", Target.Bow, actionAnchor, new Vector2(92f, 92f),
            new Color32(54, 128, 94, 220), out bowRingRect, out bowLabel);
        UpdateResponsiveSizing(true);
    }

    private Image MakeButton(string label, Target target, Vector2 anchor, Vector2 size, Color color,
        out RectTransform ringRect, out Text labelText)
    {
        RectTransform rect;
        Image image = MakeCircle(label + " Touch Button", safeAreaRoot, anchor, size, color, true, out rect);
        image.sprite = CircleSprite;
        image.raycastTarget = false;
        Image ring = MakeCircle(label + " Button Ring", safeAreaRoot, anchor, size,
            new Color(0.86f, 0.93f, 0.96f, 0.65f), false, out ringRect);
        ring.sprite = CircleRingSprite;
        ring.raycastTarget = false;

        GameObject labelObject = new GameObject(label + " Label", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(rect, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = new Vector2(4f, 4f);
        labelRect.offsetMax = new Vector2(-4f, -4f);
        Text text = labelObject.GetComponent<Text>();
        labelText = text;
        text.font = controlFont;
        text.text = label;
        text.fontSize = target == Target.Jump ? 22 : 18;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        Outline outline = labelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
        outline.effectDistance = new Vector2(1f, -1f);
        return image;
    }

    private float ResponsiveSize(float referenceSize, float minimumScreenPixels)
    {
        float scale = rootCanvas != null ? Mathf.Max(0.1f, rootCanvas.scaleFactor) : 1f;
        return Mathf.Max(referenceSize, minimumScreenPixels / scale);
    }

    private void UpdateResponsiveSizing(bool force = false)
    {
        if (rootCanvas == null) rootCanvas = GetComponentInParent<Canvas>();
        float scale = rootCanvas != null ? Mathf.Max(0.1f, rootCanvas.scaleFactor) : 1f;
        if (!force && Mathf.Abs(scale - lastCanvasScaleFactor) < 0.001f) return;
        lastCanvasScaleFactor = scale;

        SetSize(joystickRect, ResponsiveSize(176f, 156f));
        SetSize(joystickRingRect, ResponsiveSize(176f, 156f));
        SetSize(joystickThumb, ResponsiveSize(70f, 64f));
        SetSize(jumpImage != null ? jumpImage.rectTransform : null, ResponsiveSize(118f, 108f));
        SetSize(jumpRingRect, ResponsiveSize(118f, 108f));
        SetSize(swordImage != null ? swordImage.rectTransform : null, ResponsiveSize(94f, 92f));
        SetSize(swordRingRect, ResponsiveSize(94f, 92f));
        SetSize(bowImage != null ? bowImage.rectTransform : null, ResponsiveSize(92f, 92f));
        SetSize(bowRingRect, ResponsiveSize(92f, 92f));
        if (jumpLabel != null) jumpLabel.fontSize = Mathf.CeilToInt(ResponsiveSize(22f, 22f));
        if (swordLabel != null) swordLabel.fontSize = Mathf.CeilToInt(ResponsiveSize(18f, 18f));
        if (bowLabel != null) bowLabel.fontSize = Mathf.CeilToInt(ResponsiveSize(18f, 18f));
        PositionActionCluster();
    }

    private static void SetSize(RectTransform rect, float size)
    {
        if (rect != null) rect.sizeDelta = new Vector2(size, size);
    }

    private void PositionActionCluster()
    {
        if (safeAreaRoot == null || jumpImage == null || swordImage == null || bowImage == null) return;
        // Keep a small physical gap even when CanvasScaler uses a very small logical scale.
        float gap = ResponsiveSize(9f, 8f);
        float inset = ResponsiveSize(15f, 10f);
        float jumpSize = jumpImage.rectTransform.rect.width;
        float swordSize = swordImage.rectTransform.rect.width;
        float bowSize = bowImage.rectTransform.rect.width;

        Vector2 jumpPos = new Vector2(-(inset + jumpSize * 0.5f), inset + jumpSize * 0.5f);
        // Sword sits immediately to the left of jump; Bow is directly above Jump.
        // Every pair has a real gap between the RectTransform hit boxes (not just the circle art),
        // so a touch at the edge cannot accidentally trigger a neighboring action.
        Vector2 swordPos = new Vector2(jumpPos.x - (jumpSize * 0.5f + swordSize * 0.5f + gap), jumpPos.y);
        Vector2 bowPos = new Vector2(jumpPos.x, jumpPos.y + (jumpSize * 0.5f + bowSize * 0.5f + gap));

        PlaceAtBottomRight(jumpImage.rectTransform, jumpPos);
        PlaceAtBottomRight(jumpRingRect, jumpPos);
        PlaceAtBottomRight(swordImage.rectTransform, swordPos);
        PlaceAtBottomRight(swordRingRect, swordPos);
        PlaceAtBottomRight(bowImage.rectTransform, bowPos);
        PlaceAtBottomRight(bowRingRect, bowPos);
    }

    private static void PlaceAtBottomRight(RectTransform rect, Vector2 position)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
    }

    private Image MakeCircle(string objectName, Transform parent, Vector2 anchor, Vector2 size,
        Color color, bool filled, out RectTransform rect)
    {
        GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = Vector2.zero;
        Image image = go.GetComponent<Image>();
        image.sprite = filled ? CircleSprite : CircleRingSprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Sprite circleSprite;
    private static Sprite circleRingSprite;
    private static Sprite CircleSprite => circleSprite != null ? circleSprite : (circleSprite = CreateCircleSprite(false));
    private static Sprite CircleRingSprite => circleRingSprite != null ? circleRingSprite : (circleRingSprite = CreateCircleSprite(true));

    private static Sprite CreateCircleSprite(bool ringOnly)
    {
        const int resolution = 96;
        Texture2D texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true);
        texture.name = ringOnly ? "Wildrune Touch Circle Ring" : "Wildrune Touch Circle";
        texture.filterMode = FilterMode.Bilinear;
        texture.wrapMode = TextureWrapMode.Clamp;
        Color32[] pixels = new Color32[resolution * resolution];
        float center = (resolution - 1) * 0.5f;
        float outer = resolution * 0.48f;
        float inner = resolution * (ringOnly ? 0.425f : 0.0f);
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                byte alpha = 0;
                if (distance <= outer)
                {
                    if (ringOnly) alpha = (byte)(distance >= inner ? 255 : 0);
                    else alpha = 255;
                }
                pixels[y * resolution + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        return Sprite.Create(texture, new Rect(0, 0, resolution, resolution), new Vector2(0.5f, 0.5f), resolution);
    }

    private void Update()
    {
        if (!GameControls.TouchscreenMode)
        {
            if (activePointers.Count > 0) activePointers.Clear();
            if (pointerPositions.Count > 0) pointerPositions.Clear();
            joystickPointerId = int.MinValue;
            GameControls.ClearTouchInput();
            return;
        }

        UpdateSafeArea();
        UpdateResponsiveSizing();
        ProcessTouches();
        // Desktop builds can also preview the selected touch scheme with a mouse when no real touch
        // is active; a real touch stream takes priority to avoid duplicate simulated clicks.
        ProcessMousePreview();
        UpdateJoystick();
        bool jumpHeld = false;
        foreach (KeyValuePair<int, Target> pointer in activePointers)
            if (pointer.Value == Target.Jump) { jumpHeld = true; break; }
        GameControls.SetTouchJumpHeld(jumpHeld);
        UpdateVisualFeedback();
    }

    private void ProcessTouches()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            int id = touch.fingerId;
            if (touch.phase == TouchPhase.Began)
            {
                Target target = HitTest(touch.position);
                if (target == Target.None) continue;
                activePointers[id] = target;
                pointerPositions[id] = touch.position;
                OnTargetPressed(id, target);
            }
            else if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
            {
                if (activePointers.ContainsKey(id)) pointerPositions[id] = touch.position;
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                ReleasePointer(id);
            }
        }
    }

    private void ProcessMousePreview()
    {
        if (Input.touchCount > 0) return; // Device Simulator or physical touch already supplies events.
        const int mousePointerId = -90001;
        if (Input.GetMouseButtonDown(0))
        {
            Target target = HitTest(Input.mousePosition);
            if (target != Target.None)
            {
                activePointers[mousePointerId] = target;
                pointerPositions[mousePointerId] = Input.mousePosition;
                OnTargetPressed(mousePointerId, target);
            }
        }
        if (activePointers.ContainsKey(mousePointerId) && Input.GetMouseButton(0))
            pointerPositions[mousePointerId] = Input.mousePosition;
        if (Input.GetMouseButtonUp(0)) ReleasePointer(mousePointerId);
    }

    private void OnTargetPressed(int pointerId, Target target)
    {
        switch (target)
        {
            case Target.Joystick:
                // Floating stick: its base moves to the initial thumb contact within the left movement zone.
                if (joystickPointerId == int.MinValue)
                {
                    joystickPointerId = pointerId;
                    Vector2 startPosition;
                    if (pointerPositions.TryGetValue(pointerId, out startPosition)) SetJoystickCenter(startPosition);
                }
                break;
            case Target.Jump: GameControls.PressTouchAction(GameControls.Action.Jump); break;
            case Target.Sword: GameControls.PressTouchAction(GameControls.Action.Sword); break;
            case Target.Bow: GameControls.PressTouchAction(GameControls.Action.Bow); break;
        }
    }

    private void ReleasePointer(int pointerId)
    {
        Target target;
        bool releasedOwner = activePointers.TryGetValue(pointerId, out target) &&
                             target == Target.Joystick && joystickPointerId == pointerId;
        activePointers.Remove(pointerId);
        pointerPositions.Remove(pointerId);
        if (!releasedOwner) return;

        joystickPointerId = int.MinValue;
        // If a second finger had already touched the movement zone, let it take over smoothly.
        foreach (KeyValuePair<int, Target> pointer in activePointers)
        {
            if (pointer.Value != Target.Joystick) continue;
            joystickPointerId = pointer.Key;
            Vector2 position;
            if (pointerPositions.TryGetValue(pointer.Key, out position)) SetJoystickCenter(position);
            break;
        }
    }

    private Target HitTest(Vector2 screenPosition)
    {
        if (IsJoystickMovementZone(screenPosition)) return Target.Joystick;
        if (RectTransformUtility.RectangleContainsScreenPoint(jumpImage.rectTransform, screenPosition, null)) return Target.Jump;
        if (RectTransformUtility.RectangleContainsScreenPoint(swordImage.rectTransform, screenPosition, null)) return Target.Sword;
        if (RectTransformUtility.RectangleContainsScreenPoint(bowImage.rectTransform, screenPosition, null)) return Target.Bow;
        return Target.None;
    }

    private bool IsJoystickMovementZone(Vector2 screenPosition)
    {
        if (safeAreaRoot == null) return false;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(safeAreaRoot, screenPosition, null, out local)) return false;
        Rect area = safeAreaRoot.rect;
        if (area.width <= 0f || area.height <= 0f) return false;
        float normalizedX = (local.x - area.xMin) / area.width;
        float normalizedY = (local.y - area.yMin) / area.height;
        // Reserve the left 43% / lower 46% for movement, leaving the toast area and right-side action cluster clear.
        return normalizedX >= 0.02f && normalizedX <= 0.43f && normalizedY >= 0.02f && normalizedY <= 0.46f;
    }

    private void SetJoystickCenter(Vector2 screenPosition)
    {
        if (safeAreaRoot == null || joystickRect == null) return;
        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(safeAreaRoot, screenPosition, null, out local)) return;
        Rect area = safeAreaRoot.rect;
        float marginX = joystickRect.rect.width * 0.5f / Mathf.Max(1f, area.width) + 0.01f;
        float marginY = joystickRect.rect.height * 0.5f / Mathf.Max(1f, area.height) + 0.015f;
        Vector2 anchor = new Vector2(
            Mathf.Clamp((local.x - area.xMin) / Mathf.Max(1f, area.width), marginX, 1f - marginX),
            Mathf.Clamp((local.y - area.yMin) / Mathf.Max(1f, area.height), marginY, 1f - marginY));
        SetAnchor(joystickRect, anchor);
        SetAnchor(joystickRingRect, anchor);
        SetAnchor(joystickThumb, anchor);
    }

    private static void SetAnchor(RectTransform rect, Vector2 anchor)
    {
        if (rect == null) return;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.anchoredPosition = Vector2.zero;
    }

    private void UpdateJoystick()
    {
        float horizontal = 0f;
        if (joystickPointerId != int.MinValue)
        {
            Vector2 screenPosition;
            if (pointerPositions.TryGetValue(joystickPointerId, out screenPosition))
            {
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(joystickRect, screenPosition, null, out local))
                {
                    float raw = Mathf.Clamp(local.x / Mathf.Max(1f, joystickRect.rect.width * 0.39f), -1f, 1f);
                    float absolute = Mathf.Abs(raw);
                    horizontal = absolute < 0.16f ? 0f : Mathf.Sign(raw) * ((absolute - 0.16f) / 0.84f);
                }
            }
        }
        GameControls.SetTouchHorizontal(horizontal);
        if (joystickThumb != null && joystickRect != null)
            joystickThumb.anchoredPosition = new Vector2(horizontal * joystickRect.rect.width * 0.26f, 0f);
    }

    private void UpdateVisualFeedback()
    {
        bool stickActive = joystickPointerId != int.MinValue;
        if (joystickBaseImage != null) joystickBaseImage.color = stickActive
            ? new Color32(12, 26, 42, 155) : new Color32(12, 26, 42, 48);
        if (joystickRingImage != null) joystickRingImage.color = stickActive
            ? new Color32(191, 220, 233, 155) : new Color32(191, 220, 233, 55);
        if (joystickThumbImage != null) joystickThumbImage.color = stickActive
            ? new Color32(75, 159, 165, 225) : new Color32(75, 159, 165, 45);
        SetButtonActive(Target.Jump, jumpImage, new Color32(204, 148, 57, 225));
        SetButtonActive(Target.Sword, swordImage, new Color32(49, 113, 151, 220));
        SetButtonActive(Target.Bow, bowImage, new Color32(54, 128, 94, 220));
    }

    private void SetButtonActive(Target target, Image image, Color idleColor)
    {
        bool held = false;
        foreach (KeyValuePair<int, Target> pointer in activePointers)
            if (pointer.Value == target) { held = true; break; }
        SetActiveColor(image, idleColor, held);
    }

    private static void SetActiveColor(Image image, Color idleColor, bool active)
    {
        if (image == null) return;
        Color on = Color.Lerp(idleColor, Color.white, 0.22f);
        image.color = active ? on : idleColor;
    }

    private void UpdateSafeArea()
    {
        if (safeAreaRoot == null) return;
        Rect area = Screen.safeArea;
        if (Screen.width <= 0 || Screen.height <= 0) return;
        if (area == lastSafeArea && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return;
        lastSafeArea = area;
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        safeAreaRoot.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
        safeAreaRoot.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
        safeAreaRoot.offsetMin = Vector2.zero;
        safeAreaRoot.offsetMax = Vector2.zero;
    }

    private void OnDisable() => GameControls.ClearTouchInput();
    private void OnDestroy() => GameControls.ClearTouchInput();
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ClearActiveTouchState();
    }

    private void OnApplicationPause(bool paused)
    {
        // Android/iOS can suspend without a distinct focus-loss callback. Never carry a held
        // joystick, jump, or attack through the app background/resume cycle.
        if (paused) ClearActiveTouchState();
    }

    private void ClearActiveTouchState()
    {
        activePointers.Clear();
        pointerPositions.Clear();
        joystickPointerId = int.MinValue;
        GameControls.ClearTouchInput();
    }
}
