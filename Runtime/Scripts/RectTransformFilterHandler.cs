using UnityEngine;
using BetaHub;

public class RectTransformFilterHandler : MonoBehaviour
{
    public GameRecorder gameRecorder;
    public string keyFilter = "box"; // Filter key
    public Color boxColor = Color.red; // Color of the box

    private RectTransform _rectTransform;

    // Initialize the component
    private void Awake()
    {
        _rectTransform = GetComponent<RectTransform>();
    }

    // Activate the filter when the component is enabled
    private void OnEnable()
    {
        Rect rect = GetScreenCoordinates(_rectTransform);
        gameRecorder.TextureFilterManager.AddFilter(keyFilter, new DrawBox(Vector2Int.RoundToInt(rect.position), Vector2Int.RoundToInt(rect.size), boxColor));
    }

    // Remove the filter when the component is disabled
    private void OnDisable()
    {
        gameRecorder.TextureFilterManager.RemoveFilter(keyFilter);
    }

    // Calculate the screen coordinates of the UI component
    public Rect GetScreenCoordinates(RectTransform uiElement)
    {
        var worldCorners = new Vector3[4];
        uiElement.GetWorldCorners(worldCorners);
        var result = new Rect(
                      worldCorners[0].x,
                      worldCorners[0].y,
                      worldCorners[2].x - worldCorners[0].x,
                      worldCorners[2].y - worldCorners[0].y);
        return result;
    }
}