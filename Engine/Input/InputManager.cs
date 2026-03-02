using OpenTK.Windowing.GraphicsLibraryFramework;

namespace TaikoNova.Engine.Input;

/// <summary>
/// Tracks keyboard, mouse, and scroll-wheel state per frame.
/// Provides IsDown / IsPressed / IsReleased + scroll delta + mouse position.
/// </summary>
public sealed class InputManager
{
    private readonly HashSet<Keys> _currentKeys = new();
    private readonly HashSet<Keys> _previousKeys = new();

    // ── Mouse position ──
    public float MouseX { get; private set; }
    public float MouseY { get; private set; }
    public bool MouseDown { get; private set; }
    public bool MousePressed { get; private set; }   // just clicked this frame
    public bool MouseReleased { get; private set; }  // just released this frame
    private bool _prevMouseDown;

    // ── Mouse / scroll wheel ──
    /// <summary>Scroll wheel delta this frame (positive = scroll up).</summary>
    public float ScrollDelta { get; private set; }

    // ── Text input buffer (double-buffered) ──
    // Events write to _pendingText; Update() swaps it into _readableText for the game.
    private List<char> _pendingText = new();
    private List<char> _readableText = new();
    /// <summary>Characters typed this frame via TextInput event.</summary>
    public IReadOnlyList<char> TextInput => _readableText;

    /// <summary>Feed a character from the window's TextInput event.</summary>
    public void OnTextInput(char c) => _pendingText.Add(c);

    /// <summary>Feed scroll delta from the window's MouseWheel event.</summary>
    public void OnMouseWheel(float deltaY) => _scrollAccumulator += deltaY;
    private float _scrollAccumulator;

    /// <summary>Call once per frame with the current keyboard state.</summary>
    public void Update(KeyboardState keyboard, OpenTK.Windowing.GraphicsLibraryFramework.MouseState? mouse = null,
        int vpX = 0, int vpY = 0, int vpW = 0, int vpH = 0, int virtW = 0, int virtH = 0,
        float scaleX = 1f, float scaleY = 1f)
    {
        _previousKeys.Clear();
        foreach (var key in _currentKeys)
            _previousKeys.Add(key);

        _currentKeys.Clear();

        // Check all keys we care about
        foreach (Keys key in Enum.GetValues<Keys>())
        {
            if (key == Keys.Unknown) continue;
            try
            {
                if (keyboard.IsKeyDown(key))
                    _currentKeys.Add(key);
            }
            catch { /* Some keys may throw */ }
        }

        // Mouse state — remap from window coords to virtual coords
        if (mouse != null)
        {
            if (vpW > 0 && vpH > 0 && virtW > 0 && virtH > 0)
            {
                // Mouse coords are in client/logical space; convert to framebuffer space first
                float fbMouseX = mouse.X * scaleX;
                float fbMouseY = mouse.Y * scaleY;
                MouseX = (fbMouseX - vpX) / vpW * virtW;
                MouseY = (fbMouseY - vpY) / vpH * virtH;
            }
            else
            {
                MouseX = mouse.X;
                MouseY = mouse.Y;
            }
            bool btn = mouse.IsButtonDown(OpenTK.Windowing.GraphicsLibraryFramework.MouseButton.Left);
            MousePressed = btn && !_prevMouseDown;
            MouseReleased = !btn && _prevMouseDown;
            MouseDown = btn;
            _prevMouseDown = btn;
        }

        // Consume accumulated scroll
        ScrollDelta = _scrollAccumulator;
        _scrollAccumulator = 0;

        // Swap text buffers: pending chars become readable, old readable is recycled
        _readableText.Clear();
        (_readableText, _pendingText) = (_pendingText, _readableText);
    }

    /// <summary>True while the key is held down.</summary>
    public bool IsDown(Keys key) => _currentKeys.Contains(key);

    /// <summary>True on the frame the key was first pressed.</summary>
    public bool IsPressed(Keys key) => _currentKeys.Contains(key) && !_previousKeys.Contains(key);

    /// <summary>True on the frame the key was released.</summary>
    public bool IsReleased(Keys key) => !_currentKeys.Contains(key) && _previousKeys.Contains(key);

    /// <summary>True if any of the given keys were just pressed.</summary>
    public bool AnyPressed(params Keys[] keys)
    {
        foreach (var k in keys)
            if (IsPressed(k)) return true;
        return false;
    }

    /// <summary>True if any of the given keys are held.</summary>
    public bool AnyDown(params Keys[] keys)
    {
        foreach (var k in keys)
            if (IsDown(k)) return true;
        return false;
    }
}
