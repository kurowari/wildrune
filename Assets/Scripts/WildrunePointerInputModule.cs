using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Lightweight legacy-input UI module. Supports mouse clicks and independent touch pointer IDs
/// without querying missing Submit/Cancel axes, so menus work on desktop and mobile builds.
/// </summary>
public sealed class WildrunePointerInputModule : BaseInputModule
{
    private PointerEventData mouseData;
    private readonly Dictionary<int, PointerEventData> touchPointers = new Dictionary<int, PointerEventData>();
    private readonly List<RaycastResult> raycastResults = new List<RaycastResult>(16);

    public override bool IsModuleSupported() => true;

    public override bool ShouldActivateModule()
    {
        return enabled && gameObject.activeInHierarchy && eventSystem != null;
    }

    public override void ActivateModule()
    {
        base.ActivateModule();
        if (mouseData == null) mouseData = new PointerEventData(eventSystem);
        mouseData.position = Input.mousePosition;
    }

    public override void DeactivateModule()
    {
        if (mouseData != null) HandlePointerExitAndEnter(mouseData, null);
        ClearTouchPointers();
        mouseData = null;
        base.DeactivateModule();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus) ClearTouchPointers();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) ClearTouchPointers();
    }

    private void ClearTouchPointers()
    {
        foreach (KeyValuePair<int, PointerEventData> pair in touchPointers)
        {
            HandlePointerExitAndEnter(pair.Value, null);
            ReleasePointer(pair.Value, null, false);
        }
        touchPointers.Clear();
    }

    public override void Process()
    {
        ProcessTouches();
        // Device Simulator emits touch events; skip mouse processing for those frames to prevent
        // a simulated tap from activating a UI button twice.
        if (Input.touchCount == 0) ProcessMouse();
    }

    private void ProcessTouches()
    {
        for (int i = 0; i < Input.touchCount; i++)
        {
            Touch touch = Input.GetTouch(i);
            int id = touch.fingerId;
            PointerEventData data;
            if (touch.phase == TouchPhase.Began)
            {
                data = new PointerEventData(eventSystem);
                data.pointerId = id;
                data.button = PointerEventData.InputButton.Left;
                data.position = touch.position;
                touchPointers[id] = data;
                GameObject target = UpdateRaycastAndHover(data, touch.position);
                PressPointer(data, target);
            }
            else if (touchPointers.TryGetValue(id, out data))
            {
                GameObject target = UpdateRaycastAndHover(data, touch.position);
                if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                {
                    ProcessDrag(data);
                }
                else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    ReleasePointer(data, target, touch.phase == TouchPhase.Ended);
                    HandlePointerExitAndEnter(data, null);
                    touchPointers.Remove(id);
                }
            }
        }
    }

    private void ProcessMouse()
    {
        if (mouseData == null) mouseData = new PointerEventData(eventSystem);
        GameObject target = UpdateRaycastAndHover(mouseData, Input.mousePosition);
        if (Input.GetMouseButtonDown(0)) PressPointer(mouseData, target);
        if (Input.GetMouseButton(0)) ProcessDrag(mouseData);
        if (Input.GetMouseButtonUp(0)) ReleasePointer(mouseData, target, true);
    }

    private GameObject UpdateRaycastAndHover(PointerEventData data, Vector2 position)
    {
        data.delta = position - data.position;
        data.position = position;
        data.button = PointerEventData.InputButton.Left;
        data.scrollDelta = Vector2.zero;
        raycastResults.Clear();
        eventSystem.RaycastAll(data, raycastResults);
        data.pointerCurrentRaycast = raycastResults.Count > 0 ? raycastResults[0] : new RaycastResult();
        GameObject target = data.pointerCurrentRaycast.gameObject;
        HandlePointerExitAndEnter(data, target);
        return target;
    }

    private void PressPointer(PointerEventData data, GameObject target)
    {
        data.eligibleForClick = true;
        data.delta = Vector2.zero;
        data.dragging = false;
        data.useDragThreshold = true;
        data.pressPosition = data.position;
        data.pointerPressRaycast = data.pointerCurrentRaycast;
        data.clickCount = 1;
        data.clickTime = Time.unscaledTime;

        GameObject pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
        if (pressed == null) pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        data.pointerPress = pressed;
        data.rawPointerPress = target;
        data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
        if (data.pointerDrag != null)
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
    }

    private void ReleasePointer(PointerEventData data, GameObject target, bool allowClick)
    {
        if (data.pointerPress != null)
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);

        GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
        if (allowClick && data.pointerPress != null && data.pointerPress == clickHandler && data.eligibleForClick)
            ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
        else if (data.pointerDrag != null && data.dragging)
            ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.dropHandler);

        data.eligibleForClick = false;
        data.pointerPress = null;
        data.rawPointerPress = null;
        if (data.pointerDrag != null && data.dragging)
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
        data.pointerDrag = null;
        data.dragging = false;
    }

    private void ProcessDrag(PointerEventData data)
    {
        if (data.pointerDrag == null || data.delta.sqrMagnitude < 0.0001f) return;
        float dragThreshold = eventSystem.pixelDragThreshold;
        bool crossedDragThreshold = !data.useDragThreshold ||
            (data.pressPosition - data.position).sqrMagnitude >= dragThreshold * dragThreshold;
        if (!data.dragging && crossedDragThreshold)
        {
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.beginDragHandler);
            data.dragging = true;
        }
        if (data.dragging)
        {
            if (data.pointerPress != data.pointerDrag)
            {
                if (data.pointerPress != null)
                    ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);
                data.eligibleForClick = false;
                data.pointerPress = null;
                data.rawPointerPress = null;
            }
            ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.dragHandler);
        }
    }
}
