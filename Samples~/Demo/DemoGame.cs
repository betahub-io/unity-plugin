using BetaHub;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// A tiny game to try the bug reporter on: collect the coins. One coin is broken on purpose. Report it
/// with F12, or Start on a controller: the report carries a screenshot, the last 60 seconds of play
/// with sound, and the game log, which names the coin.
/// The game builds its sprites and sounds in code, so the scene needs no assets and works in the
/// Built-in pipeline and in URP.
/// </summary>
public class DemoGame : MonoBehaviour
{
    private const float PlayerSpeed = 6f;
    private const float PlayerSize = 0.8f;
    private const float CoinSize = 0.6f;
    private const float PickupDistance = 0.7f;
    private const float ArenaHalfWidth = 7.5f;
    private const float ArenaHalfHeight = 4f;
    private const float WallThickness = 0.15f;
    // room beside the walls when the camera widens for a narrow screen
    private const float ViewMargin = 0.6f;
    private const float RoundPauseSeconds = 1.5f;
    private const int CircleTextureSize = 64;
    private const int HudFontDivisor = 28;
    private const float HudMargin = 24f;
    private const int FallbackSampleRate = 44100;
    private const float ToneVolume = 0.2f;
    private const float PickupFrequency = 880f;
    private const float RefusedFrequency = 140f;
    private const float ToneSeconds = 0.12f;
    // The bug to report: this coin was never added to the round's list, so it can't be picked up.
    private const int BrokenCoin = 3;

    private static readonly Vector2[] CoinSpots =
    {
        new Vector2(-5.5f, 2.5f), new Vector2(-2f, -2.5f), new Vector2(1.5f, 2f), new Vector2(5.5f, -1.5f), new Vector2(0f, 0f),
    };
    private static readonly Vector2 PlayerStart = new Vector2(0f, -3f);
    private static readonly Color PlayerColor = new Color32(255, 122, 69, 255);
    private static readonly Color CoinColor = new Color32(244, 197, 66, 255);
    private static readonly Color WallColor = new Color32(70, 78, 104, 255);

    [Tooltip("The bug report widget in this scene. The game waits while it is open.")]
    [SerializeField] private BugReportWidget widget;

    private Transform _player;
    private SpriteRenderer[] _coins;
    private bool[] _taken;
    private bool _touchingBroken;
    private int _score;
    private int _round = 1;
    private float _nextRoundAt = -1f;
    private Camera _camera;
    private float _cameraSize;
    private Texture2D _squareTexture;
    private Texture2D _circleTexture;
    private Sprite _square;
    private Sprite _circle;
    private AudioSource _audio;
    private AudioClip _pickupSound;
    private AudioClip _refusedSound;
    private GUIStyle _hudStyle;
    private GUIStyle _hintStyle;

    void Start()
    {
        if (widget == null)
        {
            Debug.LogError("DemoGame: assign the scene's BugReportWidget to the Widget field.");
            enabled = false;
            return;
        }

        _squareTexture = SolidTexture();
        _circleTexture = CircleTexture(CircleTextureSize);
        _square = Sprite.Create(_squareTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        _circle = Sprite.Create(_circleTexture, new Rect(0, 0, CircleTextureSize, CircleTextureSize), new Vector2(0.5f, 0.5f), CircleTextureSize);

        BuildWalls(_square);
        _coins = new SpriteRenderer[CoinSpots.Length];
        _taken = new bool[CoinSpots.Length];
        for (int i = 0; i < CoinSpots.Length; i++)
        {
            _coins[i] = NewSprite("Coin " + (i + 1), _circle, CoinColor, CoinSpots[i], Vector2.one * CoinSize, 1);
        }
        _player = NewSprite("Player", _square, PlayerColor, PlayerStart, Vector2.one * PlayerSize, 2).transform;

        _camera = Camera.main;
        if (_camera != null) _cameraSize = _camera.orthographicSize;

        _audio = gameObject.AddComponent<AudioSource>();
        _pickupSound = Tone("Pickup", PickupFrequency);
        _refusedSound = Tone("Refused", RefusedFrequency);

        Debug.Log("Demo: collect the coins. One of them is broken on purpose; report it with F12 or Start.");
    }

    void Update()
    {
        FitCamera();
        // the form is open: the game waits
        if (widget.IsVisible) return;

#if ENABLE_INPUT_SYSTEM
        // The widget opens with F12 on its own; a controller button is the game's choice.
        var pad = Gamepad.current;
        if (pad != null && pad.startButton.wasPressedThisFrame)
        {
            widget.Show();
            return;
        }
#endif

        Vector2 move = ReadMove() * (PlayerSpeed * Time.deltaTime);
        Vector2 position = (Vector2)_player.position + move;
        float halfSize = PlayerSize / 2f;
        position.x = Mathf.Clamp(position.x, -ArenaHalfWidth + halfSize, ArenaHalfWidth - halfSize);
        position.y = Mathf.Clamp(position.y, -ArenaHalfHeight + halfSize, ArenaHalfHeight - halfSize);
        _player.position = position;

        CheckCoins(position);
        if (_nextRoundAt > 0f && Time.time >= _nextRoundAt) NextRound();
    }

    void OnGUI()
    {
        // IMGUI draws over the form
        if (widget == null || widget.IsVisible) return;
        if (_hudStyle == null)
        {
            int size = Mathf.Max(12, Screen.height / HudFontDivisor);
            _hudStyle = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold };
            _hintStyle = new GUIStyle(GUI.skin.label) { fontSize = size * 3 / 4, alignment = TextAnchor.LowerCenter, wordWrap = true };
        }
        GUI.Label(new Rect(HudMargin, HudMargin, Screen.width, Screen.height), $"Coins {_score}   Round {_round}", _hudStyle);
        GUI.Label(new Rect(HudMargin, 0f, Screen.width - 2f * HudMargin, Screen.height - HudMargin),
            "Move with WASD, the arrows or the left stick. One coin is broken: press F12 or Start to report it.", _hintStyle);
    }

    void OnDestroy()
    {
        if (_square != null) Destroy(_square);
        if (_circle != null) Destroy(_circle);
        if (_squareTexture != null) Destroy(_squareTexture);
        if (_circleTexture != null) Destroy(_circleTexture);
    }

    // The arena is wider than tall: on a 4:3 or 5:4 screen the camera backs off to keep the walls in view.
    private void FitCamera()
    {
        if (_camera == null || !_camera.orthographic) return;
        _camera.orthographicSize = Mathf.Max(_cameraSize, (ArenaHalfWidth + ViewMargin) / _camera.aspect);
    }

    private void CheckCoins(Vector2 player)
    {
        for (int i = 0; i < _coins.Length; i++)
        {
            if (_taken[i]) continue;
            bool touching = Vector2.Distance(player, _coins[i].transform.position) < PickupDistance;
            if (i == BrokenCoin)
            {
                if (touching && !_touchingBroken)
                {
                    Debug.LogWarning($"Demo: coin {i + 1} ignored the pickup: it is missing from the round's coin list.");
                    _audio.PlayOneShot(_refusedSound);
                }
                _touchingBroken = touching;
                continue;
            }
            if (!touching) continue;

            _taken[i] = true;
            _coins[i].enabled = false;
            _score++;
            _audio.PlayOneShot(_pickupSound);
            Debug.Log($"Demo: coin {i + 1} picked up, {_score} so far.");
        }

        if (_nextRoundAt < 0f && AllWorkingCoinsTaken()) _nextRoundAt = Time.time + RoundPauseSeconds;
    }

    private bool AllWorkingCoinsTaken()
    {
        for (int i = 0; i < _taken.Length; i++)
        {
            if (i != BrokenCoin && !_taken[i]) return false;
        }
        return true;
    }

    private void NextRound()
    {
        _nextRoundAt = -1f;
        _round++;
        for (int i = 0; i < _coins.Length; i++)
        {
            _taken[i] = false;
            _coins[i].enabled = true;
        }
        Debug.Log($"Demo: round {_round}, the coins are back.");
    }

    private static Vector2 ReadMove()
    {
        Vector2 move = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1f;
        }
        var pad = Gamepad.current;
        if (pad != null) move += pad.leftStick.ReadValue() + pad.dpad.ReadValue();
#elif ENABLE_LEGACY_INPUT_MANAGER
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
#endif
        return Vector2.ClampMagnitude(move, 1f);
    }

    private void BuildWalls(Sprite square)
    {
        float width = 2f * ArenaHalfWidth + WallThickness;
        float height = 2f * ArenaHalfHeight + WallThickness;
        NewSprite("Wall Top", square, WallColor, new Vector2(0f, ArenaHalfHeight), new Vector2(width, WallThickness), 0);
        NewSprite("Wall Bottom", square, WallColor, new Vector2(0f, -ArenaHalfHeight), new Vector2(width, WallThickness), 0);
        NewSprite("Wall Left", square, WallColor, new Vector2(-ArenaHalfWidth, 0f), new Vector2(WallThickness, height), 0);
        NewSprite("Wall Right", square, WallColor, new Vector2(ArenaHalfWidth, 0f), new Vector2(WallThickness, height), 0);
    }

    private SpriteRenderer NewSprite(string name, Sprite sprite, Color color, Vector2 position, Vector2 size, int order)
    {
        var child = new GameObject(name);
        child.transform.SetParent(transform, false);
        child.transform.localPosition = position;
        child.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = child.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        return renderer;
    }

    private static Texture2D SolidTexture()
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        return texture;
    }

    private static Texture2D CircleTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size / 2f;
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(radius, radius));
                // one pixel of soft edge
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - distance));
            }
        }
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private static AudioClip Tone(string name, float frequency)
    {
        int rate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : FallbackSampleRate;
        int count = Mathf.CeilToInt(rate * ToneSeconds);
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            float fade = 1f - (float)i / count;
            samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * i / rate) * fade * ToneVolume;
        }
        var clip = AudioClip.Create(name, count, 1, rate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
