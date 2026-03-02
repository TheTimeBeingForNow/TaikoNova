using OpenTK.Windowing.GraphicsLibraryFramework;
using TaikoNova.Engine;
using TaikoNova.Engine.GL;
using TaikoNova.Engine.Text;
using TaikoNova.Game.Settings;
using TaikoNova.Game.Skin;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Full-featured settings overlay drawn on top of any screen.
/// Categories, search bar, sliders, toggles, all keyboard+mouse driven.
/// Changes take effect immediately and are saved on close.
/// </summary>
public sealed class SettingsOverlay
{
    private readonly GameEngine _engine;
    private readonly SettingsManager _settings;
    private readonly Action _applySettings;

    public bool IsOpen { get; private set; }

    // ── Animation ──
    private float _openAnim;      // 0→1 slide-in
    private float _dimAnim;       // 0→1 background dim
    private double _time;

    // ── Categories & items ──
    private enum Category { Audio, Gameplay, Display, Input }
    private static readonly Category[] AllCategories =
        (Category[])Enum.GetValues(typeof(Category));

    private Category _category = Category.Audio;
    private int _selectedItem;
    private float _scrollOffset;
    private float _targetScroll;

    // ── Search ──
    private string _searchQuery = "";
    private bool _searchActive;
    private float _searchCursorBlink;

    // ── Slider dragging ──
    private bool _dragging;
    private int _dragItemIndex = -1;

    // ── Panel layout ──
    private const float PanelW = 560f;
    private const float HeaderH = 56f;
    private const float CatBarH = 36f;
    private const float SearchBarH = 36f;
    private const float ItemH = 44f;
    private const float ItemGap = 2f;
    private const float SliderW = 240f;
    private const float SliderH = 6f;
    private const float ToggleW = 40f;
    private const float ToggleH = 20f;
    private const float PanelPadX = 24f;
    private const float BottomPad = 20f;

    // ── Setting item definitions ──
    private readonly record struct SettingDef(
        string Name, Category Cat, SettingType Type,
        float Min, float Max, float Step,
        Func<SettingsManager, float> GetFloat,
        Action<SettingsManager, float>? SetFloat,
        Func<SettingsManager, bool>? GetBool,
        Action<SettingsManager, bool>? SetBool,
        Func<SettingsManager, string>? GetString,
        string? Suffix,
        string[]? Options);

    private enum SettingType { Slider, Toggle, Label }

    private readonly SettingDef[] _allDefs;
    private List<int> _filteredDefs = new(); // indices into _allDefs

    // Accent colours per category
    private static readonly float[][] CatColors =
    {
        new[] { 0.30f, 0.55f, 0.95f }, // Audio  - blue
        new[] { 0.90f, 0.72f, 0.20f }, // Gameplay - gold
        new[] { 0.45f, 0.80f, 0.50f }, // Display - green
        new[] { 0.75f, 0.45f, 0.90f }, // Input  - purple
    };

    public SettingsOverlay(GameEngine engine, SettingsManager settings, Action applySettings)
    {
        _engine = engine;
        _settings = settings;
        _applySettings = applySettings;

        _allDefs = new SettingDef[]
        {
            // ── Audio ──
            Slider("Master Volume", Category.Audio, 0, 100, 1,
                s => s.MasterVolume * 100, (s, v) => s.MasterVolume = v / 100f, "%"),
            Slider("Music Volume", Category.Audio, 0, 100, 1,
                s => s.MusicVolume * 100, (s, v) => s.MusicVolume = v / 100f, "%"),
            Slider("SFX Volume", Category.Audio, 0, 100, 1,
                s => s.SfxVolume * 100, (s, v) => s.SfxVolume = v / 100f, "%"),

            // ── Gameplay ──
            Slider("Scroll Speed", Category.Gameplay, 50, 200, 5,
                s => s.ScrollSpeed * 100, (s, v) => s.ScrollSpeed = v / 100f, "%"),
            Slider("Background Dim", Category.Gameplay, 0, 100, 5,
                s => s.BackgroundDim * 100, (s, v) => s.BackgroundDim = v / 100f, "%"),
            Slider("Global Offset", Category.Gameplay, -200, 200, 1,
                s => s.GlobalOffset, (s, v) => s.GlobalOffset = (int)v, "ms"),
            Toggle("Hit Error Bar", Category.Gameplay,
                s => s.ShowHitError, (s, v) => s.ShowHitError = v),

            // ── Display ──
            Toggle("Fullscreen", Category.Display,
                s => s.Fullscreen, (s, v) => s.Fullscreen = v),
            Toggle("Show FPS", Category.Display,
                s => s.ShowFps, (s, v) => s.ShowFps = v),
            Toggle("VSync", Category.Display,
                s => s.VSync, (s, v) => s.VSync = v),

            // ── Input ──
            Label("Don Keys", Category.Input, s => s.DonKeys),
            Label("Kat Keys", Category.Input, s => s.KatKeys),
        };

        RebuildFilter();
    }

    // ── Factory helpers ──
    private static SettingDef Slider(string name, Category cat, float min, float max, float step,
        Func<SettingsManager, float> get, Action<SettingsManager, float> set, string suffix) =>
        new(name, cat, SettingType.Slider, min, max, step, get, set, null, null, null, suffix, null);

    private static SettingDef Toggle(string name, Category cat,
        Func<SettingsManager, bool> get, Action<SettingsManager, bool> set) =>
        new(name, cat, SettingType.Toggle, 0, 1, 1, _ => 0, null, get, set, null, null, null);

    private static SettingDef Label(string name, Category cat,
        Func<SettingsManager, string> get) =>
        new(name, cat, SettingType.Label, 0, 0, 0, _ => 0, null, null, null, get, null, null);

    // ═══════════════════════════════════════════════════════════════════
    //  Open / Close
    // ═══════════════════════════════════════════════════════════════════

    public void Open()
    {
        IsOpen = true;
        _openAnim = 0;
        _dimAnim = 0;
        _time = 0;
        _selectedItem = 0;
        _scrollOffset = 0;
        _targetScroll = 0;
        _searchQuery = "";
        _searchActive = false;
        _dragging = false;
        RebuildFilter();
    }

    public void Close()
    {
        IsOpen = false;
        _settings.Save();
        _applySettings();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Update
    // ═══════════════════════════════════════════════════════════════════

    public void Update(double dt)
    {
        if (!IsOpen) return;

        float fdt = (float)dt;
        _time += dt;
        _openAnim = MathF.Min(1f, _openAnim + fdt * 5f);
        _dimAnim = MathF.Min(1f, _dimAnim + fdt * 4f);
        _searchCursorBlink += fdt * 3f;

        // Smooth scroll
        _scrollOffset += (_targetScroll - _scrollOffset) * MathF.Min(1f, fdt * 12f);

        var input = _engine.Input;

        // ── Search bar input ──
        if (_searchActive)
        {
            foreach (char c in input.TextInput)
            {
                if (c == '\b' || c == 127)
                {
                    if (_searchQuery.Length > 0)
                        _searchQuery = _searchQuery[..^1];
                }
                else if (c >= ' ')
                {
                    _searchQuery += c;
                }
            }
            RebuildFilter();

            if (input.IsPressed(Keys.Escape))
            {
                if (_searchQuery.Length > 0)
                    _searchQuery = "";
                else
                    _searchActive = false;
                RebuildFilter();
                return;
            }
            if (input.IsPressed(Keys.Enter))
            {
                _searchActive = false;
                return;
            }
            // Allow nav even while searching
        }
        else
        {
            // ESC closes settings
            if (input.IsPressed(Keys.Escape))
            {
                Close();
                return;
            }

            // Tab/Ctrl to search
            if (input.IsPressed(Keys.Tab))
            {
                _searchActive = true;
                _searchCursorBlink = 0;
                return;
            }
        }

        // ── Category switching (Q/E or 1-4) ──
        if (input.IsPressed(Keys.Q) && !_searchActive)
        {
            int idx = Array.IndexOf(AllCategories, _category);
            _category = AllCategories[(idx - 1 + AllCategories.Length) % AllCategories.Length];
            _selectedItem = 0;
            _targetScroll = 0;
            RebuildFilter();
        }
        if (input.IsPressed(Keys.E) && !_searchActive)
        {
            int idx = Array.IndexOf(AllCategories, _category);
            _category = AllCategories[(idx + 1) % AllCategories.Length];
            _selectedItem = 0;
            _targetScroll = 0;
            RebuildFilter();
        }
        for (int i = 0; i < AllCategories.Length && i < 4; i++)
        {
            if (input.IsPressed(Keys.D1 + i) && !_searchActive)
            {
                _category = AllCategories[i];
                _selectedItem = 0;
                _targetScroll = 0;
                RebuildFilter();
            }
        }

        // ── Navigation ──
        if (input.IsPressed(Keys.Up))
        {
            _selectedItem = Math.Max(0, _selectedItem - 1);
            EnsureVisible();
        }
        if (input.IsPressed(Keys.Down))
        {
            _selectedItem = Math.Min(_filteredDefs.Count - 1, _selectedItem + 1);
            EnsureVisible();
        }

        // ── Value adjustment (Left/Right or mouse drag) ──
        if (_filteredDefs.Count > 0 && _selectedItem < _filteredDefs.Count)
        {
            var def = _allDefs[_filteredDefs[_selectedItem]];
            HandleValueChange(def, input, fdt);
        }

        // ── Mouse interaction ──
        HandleMouse(input);

        // ── Scroll wheel ──
        if (MathF.Abs(input.ScrollDelta) > 0.01f)
        {
            _targetScroll -= input.ScrollDelta * 40f;
            _targetScroll = MathF.Max(0f, _targetScroll);
        }
    }

    private void HandleValueChange(SettingDef def, Engine.Input.InputManager input, float fdt)
    {
        if (def.Type == SettingType.Slider && def.SetFloat != null)
        {
            float val = def.GetFloat(_settings);
            float delta = def.Step;

            // Hold shift for faster adjustment
            if (input.IsDown(Keys.LeftShift) || input.IsDown(Keys.RightShift))
                delta *= 5;

            if (input.IsPressed(Keys.Left))
            {
                val = MathF.Max(def.Min, val - delta);
                def.SetFloat(_settings, val);
                _applySettings();
            }
            if (input.IsPressed(Keys.Right))
            {
                val = MathF.Min(def.Max, val + delta);
                def.SetFloat(_settings, val);
                _applySettings();
            }
        }
        else if (def.Type == SettingType.Toggle && def.SetBool != null && def.GetBool != null)
        {
            if (input.IsPressed(Keys.Enter) || input.IsPressed(Keys.Space)
                || input.IsPressed(Keys.Left) || input.IsPressed(Keys.Right))
            {
                def.SetBool(_settings, !def.GetBool(_settings));
                _applySettings();
            }
        }
    }

    private void HandleMouse(Engine.Input.InputManager input)
    {
        float panelX = (_engine.ScreenWidth - PanelW) / 2f;
        float panelY = (_engine.ScreenHeight - GetPanelH()) / 2f;
        float contentY = panelY + HeaderH + CatBarH + SearchBarH;
        float mx = input.MouseX;
        float my = input.MouseY;

        // Check category clicks
        if (input.MousePressed)
        {
            float catY = panelY + HeaderH;
            if (my >= catY && my < catY + CatBarH && mx >= panelX && mx < panelX + PanelW)
            {
                float catW = PanelW / AllCategories.Length;
                int catIdx = (int)((mx - panelX) / catW);
                if (catIdx >= 0 && catIdx < AllCategories.Length)
                {
                    _category = AllCategories[catIdx];
                    _selectedItem = 0;
                    _targetScroll = 0;
                    RebuildFilter();
                }
            }

            // Search bar click
            float searchY = panelY + HeaderH + CatBarH;
            if (my >= searchY && my < searchY + SearchBarH && mx >= panelX && mx < panelX + PanelW)
            {
                _searchActive = true;
                _searchCursorBlink = 0;
            }
        }

        // Item clicks and slider drags
        if (_filteredDefs.Count > 0)
        {
            for (int i = 0; i < _filteredDefs.Count; i++)
            {
                float itemY = contentY + i * (ItemH + ItemGap) - _scrollOffset;
                if (itemY + ItemH < contentY || itemY > panelY + GetPanelH() - BottomPad)
                    continue;

                var def = _allDefs[_filteredDefs[i]];
                float sliderX = panelX + PanelW - PanelPadX - SliderW;
                float sliderRight = sliderX + SliderW;

                if (input.MousePressed && my >= itemY && my < itemY + ItemH && mx >= panelX && mx < panelX + PanelW)
                {
                    _selectedItem = i;

                    if (def.Type == SettingType.Toggle && def.SetBool != null && def.GetBool != null)
                    {
                        def.SetBool(_settings, !def.GetBool(_settings));
                        _applySettings();
                    }
                    else if (def.Type == SettingType.Slider && mx >= sliderX && mx <= sliderRight)
                    {
                        _dragging = true;
                        _dragItemIndex = i;
                    }
                }
            }
        }

        // Slider drag update
        if (_dragging && input.MouseDown && _dragItemIndex >= 0 && _dragItemIndex < _filteredDefs.Count)
        {
            var def = _allDefs[_filteredDefs[_dragItemIndex]];
            if (def.Type == SettingType.Slider && def.SetFloat != null)
            {
                float sliderX = panelX + PanelW - PanelPadX - SliderW;
                float t = Math.Clamp((mx - sliderX) / SliderW, 0f, 1f);
                float val = def.Min + t * (def.Max - def.Min);
                // Snap to step
                val = MathF.Round(val / def.Step) * def.Step;
                val = Math.Clamp(val, def.Min, def.Max);
                def.SetFloat(_settings, val);
                _applySettings();
            }
        }

        if (input.MouseReleased)
        {
            _dragging = false;
            _dragItemIndex = -1;
        }
    }

    private void EnsureVisible()
    {
        float itemTop = _selectedItem * (ItemH + ItemGap);
        float visH = GetVisibleH();
        if (itemTop < _targetScroll)
            _targetScroll = itemTop;
        else if (itemTop + ItemH > _targetScroll + visH)
            _targetScroll = itemTop + ItemH - visH;
        _targetScroll = MathF.Max(0, _targetScroll);
    }

    private float GetPanelH()
    {
        int itemCount = Math.Max(_filteredDefs.Count, 3);
        float content = itemCount * (ItemH + ItemGap) + BottomPad;
        float maxH = _engine.ScreenHeight * 0.85f;
        return MathF.Min(HeaderH + CatBarH + SearchBarH + content, maxH);
    }

    private float GetVisibleH() =>
        GetPanelH() - HeaderH - CatBarH - SearchBarH - BottomPad;

    private void RebuildFilter()
    {
        _filteredDefs.Clear();
        string q = _searchQuery.ToLowerInvariant().Trim();

        for (int i = 0; i < _allDefs.Length; i++)
        {
            var d = _allDefs[i];

            // Must match category (unless searching)
            if (string.IsNullOrEmpty(q) && d.Cat != _category) continue;

            // Search filter
            if (!string.IsNullOrEmpty(q) && !d.Name.ToLowerInvariant().Contains(q))
                continue;

            _filteredDefs.Add(i);
        }

        _selectedItem = Math.Clamp(_selectedItem, 0, Math.Max(0, _filteredDefs.Count - 1));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Render
    // ═══════════════════════════════════════════════════════════════════

    public void Render(double dt)
    {
        if (!IsOpen && _openAnim <= 0) return;

        var batch = _engine.SpriteBatch;
        var font = _engine.Font;
        var px = _engine.PixelTex;
        var proj = _engine.Projection;
        int sw = _engine.ScreenWidth;
        int sh = _engine.ScreenHeight;

        float slide = EaseOutCubic(_openAnim);
        float dimA = _dimAnim * 0.6f;

        float panelH = GetPanelH();
        float panelX = (sw - PanelW) / 2f;
        float panelY = (sh - panelH) / 2f;

        // Slide from top
        float slideOff = (1f - slide) * -80f;
        panelY += slideOff;

        batch.Begin(proj);

        // ── Dim background ──
        batch.Draw(px, 0, 0, sw, sh, 0f, 0f, 0f, dimA);

        // ── Panel background ──
        batch.Draw(px, panelX - 1, panelY - 1, PanelW + 2, panelH + 2,
            0.20f, 0.20f, 0.25f, slide * 0.4f);
        batch.Draw(px, panelX, panelY, PanelW, panelH,
            0.09f, 0.09f, 0.13f, slide * 0.97f);

        // ── Header ──
        float[] catColor = CatColors[(int)_category];
        batch.Draw(px, panelX, panelY, PanelW, HeaderH,
            0.11f, 0.11f, 0.15f, slide);

        // Accent dot
        batch.Draw(_engine.CircleTex, panelX + 16, panelY + 20, 12, 12,
            catColor[0], catColor[1], catColor[2], slide);

        font.DrawTextShadow(batch, "Settings", panelX + 36, panelY + 16, 1.2f,
            1f, 1f, 1f, slide, 2f);

        // Close hint (top right)
        font.DrawTextRight(batch, "ESC to close", panelX + PanelW - 16, panelY + 20, 0.5f,
            0.45f, 0.45f, 0.50f, slide * 0.7f);

        // ── Category tabs ──
        float catY = panelY + HeaderH;
        batch.Draw(px, panelX, catY, PanelW, CatBarH,
            0.07f, 0.07f, 0.10f, slide);

        float catTabW = PanelW / AllCategories.Length;
        for (int i = 0; i < AllCategories.Length; i++)
        {
            bool sel = AllCategories[i] == _category && string.IsNullOrEmpty(_searchQuery);
            float tabX = panelX + i * catTabW;
            float[] cc = CatColors[i];

            if (sel)
            {
                // Active tab underline
                batch.Draw(px, tabX, catY + CatBarH - 3, catTabW, 3,
                    cc[0], cc[1], cc[2], slide);
                // Slight bg tint
                batch.Draw(px, tabX, catY, catTabW, CatBarH,
                    cc[0] * 0.06f, cc[1] * 0.06f, cc[2] * 0.06f, slide * 0.5f);
            }

            string catName = AllCategories[i].ToString();
            float tw = font.MeasureWidth(catName, 0.6f);
            float tx = tabX + (catTabW - tw) / 2f;
            float tr = sel ? (0.6f + cc[0] * 0.4f) : 0.45f;
            float tg = sel ? (0.6f + cc[1] * 0.4f) : 0.45f;
            float tb = sel ? (0.6f + cc[2] * 0.4f) : 0.50f;
            font.DrawText(batch, catName, tx, catY + 9, 0.6f, tr, tg, tb, slide);

            // Number hint
            font.DrawText(batch, $"{i + 1}", tabX + 6, catY + 10, 0.4f,
                0.30f, 0.30f, 0.35f, slide * 0.5f);
        }

        // ── Search bar ──
        float searchY = catY + CatBarH;
        batch.Draw(px, panelX, searchY, PanelW, SearchBarH,
            0.06f, 0.06f, 0.09f, slide);

        // Search box
        float sbX = panelX + PanelPadX;
        float sbW = PanelW - PanelPadX * 2;
        float sbY = searchY + 6;
        float sbH = SearchBarH - 12;
        batch.Draw(px, sbX, sbY, sbW, sbH,
            _searchActive ? 0.14f : 0.10f, _searchActive ? 0.14f : 0.10f,
            _searchActive ? 0.18f : 0.13f, slide * 0.9f);

        if (_searchActive)
        {
            // Active border
            batch.Draw(px, sbX, sbY + sbH - 1, sbW, 1,
                catColor[0] * 0.6f, catColor[1] * 0.6f, catColor[2] * 0.6f, slide);
        }

        string searchDisplay = _searchQuery.Length > 0 ? _searchQuery : "Search settings... (TAB)";
        float searchA = _searchQuery.Length > 0 ? 0.9f : 0.35f;
        font.DrawText(batch, searchDisplay, sbX + 8, sbY + 4, 0.55f,
            0.6f, 0.6f, 0.65f, slide * searchA);

        // Cursor
        if (_searchActive && ((int)(_searchCursorBlink) % 2 == 0))
        {
            float curX = sbX + 8 + font.MeasureWidth(_searchQuery, 0.55f);
            batch.Draw(px, curX, sbY + 3, 1, sbH - 6,
                0.8f, 0.8f, 0.85f, slide * 0.8f);
        }

        // ── Settings items ──
        float contentY = searchY + SearchBarH;
        float visH = GetVisibleH();

        // Clip region (via scissor-like approach: just skip out-of-bounds items)
        for (int i = 0; i < _filteredDefs.Count; i++)
        {
            float itemY = contentY + i * (ItemH + ItemGap) - _scrollOffset;

            // Skip items outside visible area
            if (itemY + ItemH < contentY || itemY > panelY + panelH - BottomPad)
                continue;

            var def = _allDefs[_filteredDefs[i]];
            bool sel = i == _selectedItem;
            float[] cc = CatColors[(int)def.Cat];

            // Item background
            float bgB = sel ? 0.13f : 0.08f;
            float bgA = sel ? 0.9f : 0.5f;
            batch.Draw(px, panelX + 4, itemY, PanelW - 8, ItemH,
                bgB, bgB, bgB + 0.02f, bgA * slide);

            // Selected accent bar
            if (sel)
            {
                batch.Draw(px, panelX + 4, itemY, 3, ItemH,
                    cc[0], cc[1], cc[2], slide * 0.9f);

                // Subtle glow
                batch.Draw(px, panelX + 4, itemY, PanelW - 8, ItemH,
                    cc[0] * 0.04f, cc[1] * 0.04f, cc[2] * 0.04f, slide * 0.5f);
            }

            // Label
            float lr = sel ? 0.95f : 0.65f;
            float lg = sel ? 0.95f : 0.65f;
            float lb = sel ? 1f : 0.70f;
            font.DrawText(batch, def.Name, panelX + PanelPadX + 8, itemY + 13, 0.7f,
                lr, lg, lb, slide);

            // Value control
            float rightX = panelX + PanelW - PanelPadX;

            switch (def.Type)
            {
                case SettingType.Slider:
                    RenderSlider(batch, font, px, def, rightX, itemY, cc, sel, slide);
                    break;
                case SettingType.Toggle:
                    RenderToggle(batch, px, def, rightX, itemY, cc, sel, slide);
                    break;
                case SettingType.Label:
                    if (def.GetString != null)
                    {
                        string val = def.GetString(_settings);
                        font.DrawTextRight(batch, val, rightX, itemY + 13, 0.65f,
                            0.55f, 0.55f, 0.60f, slide);
                    }
                    break;
            }
        }

        // ── Scroll indicator ──
        if (_filteredDefs.Count * (ItemH + ItemGap) > visH)
        {
            float totalContentH = _filteredDefs.Count * (ItemH + ItemGap);
            float barFrac = visH / totalContentH;
            float barH = MathF.Max(20f, visH * barFrac);
            float barY = contentY + (_scrollOffset / totalContentH) * visH;
            batch.Draw(px, panelX + PanelW - 6, barY, 3, barH,
                0.3f, 0.3f, 0.35f, slide * 0.5f);
        }

        // ── Bottom hint ──
        float hintY = panelY + panelH - 16;
        font.DrawText(batch, "Q/E  Categories    UP/DOWN  Navigate    LEFT/RIGHT  Adjust",
            panelX + PanelPadX, hintY, 0.4f, 0.35f, 0.35f, 0.40f, slide * 0.6f);

        batch.End();
    }

    private void RenderSlider(SpriteBatch batch, BitmapFont font, Texture2D px,
        SettingDef def, float rightX, float itemY, float[] cc, bool sel, float slide)
    {
        float val = def.GetFloat(_settings);
        float t = (val - def.Min) / (def.Max - def.Min);

        float sliderX = rightX - SliderW;
        float sliderY = itemY + (ItemH - SliderH) / 2f;

        // Track background
        batch.Draw(px, sliderX, sliderY, SliderW, SliderH,
            0.18f, 0.18f, 0.22f, slide * 0.9f);

        // Filled portion
        float fillW = t * SliderW;
        batch.Draw(px, sliderX, sliderY, fillW, SliderH,
            cc[0] * (sel ? 0.8f : 0.5f), cc[1] * (sel ? 0.8f : 0.5f),
            cc[2] * (sel ? 0.8f : 0.5f), slide * 0.9f);

        // Thumb
        float thumbX = sliderX + fillW - 5;
        float thumbY = itemY + (ItemH - 14) / 2f;
        batch.Draw(_engine.CircleTex, thumbX, thumbY, 12, 14,
            sel ? 1f : 0.7f, sel ? 1f : 0.7f, sel ? 1f : 0.7f, slide);

        // Value text
        string valStr;
        if (def.Suffix == "ms")
            valStr = $"{(int)val}{def.Suffix}";
        else
            valStr = $"{(int)val}{def.Suffix}";

        font.DrawTextRight(batch, valStr, sliderX - 10, itemY + 14, 0.55f,
            0.55f, 0.55f, 0.60f, slide);
    }

    private void RenderToggle(SpriteBatch batch, Texture2D px,
        SettingDef def, float rightX, float itemY, float[] cc, bool sel, float slide)
    {
        bool on = def.GetBool!(_settings);

        float togX = rightX - ToggleW;
        float togY = itemY + (ItemH - ToggleH) / 2f;

        // Track
        float tr = on ? cc[0] * 0.5f : 0.15f;
        float tg = on ? cc[1] * 0.5f : 0.15f;
        float tb = on ? cc[2] * 0.5f : 0.18f;
        batch.Draw(px, togX, togY, ToggleW, ToggleH, tr, tg, tb, slide * 0.9f);

        // Knob
        float knobX = on ? togX + ToggleW - ToggleH + 2 : togX + 2;
        float knobY = togY + 2;
        float knobS = ToggleH - 4;
        float kr = on ? 1f : 0.5f;
        batch.Draw(_engine.CircleTex, knobX, knobY, knobS, knobS, kr, kr, kr, slide);

        // Label
        string label = on ? "ON" : "OFF";
        float lx = togX - 10 - _engine.Font.MeasureWidth(label, 0.5f);
        _engine.Font.DrawText(batch, label, lx, itemY + 15, 0.5f,
            on ? 0.55f : 0.40f, on ? 0.75f : 0.40f, on ? 0.55f : 0.45f, slide * 0.8f);
    }

    private static float EaseOutCubic(float t)
    {
        float t1 = 1f - MathF.Max(0f, MathF.Min(1f, t));
        return 1f - t1 * t1 * t1;
    }
}
