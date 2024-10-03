using UnityEngine;

namespace BetaHub
{
    public class RectTransformFilterHandler : MonoBehaviour
    {
        [Tooltip("Reference to the GameRecorder object. If not set, it will try to find one in the scene.")]
        public GameRecorder gameRecorder;

        [Tooltip("Unique key to identify this filter in the GameRecorder.")]
        public string keyFilter = "box";

        [Tooltip("Color of the rectangle drawn on screen.")]
        public Color boxColor = Color.red;

        // Private field to cache RectTransform
        private RectTransform _rectTransform;

        // Initialize the component
        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();

            // If RectTransform is missing, print an error and disable this component
            if (_rectTransform == null)
            {
                Debug.LogError($"{nameof(RectTransform)} component is missing on {gameObject.name}. Disabling {nameof(RectTransformFilterHandler)}.");
                enabled = false; // Disable this script to avoid further errors
                return;
            }

            // Check if gameRecorder is not assigned
            if (gameRecorder == null)
            {
                // Print a warning and try to find the GameRecorder in the scene
                Debug.LogWarning("gameRecorder is not set! Trying to find GameRecorder by lookup.");
                gameRecorder = FindObjectOfType<GameRecorder>();

                // If still null, print an error to avoid null reference exceptions later
                if (gameRecorder == null)
                {
                    Debug.LogError("GameRecorder could not be found! Make sure GameRecorder is in the scene and assigned.");
                }
            }
        }

        // Activate the filter when the component is enabled
        private void OnEnable()
        {
            // Ensure we have a valid gameRecorder before proceeding
            if (!ValidateGameRecorder()) return;

            // Get screen coordinates of the RectTransform
            Rect rect = GetScreenCoordinates(_rectTransform);

            // Add the filter to the GameRecorder's TextureFilterManager
            gameRecorder.TextureFilterManager.AddFilter(
                keyFilter,
                new DrawBox(
                    Vector2Int.RoundToInt(rect.position),
                    Vector2Int.RoundToInt(rect.size),
                    boxColor
                )
            );
        }

        // Remove the filter when the component is disabled
        private void OnDisable()
        {
            // Ensure we have a valid gameRecorder before proceeding
            if (!ValidateGameRecorder()) return;

            // Remove the filter when the component is disabled
            gameRecorder.TextureFilterManager.RemoveFilter(keyFilter);
        }

        // Validate if the gameRecorder is properly assigned
        private bool ValidateGameRecorder()
        {
            if (gameRecorder == null)
            {
                Debug.LogError($"{nameof(gameRecorder)} is null. Cannot proceed with filter handling.");
                return false;
            }
            return true;
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
}