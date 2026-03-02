using OpenTK.Windowing.GraphicsLibraryFramework;
using TaikoNova.Engine;
using TaikoNova.Engine.GL;
using TaikoNova.Game.Beatmap;
using TaikoNova.Game.Skin;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Polished song selection screen with card-based layout, smooth
/// scrolling, selection animations, and an info panel.
/// osu!lazer-inspired UX: scroll wheel, search filter, audio preview,
/// random select, exit animation.
/// </summary>
public class SongSelectScreen : Screen
{
    // ── Data ──
    private List<BeatmapInfo> _beatmaps = new();
    private List<int> _filteredIndices = new(); // indices into _beatmaps that match filter
    private int _selectedFilterIdx;             // index into _filteredIndices
    private bool _scanned;
    private string _songsPath = "";

    /// <summary>Set by file drop import to trigger a rescan on next update.</summary>
    public volatile bool NeedsRescan;

    // ── Search / filter ──
    private string _searchQuery = "";
    private float _searchBarReveal;           // 0→1 reveal animation
    private bool _searchActive;               // whether search bar is visible
    private float _searchCursorBlink;         // cursor blink timer

    // ── Animation state ──
    private float _scrollOffset;
    private float _targetScroll;
    private float _selectGlow;        // pulsing glow on selected card (0-1)
    private float _enterAnim;         // screen enter fade-in (0→1)
    private double _time;             // running clock for animations
    private float[] _cardSlide;       // per-card horizontal slide-in offset
    private float _logoWobble;        // subtle title animation
    private int _prevSelectedIndex;   // for info panel reveal on selection change
    private float _infoReveal;        // info panel content reveal (0→1)
    private float _selectionFlash;    // brief flash on selection change

    // ── Exit animation ──
    private bool _exiting;
    private float _exitAnim;          // 0→1, fade out when confirming
    private string _exitFilePath = "";
    private bool _exitPractice;

    // ── Audio preview ──
    private int _previewLoadedIndex = -1;
    private float _previewVolume;     // fade in 0→1
    private bool _previewPlaying;
    private double _previewDelay;     // delay before starting preview (seconds)

    // ── Background ──
    private BackgroundManager _background;
    private int _bgLoadedIndex = -1;

    // ── Key repeat ──
    private double _repeatTimer;
    private int _repeatDir;
    private const double RepeatDelay = 0.35;
    private const double RepeatRate  = 0.06;

    // ── Layout constants ──
    private const float TopBarH     = 44f;
    private const float BottomBarH  = 44f;
    private const float CardH       = 72f;
    private const float CardGap     = 6f;
    private const float CardItemH   = CardH + CardGap;
    private const float ListPadX    = 16f;
    private const float InfoPanelPct= 0.38f;
    private const float CardRadius  = 6f;
    private const float SearchBarH  = 36f;

    public SongSelectScreen(GameEngine engine, TaikoGame game) : base(engine, game)
    {
        _cardSlide = Array.Empty<float>();
        _prevSelectedIndex = -1;
        _background = new BackgroundManager(engine);
        _background.DimLevel = 0.82f; // overridden by settings when available
    }

    public override void OnEnter()
    {
        if (!_scanned)
        {
            ScanForBeatmaps();
            _scanned = true;
        }
        _enterAnim = 0f;
        _infoReveal = 0f;
        _selectionFlash = 0f;
        _exiting = false;
        _exitAnim = 0f;
        _prevSelectedIndex = SelectedBeatmapIndex;
        _searchQuery = "";
        _searchActive = false;
        _searchBarReveal = 0f;
        RebuildFilter();
        int total = _filteredIndices.Count + 1;
        _cardSlide = new float[total];
        for (int i = 0; i < _cardSlide.Length; i++)
            _cardSlide[i] = 120f + i * 18f;
        // Resume or start audio preview
        _previewLoadedIndex = -1;
        _previewPlaying = false;
        _previewVolume = 0f;
        _previewDelay = 0.6; // brief delay before first preview
    }

    public override void OnExit()
    {
        // Stop preview audio when leaving
        StopPreview();
    }

    /// <summary>The actual beatmap index (into _beatmaps) of the current selection, or _beatmaps.Count for practice.</summary>
    private int SelectedBeatmapIndex
    {
        get
        {
            if (_selectedFilterIdx < 0 || _selectedFilterIdx >= _filteredIndices.Count)
                return _beatmaps.Count; // practice mode
            return _filteredIndices[_selectedFilterIdx];
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Beatmap scanning (unchanged logic)
    // ═══════════════════════════════════════════════════════════════════

    private void ScanForBeatmaps()
    {
        _beatmaps.Clear();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string[] localPaths =
        {
            Path.Combine(Environment.CurrentDirectory, "Songs"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Songs"),
        };

        foreach (string rawPath in localPaths)
        {
            string path;
            try { path = Path.GetFullPath(rawPath); }
            catch { continue; }
            if (!seen.Add(path)) continue;

            if (Directory.Exists(path))
            {
                _songsPath = path;
                Console.WriteLine($"[SongSelect] Local Songs: {path}");
                AddFromDirectory(path, seen);
            }
        }

        foreach (string stableSongs in OsuInstallDetector.FindStableSongsPaths())
        {
            string full;
            try { full = Path.GetFullPath(stableSongs); }
            catch { continue; }
            if (!seen.Add(full)) continue;
            Console.WriteLine($"[SongSelect] osu! stable Songs: {full}");
            AddFromDirectory(full, seen);
        }

        var lazerFiles = OsuInstallDetector.FindLazerOsuFiles();
        if (lazerFiles.Count > 0)
        {
            Console.WriteLine($"[SongSelect] Importing {lazerFiles.Count} maps from osu! lazer...");
            foreach (string osuFile in lazerFiles)
            {
                if (seen.Contains(osuFile)) continue;
                AddSingleFile(osuFile);
            }
            Console.WriteLine($"[SongSelect] Lazer import done ({_beatmaps.Count} total maps)");
        }

        string[] extraDirs = {
            Environment.CurrentDirectory,
            AppDomain.CurrentDomain.BaseDirectory
        };

        if (string.IsNullOrEmpty(_songsPath))
            _songsPath = Path.Combine(Environment.CurrentDirectory, "Songs");
        Directory.CreateDirectory(_songsPath);

        foreach (string dir in extraDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string oszFile in Directory.GetFiles(dir, "*.osz"))
                BeatmapDecoder.ExtractOsz(oszFile, _songsPath);
        }

        if (_beatmaps.Count == 0 && Directory.Exists(_songsPath))
        {
            var fresh = BeatmapDecoder.ScanSongsDirectory(_songsPath);
            foreach (var b in fresh)
                if (!seen.Contains(b.FilePath))
                    _beatmaps.Add(b);
        }

        foreach (string dir in extraDirs)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string osuFile in Directory.GetFiles(dir, "*.osu", SearchOption.AllDirectories))
            {
                if (_beatmaps.Any(b => string.Equals(b.FilePath, osuFile, StringComparison.OrdinalIgnoreCase)))
                    continue;
                AddSingleFile(osuFile);
            }
        }

        // Sort by artist then title for a cleaner list
        _beatmaps.Sort((a, b) =>
        {
            int c = string.Compare(a.Artist, b.Artist, StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void AddFromDirectory(string path, HashSet<string> seen)
    {
        var found = BeatmapDecoder.ScanSongsDirectory(path);
        foreach (var b in found)
            if (seen.Add(b.FilePath))
                _beatmaps.Add(b);
        if (found.Count > 0)
            Console.WriteLine($"[SongSelect]   -> {found.Count} beatmaps");
    }

    private void AddSingleFile(string osuFile)
    {
        try
        {
            var map = BeatmapDecoder.Decode(osuFile);
            _beatmaps.Add(new BeatmapInfo
            {
                FilePath = osuFile,
                FolderPath = map.FolderPath,
                BackgroundFilename = map.BackgroundFilename,
                AudioFilename = map.AudioFilename,
                PreviewTime = map.PreviewTime,
                Title = map.Title,
                Artist = map.Artist,
                Version = map.Version,
                Creator = map.Creator,
                OD = map.OverallDifficulty
            });
        }
        catch { }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Update
    // ═══════════════════════════════════════════════════════════════════

    public override void Update(double dt)
    {
        _time += dt;
        var input = Engine.Input;
        int totalItems = _filteredIndices.Count + 1; // +1 practice mode

        // ── Exit animation ──
        if (_exiting)
        {
            _exitAnim = MathF.Min(1f, _exitAnim + (float)dt * 4f);
            // When exit animation completes, actually transition
            if (_exitAnim >= 1f)
            {
                _exiting = false;
                if (_exitPractice)
                    Game.StartPractice();
                else
                    Game.StartBeatmap(_exitFilePath);
            }
            return; // block all input during exit
        }

        // ── Enter animation ──
        _enterAnim = MathF.Min(1f, _enterAnim + (float)dt * 3.5f);

        // ── Card slide-in animation ──
        if (_cardSlide.Length != totalItems)
            _cardSlide = new float[totalItems];
        for (int i = 0; i < _cardSlide.Length; i++)
            _cardSlide[i] = Lerp(_cardSlide[i], 0f, (float)dt * (8f + i * 0.3f));

        // ── Search input ──
        if (input.IsPressed(Keys.Tab))
        {
            _searchActive = !_searchActive;
            if (!_searchActive && _searchQuery.Length == 0)
                _searchBarReveal = 0f;
        }

        // Typing starts search automatically (exclude space — it's a confirm key)
        if (!_searchActive && input.TextInput.Count > 0)
        {
            foreach (char c in input.TextInput)
            {
                if (c > 32 && c < 127) // > 32 to exclude space
                {
                    _searchActive = true;
                    break;
                }
            }
        }

        if (_searchActive)
        {
            _searchBarReveal = MathF.Min(1f, _searchBarReveal + (float)dt * 8f);
            _searchCursorBlink = (float)((_time * 2.0) % 1.0);

            bool changed = false;
            foreach (char c in input.TextInput)
            {
                if (c >= 32 && c < 127)
                {
                    _searchQuery += c;
                    changed = true;
                }
            }
            if (input.IsPressed(Keys.Backspace))
            {
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = _searchQuery[..^1];
                    changed = true;
                }
                else
                {
                    _searchActive = false;
                }
            }
            if (input.IsPressed(Keys.Escape))
            {
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = "";
                    changed = true;
                }
                else
                {
                    _searchActive = false;
                }
            }

            if (changed)
            {
                int prevBeatmapIdx = SelectedBeatmapIndex;
                RebuildFilter();
                // Try to stay on the same beatmap after filtering
                _selectedFilterIdx = 0;
                for (int i = 0; i < _filteredIndices.Count; i++)
                {
                    if (_filteredIndices[i] == prevBeatmapIdx)
                    {
                        _selectedFilterIdx = i;
                        break;
                    }
                }
                totalItems = _filteredIndices.Count + 1;
                _cardSlide = new float[totalItems];
                for (int i = 0; i < _cardSlide.Length; i++)
                    _cardSlide[i] = 40f + i * 8f;
            }
        }
        else
        {
            _searchBarReveal = MathF.Max(0f, _searchBarReveal - (float)dt * 6f);
        }

        // ── Navigation with key repeat ──
        // Don't process navigation keys if search is active and typing
        bool navAllowed = !_searchActive || true; // allow nav even while searching

        bool upPressed = input.IsPressed(Keys.Up) || input.IsPressed(Keys.Left);
        bool downPressed = input.IsPressed(Keys.Down) || input.IsPressed(Keys.Right);
        bool upHeld = input.IsDown(Keys.Up) || input.IsDown(Keys.Left);
        bool downHeld = input.IsDown(Keys.Down) || input.IsDown(Keys.Right);

        if (upPressed)
        {
            MoveSelection(-1);
            _repeatDir = -1;
            _repeatTimer = RepeatDelay;
        }
        else if (downPressed)
        {
            MoveSelection(1);
            _repeatDir = 1;
            _repeatTimer = RepeatDelay;
        }

        // Key repeat while held
        if (_repeatDir != 0)
        {
            bool held = _repeatDir == -1 ? upHeld : downHeld;
            if (!held)
            {
                _repeatDir = 0;
            }
            else
            {
                _repeatTimer -= dt;
                while (_repeatTimer <= 0)
                {
                    MoveSelection(_repeatDir);
                    _repeatTimer += RepeatRate;
                }
            }
        }

        // ── Mouse scroll wheel ──
        float scroll = input.ScrollDelta;
        if (MathF.Abs(scroll) > 0.01f)
        {
            int steps = -(int)MathF.Round(scroll * 3f); // 3 cards per scroll notch
            if (steps != 0)
                MoveSelection(steps);
        }

        // ── Page Up / Page Down ──
        if (input.IsPressed(Keys.PageUp))
            MoveSelection(-8);
        if (input.IsPressed(Keys.PageDown))
            MoveSelection(8);
        if (input.IsPressed(Keys.Home))
        {
            _selectedFilterIdx = 0;
            ResetCardSlides();
        }
        if (input.IsPressed(Keys.End))
        {
            _selectedFilterIdx = totalItems - 1;
            ResetCardSlides();
        }

        // ── F2 = Random select (osu! convention) ──
        if (input.IsPressed(Keys.F2) && _filteredIndices.Count > 1)
        {
            int r;
            do { r = Random.Shared.Next(_filteredIndices.Count); }
            while (r == _selectedFilterIdx && _filteredIndices.Count > 1);
            _selectedFilterIdx = r;
            ResetCardSlides();
        }

        // ── Confirm (with exit animation) ──
        bool confirmPressed = input.IsPressed(Keys.Enter)
            || (!_searchActive && input.IsPressed(Keys.Space));
        if (confirmPressed)
        {
            int bmIdx = SelectedBeatmapIndex;
            _exiting = true;
            _exitAnim = 0f;
            if (bmIdx >= _beatmaps.Count)
            {
                _exitPractice = true;
            }
            else
            {
                _exitPractice = false;
                _exitFilePath = _beatmaps[bmIdx].FilePath;
            }
            _previewPlaying = false;
        }

        // ── Refresh ──
        if (input.IsPressed(Keys.F5) || NeedsRescan)
        {
            NeedsRescan = false;
            _scanned = false;
            ScanForBeatmaps();
            _searchQuery = "";
            _searchActive = false;
            RebuildFilter();
            _selectedFilterIdx = 0;
            totalItems = _filteredIndices.Count + 1;
            _cardSlide = new float[totalItems];
            for (int i = 0; i < _cardSlide.Length; i++)
                _cardSlide[i] = 120f + i * 18f;
            _previewLoadedIndex = -1;
        }

        // ── Smooth scrolling ──
        float listH = Engine.ScreenHeight - TopBarH - BottomBarH
                     - (_searchBarReveal > 0.01f ? SearchBarH * _searchBarReveal : 0f);
        _targetScroll = _selectedFilterIdx * CardItemH - listH * 0.35f;
        float maxScroll = MathF.Max(0, totalItems * CardItemH - listH + CardItemH);
        _targetScroll = MathF.Max(0, MathF.Min(_targetScroll, maxScroll));
        _scrollOffset = Lerp(_scrollOffset, _targetScroll, (float)dt * 12f);

        // ── Glow pulse ──
        _selectGlow = (MathF.Sin((float)_time * 3.5f) + 1f) * 0.5f;

        // ── Logo wobble ──
        _logoWobble = MathF.Sin((float)_time * 1.2f) * 2f;

        // ── Info panel reveal on selection change ──
        int currentBmIdx = SelectedBeatmapIndex;
        if (currentBmIdx != _prevSelectedIndex)
        {
            _infoReveal = 0f;
            _selectionFlash = 1f;
            _prevSelectedIndex = currentBmIdx;
            _previewDelay = 0.4; // delay before starting new preview
        }
        _infoReveal = MathF.Min(1f, _infoReveal + (float)dt * SkinConfig.InfoRevealSpeed);
        _selectionFlash = MathF.Max(0f, _selectionFlash - (float)dt * 5f);

        // ── Load background for selected beatmap ──
        if (_bgLoadedIndex != currentBmIdx)
        {
            _bgLoadedIndex = currentBmIdx;
            LoadSelectedBackground();
        }

        // ── Audio preview ──
        UpdateAudioPreview(dt);
    }

    private void LoadSelectedBackground()
    {
        _background.Unload();

        int bmIdx = SelectedBeatmapIndex;
        if (bmIdx >= _beatmaps.Count) return;

        var bm = _beatmaps[bmIdx];
        if (string.IsNullOrEmpty(bm.BackgroundFilename)) return;

        var stub = new BeatmapData
        {
            FilePath = bm.FilePath,
            FolderPath = string.IsNullOrEmpty(bm.FolderPath)
                ? (Path.GetDirectoryName(bm.FilePath) ?? "")
                : bm.FolderPath,
            BackgroundFilename = bm.BackgroundFilename
        };
        _background.Load(stub);
    }

    private void MoveSelection(int delta)
    {
        int totalItems = _filteredIndices.Count + 1;
        _selectedFilterIdx = Math.Clamp(_selectedFilterIdx + delta, 0, totalItems - 1);
    }

    private void ResetCardSlides()
    {
        for (int i = 0; i < _cardSlide.Length; i++)
            _cardSlide[i] = 80f;
    }

    // ── Audio preview ──

    private void UpdateAudioPreview(double dt)
    {
        int bmIdx = SelectedBeatmapIndex;

        // Fade preview volume
        if (_previewPlaying)
            _previewVolume = MathF.Min(1f, _previewVolume + (float)dt * 2f);
        else
            _previewVolume = MathF.Max(0f, _previewVolume - (float)dt * 4f);

        if (Engine.Audio.IsMusicLoaded)
            Engine.Audio.SetMusicVolume(_previewVolume * 0.35f * Game.Settings.MasterVolume * Game.Settings.MusicVolume);

        // Stop audio once faded out
        if (_previewVolume <= 0f && !_previewPlaying && Engine.Audio.IsPlaying)
            Engine.Audio.StopMusic();

        // Delay before starting preview
        if (_previewDelay > 0)
        {
            _previewDelay -= dt;
            return;
        }

        // Load new preview when selection changes
        if (_previewLoadedIndex != bmIdx)
        {
            _previewLoadedIndex = bmIdx;
            _previewPlaying = false;
            _previewVolume = 0f;

            if (bmIdx < _beatmaps.Count)
            {
                var bm = _beatmaps[bmIdx];
                string audioPath = ResolveAudioPath(bm);
                if (!string.IsNullOrEmpty(audioPath))
                {
                    Engine.Audio.StopMusic();
                    if (Engine.Audio.LoadMusic(audioPath))
                    {
                        // Seek to preview point (or 40% of duration)
                        double seekMs = bm.PreviewTime > 0 ? bm.PreviewTime : 0;
                        Engine.Audio.PlayMusic();
                        if (seekMs > 0)
                            Engine.Audio.SeekMusic(seekMs);
                        else
                        {
                            // Seek to 40% if no preview time set
                            double dur = Engine.Audio.MusicDuration;
                            if (dur > 0)
                                Engine.Audio.SeekMusic(dur * 0.4);
                        }
                        _previewPlaying = true;
                    }
                }
                else
                {
                    Engine.Audio.StopMusic();
                }
            }
            else
            {
                Engine.Audio.StopMusic();
            }
        }
    }

    private void StopPreview()
    {
        _previewPlaying = false;
        Engine.Audio.StopMusic();
    }

    private string ResolveAudioPath(BeatmapInfo bm)
    {
        if (string.IsNullOrEmpty(bm.AudioFilename)) return "";
        string folder = string.IsNullOrEmpty(bm.FolderPath)
            ? (Path.GetDirectoryName(bm.FilePath) ?? "")
            : bm.FolderPath;
        string direct = Path.Combine(folder, bm.AudioFilename);
        if (File.Exists(direct)) return direct;

        // Try lazer audio resolver
        var resolved = LazerAudioResolver.ResolveAudio(bm.FilePath, bm.AudioFilename);
        return resolved ?? "";
    }

    // ── Filter / search ──

    private void RebuildFilter()
    {
        _filteredIndices.Clear();
        if (string.IsNullOrEmpty(_searchQuery))
        {
            for (int i = 0; i < _beatmaps.Count; i++)
                _filteredIndices.Add(i);
            return;
        }

        string query = _searchQuery.ToLowerInvariant();
        string[] tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < _beatmaps.Count; i++)
        {
            var b = _beatmaps[i];
            string hay = $"{b.Title} {b.Artist} {b.Version} {b.Creator}".ToLowerInvariant();
            bool match = true;
            foreach (string tok in tokens)
            {
                if (!hay.Contains(tok))
                {
                    match = false;
                    break;
                }
            }
            if (match) _filteredIndices.Add(i);
        }

        if (_selectedFilterIdx >= _filteredIndices.Count + 1)
            _selectedFilterIdx = Math.Max(0, _filteredIndices.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Render
    // ═══════════════════════════════════════════════════════════════════

    public override void Render(double dt)
    {
        var batch = Engine.SpriteBatch;
        var font  = Engine.Font;
        var px    = Engine.PixelTex;
        var proj  = Engine.Projection;
        int sw    = Engine.ScreenWidth;
        int sh    = Engine.ScreenHeight;
        float fadeA = EaseOutCubic(_enterAnim);

        // Exit animation: fade + slide
        float exitFade = 1f;
        float exitSlide = 0f;
        if (_exiting)
        {
            float t = EaseOutCubic(_exitAnim);
            exitFade = 1f - t;
            exitSlide = t * 60f; // cards slide right
        }
        fadeA *= exitFade;

        float infoPanelW = sw * InfoPanelPct;
        float listW = sw - infoPanelW;

        batch.Begin(proj);

        // ── Full-screen background ──
        if (_background.HasBackground)
        {
            _background.Render();
            // Exit: fade to black
            if (_exiting)
                batch.Draw(px, 0, 0, sw, sh, 0f, 0f, 0f, _exitAnim * 0.7f);
        }
        else
        {
            batch.Draw(px, 0, 0, sw, sh, 0f, 0f, 0f, 1f);
        }

        // Subtle gradient overlay on the right side
        batch.Draw(px, listW, 0, infoPanelW, sh, 0.04f, 0.04f, 0.08f, 0.95f * exitFade);

        // ── Search bar (below top bar, above cards) ──
        float searchBarOffset = 0f;
        if (_searchBarReveal > 0.01f)
        {
            float sbH = SearchBarH * _searchBarReveal;
            float sbY = TopBarH;
            searchBarOffset = sbH;

            // Background
            batch.Draw(px, 0, sbY, listW, sbH, 0.06f, 0.06f, 0.10f, 0.95f * fadeA);
            // Bottom edge
            batch.Draw(px, 0, sbY + sbH - 1, listW, 1,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], 0.3f * fadeA);

            // Search icon (magnifying glass as text)
            float searchTextY = sbY + (sbH - font.MeasureHeight(0.7f)) * 0.5f;
            font.DrawText(batch, ">", 16, searchTextY, 0.7f,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], fadeA * 0.9f);

            // Query text
            string displayQuery = _searchQuery;
            float cursorAlpha = _searchCursorBlink < 0.5f ? 0.9f : 0.2f;
            font.DrawText(batch, displayQuery, 38, searchTextY, 0.7f,
                0.9f, 0.9f, 0.95f, fadeA);

            // Cursor
            float cursorX = 38 + font.MeasureWidth(displayQuery, 0.7f) + 2;
            batch.Draw(px, cursorX, searchTextY, 2, font.MeasureHeight(0.7f),
                1f, 1f, 1f, cursorAlpha * fadeA);

            // Result count
            if (_searchQuery.Length > 0)
            {
                string resultStr = $"{_filteredIndices.Count} found";
                font.DrawTextRight(batch, resultStr, (int)(listW - 16), searchTextY, 0.6f,
                    0.5f, 0.5f, 0.6f, fadeA * 0.7f);
            }
        }

        // ── Song card list ──
        int totalItems = _filteredIndices.Count + 1;
        float listTop = TopBarH + 4 + searchBarOffset;
        float listBottom = sh - BottomBarH;

        // Empty state
        if (_filteredIndices.Count == 0 && _searchQuery.Length > 0)
        {
            float emptyY = (listTop + listBottom) * 0.5f - 30f;
            font.DrawTextShadow(batch, "No maps found", listW * 0.5f - font.MeasureWidth("No maps found", 1.0f) * 0.5f,
                emptyY, 1.0f, 0.5f, 0.5f, 0.6f, fadeA * 0.8f, 2f);
            string hint = "Try different search terms";
            font.DrawText(batch, hint, listW * 0.5f - font.MeasureWidth(hint, 0.6f) * 0.5f,
                emptyY + 30, 0.6f, 0.4f, 0.4f, 0.5f, fadeA * 0.5f);
        }
        else if (_beatmaps.Count == 0)
        {
            float emptyY = (listTop + listBottom) * 0.5f - 40f;
            font.DrawTextShadow(batch, "No songs found", listW * 0.5f - font.MeasureWidth("No songs found", 1.0f) * 0.5f,
                emptyY, 1.0f, 0.6f, 0.6f, 0.7f, fadeA * 0.9f, 2f);
            string[] hints = {
                "Drop .osz files into the Songs folder",
                "or install osu! stable/lazer",
                "Press F5 to rescan"
            };
            for (int h = 0; h < hints.Length; h++)
            {
                float hw = font.MeasureWidth(hints[h], 0.6f);
                font.DrawText(batch, hints[h], listW * 0.5f - hw * 0.5f,
                    emptyY + 34 + h * 22, 0.6f, 0.4f, 0.4f, 0.5f, fadeA * 0.6f);
            }
        }

        for (int fi = 0; fi < totalItems; fi++)
        {
            float baseY = listTop + fi * CardItemH - _scrollOffset;

            // Strict culling
            if (baseY + CardH < listTop || baseY > listBottom) continue;

            bool selected = fi == _selectedFilterIdx;
            float slideX = fi < _cardSlide.Length ? _cardSlide[fi] : 0;
            slideX += exitSlide; // exit animation slides cards right

            // Distance from selected card for depth effect
            int dist = Math.Abs(fi - _selectedFilterIdx);
            float depthDim = MathF.Max(0.45f, 1f - dist * 0.12f);
            float depthScale = selected ? 1f : MathF.Max(0.92f, 1f - dist * 0.015f);

            float cardX = ListPadX + slideX + (selected ? 8f : 4f + dist * 1f);
            float cardW = (listW - ListPadX * 2 - slideX - (selected ? 0f : 8f + dist * 1f)) * depthScale;
            float cardY = baseY;

            // ── Card background ──
            if (selected)
            {
                // Glow behind selected card
                float glowA = 0.10f + _selectGlow * 0.08f;
                batch.Draw(px, cardX - 6, cardY - 4, cardW + 12, CardH + 8,
                    SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], glowA * fadeA);

                // Selection flash overlay
                if (_selectionFlash > 0.01f)
                {
                    batch.Draw(px, cardX - 6, cardY - 4, cardW + 12, CardH + 8,
                        1f, 1f, 1f, _selectionFlash * 0.12f * fadeA);
                }

                // Card fill
                batch.Draw(px, cardX, cardY, cardW, CardH,
                    0.14f, 0.14f, 0.18f, 0.98f * fadeA);

                // Left accent bar
                batch.Draw(px, cardX, cardY, 5f, CardH,
                    SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], fadeA);

                // Top highlight line
                batch.Draw(px, cardX, cardY, cardW, 1f,
                    1f, 1f, 1f, 0.10f * fadeA);

                // Bottom subtle highlight
                batch.Draw(px, cardX, cardY + CardH - 1, cardW, 1f,
                    SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], 0.15f * fadeA);
            }
            else
            {
                batch.Draw(px, cardX, cardY, cardW, CardH,
                    0.10f, 0.10f, 0.13f, (0.55f + depthDim * 0.30f) * fadeA);
            }

            // ── Card content ──
            float textX = cardX + 18f;
            float textAlpha = fadeA * (selected ? 1f : 0.35f + depthDim * 0.35f);

            bool isPractice = fi >= _filteredIndices.Count;

            if (!isPractice)
            {
                int bmIdx = _filteredIndices[fi];
                var bm = _beatmaps[bmIdx];
                float titleScale = selected ? 1.0f : 0.85f;
                float detailScale = 0.65f;

                // Title
                string title = bm.Title;
                if (font.MeasureWidth(title, titleScale) > cardW - 40)
                    title = TruncateToFit(font, title, titleScale, cardW - 50);

                font.DrawText(batch, title, textX, cardY + 10, titleScale,
                    1f, 1f, 1f, textAlpha);

                // Artist - Version
                string subtitle = $"{bm.Artist}";
                if (!string.IsNullOrEmpty(bm.Version))
                    subtitle += $"  [{bm.Version}]";
                if (font.MeasureWidth(subtitle, detailScale) > cardW - 40)
                    subtitle = TruncateToFit(font, subtitle, detailScale, cardW - 50);

                font.DrawText(batch, subtitle, textX, cardY + 34, detailScale,
                    0.6f, 0.6f, 0.7f, textAlpha * 0.9f);

                // OD badge on the right
                if (selected)
                {
                    string odStr = $"OD {bm.OD:F1}";
                    float odW = font.MeasureWidth(odStr, 0.6f) + 12;
                    float odX = cardX + cardW - odW - 12;
                    float odY = cardY + 10;

                    float[] odColor = GetOdColor(bm.OD);
                    batch.Draw(px, odX, odY, odW, 20,
                        odColor[0], odColor[1], odColor[2], 0.25f * fadeA);
                    font.DrawText(batch, odStr, odX + 6, odY + 3, 0.6f,
                        odColor[0], odColor[1], odColor[2], fadeA);
                }

                // Bottom separator
                if (!selected)
                    batch.Draw(px, cardX + 12, cardY + CardH - 1, cardW - 24, 1,
                        0.2f, 0.2f, 0.25f, 0.3f * fadeA);
            }
            else
            {
                // ── Practice Mode card ──
                float titleScale = selected ? 1.0f : 0.85f;
                font.DrawText(batch, "Practice Mode", textX, cardY + 10, titleScale,
                    SkinConfig.DrumrollColor[0], SkinConfig.DrumrollColor[1],
                    SkinConfig.DrumrollColor[2], textAlpha);
                font.DrawText(batch, "160 BPM  |  Generated patterns  |  No audio", textX, cardY + 34,
                    0.6f, 0.5f, 0.5f, 0.55f, textAlpha * 0.8f);
            }
        }

        // ── Scroll position indicator (thin bar on left edge) ──
        if (totalItems > 1)
        {
            float trackTop = listTop + 4;
            float trackH = listBottom - listTop - 8;
            float thumbPct = MathF.Min(1f, (listBottom - listTop) / (totalItems * CardItemH));
            float thumbH = MathF.Max(20f, trackH * thumbPct);
            float scrollPct = totalItems > 1
                ? (float)_selectedFilterIdx / (totalItems - 1)
                : 0f;
            float thumbY = trackTop + (trackH - thumbH) * scrollPct;

            // Track
            batch.Draw(px, 4, trackTop, 3, trackH, 0.2f, 0.2f, 0.25f, 0.15f * fadeA);
            // Thumb
            batch.Draw(px, 4, thumbY, 3, thumbH,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2], 0.5f * fadeA);
        }

        // ── Top bar (drawn AFTER cards so it covers scrolled items) ──
        batch.Draw(px, 0, 0, sw, TopBarH, 0.08f, 0.08f, 0.12f, 1f * exitFade);
        batch.Draw(px, 0, TopBarH - 8, sw, 8, 0.08f, 0.08f, 0.12f, 0.6f * exitFade);
        batch.Draw(px, 0, TopBarH - 2, sw, 2, SkinConfig.Accent[0], SkinConfig.Accent[1],
            SkinConfig.Accent[2], 0.7f * fadeA);
        batch.Draw(px, 0, TopBarH, sw, 1, SkinConfig.Accent[0], SkinConfig.Accent[1],
            SkinConfig.Accent[2], 0.15f * fadeA);

        // Title
        font.DrawTextShadow(batch, "SELECT A SONG", 20, 12,
            0.9f, 1f, 1f, 1f, fadeA, 2f);

        // Song count + filter indicator
        string countStr = _searchQuery.Length > 0
            ? $"{_filteredIndices.Count}/{_beatmaps.Count} songs"
            : $"{_beatmaps.Count} songs";
        font.DrawTextRightShadow(batch, countStr, sw - 20, 14, 0.8f,
            0.6f, 0.6f, 0.7f, fadeA * 0.7f);

        // ── Bottom bar ──
        float bottomY = sh - BottomBarH;
        batch.Draw(px, 0, bottomY, sw, BottomBarH, 0.08f, 0.08f, 0.12f, 1f * exitFade);
        batch.Draw(px, 0, bottomY, sw, 1, 0.25f, 0.25f, 0.30f, 0.5f * fadeA);
        batch.Draw(px, 0, bottomY - 6, sw, 6, 0.08f, 0.08f, 0.12f, 0.4f * exitFade);

        // Controls
        float ctrlY = bottomY + 12;
        float cx = 20;
        cx = DrawKeyHint(batch, font, px, cx, ctrlY, "ARROWS", "Navigate", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "ENTER", "Play", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "TAB", "Search", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "F2", "Random", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "F5", "Refresh", fadeA);
        cx = DrawKeyHint(batch, font, px, cx + 16, ctrlY, "ESC", "Back", fadeA);

        // ── Audio preview indicator (small) ──
        if (_previewPlaying && _previewVolume > 0.01f)
        {
            string nowPlaying = "NOW PLAYING";
            float npW = font.MeasureWidth(nowPlaying, 0.45f);
            // Subtle animated bars
            float barAnim = MathF.Sin((float)_time * 4f);
            font.DrawText(batch, nowPlaying, sw - npW - 20, bottomY - 16, 0.45f,
                SkinConfig.Accent[0], SkinConfig.Accent[1], SkinConfig.Accent[2],
                fadeA * 0.4f * _previewVolume);
        }

        // ── Right panel: Song info ──
        RenderInfoPanel(batch, font, px, listW, infoPanelW, sh, fadeA);

        // ── Exit flash overlay ──
        if (_exiting && _exitAnim > 0.5f)
        {
            float flashA = (_exitAnim - 0.5f) * 2f; // 0→1 over second half
            batch.Draw(px, 0, 0, sw, sh, 1f, 1f, 1f, flashA * 0.15f);
        }

        batch.End();
    }

    /// <summary>Render the right-side info panel for the selected song.</summary>
    private void RenderInfoPanel(SpriteBatch batch, Engine.Text.BitmapFont font,
        Texture2D px, float panelX, float panelW, float sh, float fadeA)
    {
        float py = TopBarH + 20;
        float margin = 24f;
        float contentX = panelX + margin;
        float contentW = panelW - margin * 2;
        float revealAlpha = fadeA * EaseOutCubic(_infoReveal);
        float slideIn = (1f - EaseOutCubic(_infoReveal)) * 20f; // content slides up as it reveals

        // Panel separator line
        batch.Draw(px, panelX, TopBarH, 1f, sh - TopBarH - BottomBarH,
            0.2f, 0.2f, 0.25f, 0.3f * fadeA);

        if (_selectedFilterIdx >= _filteredIndices.Count)
        {
            // Practice mode info
            font.DrawTextShadow(batch, "PRACTICE MODE", contentX, py + slideIn, 1.1f,
                SkinConfig.DrumrollColor[0], SkinConfig.DrumrollColor[1],
                SkinConfig.DrumrollColor[2], revealAlpha, 2f);
            py += 40;

            batch.Draw(px, contentX, py + slideIn, contentW, 1, 0.3f, 0.3f, 0.35f, 0.5f * revealAlpha);
            py += 16;

            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "BPM", "160", revealAlpha); py += 32;
            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "Patterns", "Auto-generated", revealAlpha); py += 32;
            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "Audio", "None", revealAlpha); py += 32;

            py += 20;
            font.DrawText(batch, "Great for warming up", contentX, py + slideIn, 0.7f,
                0.5f, 0.5f, 0.55f, revealAlpha * 0.7f);
            py += 22;
            font.DrawText(batch, "and practicing hits!", contentX, py + slideIn, 0.7f,
                0.5f, 0.5f, 0.55f, revealAlpha * 0.7f);
            return;
        }

        var bm = _beatmaps[SelectedBeatmapIndex];

        // ── Song title (large, wraps to two lines if needed) ──
        float titleScale = 1.1f;
        py = DrawWrappedText(batch, font, bm.Title, contentX, py + slideIn, contentW,
            titleScale, 1f, 1f, 1f, revealAlpha, shadow: true);
        py += 8;

        // ── Artist ──
        string artist = bm.Artist;
        if (font.MeasureWidth(artist, 0.85f) > contentW)
            artist = TruncateToFit(font, artist, 0.85f, contentW);
        font.DrawText(batch, artist, contentX, py + slideIn, 0.85f,
            0.7f, 0.7f, 0.8f, revealAlpha * 0.9f);
        py += 28;

        // ── Divider ──
        batch.Draw(px, contentX, py + slideIn, contentW, 1, 0.3f, 0.3f, 0.35f, 0.5f * revealAlpha);
        py += 16;

        // ── Info rows (staggered reveal) ──
        int rowIdx = 0;
        if (!string.IsNullOrEmpty(bm.Version))
        {
            float rowAlpha = GetStaggeredAlpha(_infoReveal, rowIdx++);
            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "Difficulty", bm.Version, rowAlpha * fadeA);
            py += 32;
        }
        if (!string.IsNullOrEmpty(bm.Creator))
        {
            float rowAlpha = GetStaggeredAlpha(_infoReveal, rowIdx++);
            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "Mapper", bm.Creator, rowAlpha * fadeA);
            py += 32;
        }

        {
            float rowAlpha = GetStaggeredAlpha(_infoReveal, rowIdx++);
            DrawInfoRow(batch, font, px, contentX, py + slideIn, contentW, "OD", $"{bm.OD:F1}", rowAlpha * fadeA);
            py += 32;
        }

        // ── OD difficulty bar ──
        py += 4;
        float barW = contentW * 0.7f;
        float barH = 6f;
        float barReveal = GetStaggeredAlpha(_infoReveal, rowIdx);
        batch.Draw(px, contentX, py + slideIn, barW, barH, 0.15f, 0.15f, 0.18f, barReveal * fadeA * 0.8f);

        float fillPct = MathF.Min(1f, bm.OD / 10f);
        float animatedFill = fillPct * EaseOutCubic(MathF.Min(1f, _infoReveal * 1.5f));
        float[] odCol = GetOdColor(bm.OD);
        batch.Draw(px, contentX, py + slideIn, barW * animatedFill, barH,
            odCol[0], odCol[1], odCol[2], barReveal * fadeA * 0.9f);
        // Bar shine
        if (animatedFill > 0.01f)
            batch.Draw(px, contentX, py + slideIn, barW * animatedFill, 2f, 1f, 1f, 1f, barReveal * fadeA * 0.15f);
        py += 24;

        // ── Divider ──
        batch.Draw(px, contentX, py + slideIn, contentW, 1, 0.3f, 0.3f, 0.35f, 0.3f * revealAlpha);
        py += 20;

        // ── Source hint (small) ──
        string fileHint = GetFriendlySource(bm.FilePath);
        if (font.MeasureWidth(fileHint, 0.5f) > contentW)
            fileHint = TruncateToFit(font, fileHint, 0.5f, contentW);
        font.DrawText(batch, fileHint, contentX, py + slideIn, 0.5f,
            0.35f, 0.35f, 0.4f, revealAlpha * 0.5f);
    }

    /// <summary>Draw a label: value row in the info panel.</summary>
    private static void DrawInfoRow(SpriteBatch batch, Engine.Text.BitmapFont font,
        Texture2D px, float x, float y, float w, string label, string value, float fadeA)
    {
        font.DrawText(batch, label, x, y, 0.7f, 0.45f, 0.45f, 0.5f, fadeA * 0.8f);
        font.DrawTextRight(batch, value, x + w, y, 0.75f, 0.85f, 0.85f, 0.9f, fadeA);
    }

    /// <summary>Draw a key hint badge (key label + action name).</summary>
    private float DrawKeyHint(SpriteBatch batch, Engine.Text.BitmapFont font,
        Texture2D px, float x, float y, string key, string action, float fadeA)
    {
        float keyW = font.MeasureWidth(key, 0.6f) + 10;
        float keyH = 20f;

        // Key badge background
        batch.Draw(px, x, y, keyW, keyH, 0.2f, 0.2f, 0.25f, fadeA * 0.9f);
        font.DrawText(batch, key, x + 5, y + 3, 0.6f, 0.9f, 0.9f, 0.95f, fadeA);

        // Action label
        float actionX = x + keyW + 6;
        font.DrawText(batch, action, actionX, y + 3, 0.6f, 0.5f, 0.5f, 0.55f, fadeA * 0.8f);

        return actionX + font.MeasureWidth(action, 0.6f);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Get a color based on OD value (green→yellow→red).</summary>
    private static float[] GetOdColor(float od)
    {
        if (od <= 4) return new[] { 0.3f, 0.85f, 0.4f, 1f };   // Easy — green
        if (od <= 6) return new[] { 0.9f, 0.85f, 0.2f, 1f };   // Normal — yellow
        if (od <= 8) return new[] { 0.95f, 0.5f, 0.15f, 1f };  // Hard — orange
        return new[] { 0.9f, 0.25f, 0.2f, 1f };                 // Insane — red
    }

    /// <summary>Truncate text to fit within maxWidth, appending "..".</summary>
    private static string TruncateToFit(Engine.Text.BitmapFont font, string text,
        float scale, float maxWidth)
    {
        if (font.MeasureWidth(text, scale) <= maxWidth) return text;
        for (int len = text.Length - 1; len > 0; len--)
        {
            string truncated = text[..len] + "..";
            if (font.MeasureWidth(truncated, scale) <= maxWidth)
                return truncated;
        }
        return "..";
    }

    private static float Lerp(float a, float b, float t)
        => a + (b - a) * MathF.Min(1f, t);

    private static float EaseOutCubic(float t)
    {
        float t1 = 1f - t;
        return 1f - t1 * t1 * t1;
    }

    /// <summary>Get alpha for staggered row reveal. Each row delays slightly.</summary>
    private static float GetStaggeredAlpha(float masterT, int rowIndex)
    {
        float delay = rowIndex * 0.12f;
        float t = MathF.Max(0f, (masterT - delay) / 0.5f);
        return MathF.Min(1f, t);
    }

    /// <summary>Draw text that wraps to multiple lines if it exceeds maxWidth. Returns the Y after the last line.</summary>
    private static float DrawWrappedText(SpriteBatch batch, Engine.Text.BitmapFont font,
        string text, float x, float y, float maxWidth, float scale,
        float r, float g, float b, float a, bool shadow = false)
    {
        if (font.MeasureWidth(text, scale) <= maxWidth)
        {
            if (shadow)
                font.DrawTextShadow(batch, text, x, y, scale, r, g, b, a, 2f);
            else
                font.DrawText(batch, text, x, y, scale, r, g, b, a);
            return y + font.MeasureHeight(scale);
        }

        // Word wrap
        float lineH = font.MeasureHeight(scale) + 2f;
        string[] words = text.Split(' ');
        string line = "";

        foreach (string word in words)
        {
            string test = line.Length == 0 ? word : line + " " + word;
            if (font.MeasureWidth(test, scale) > maxWidth && line.Length > 0)
            {
                if (shadow)
                    font.DrawTextShadow(batch, line, x, y, scale, r, g, b, a, 2f);
                else
                    font.DrawText(batch, line, x, y, scale, r, g, b, a);
                y += lineH;
                line = word;
            }
            else
            {
                line = test;
            }
        }

        if (line.Length > 0)
        {
            // Truncate last line if still too long
            if (font.MeasureWidth(line, scale) > maxWidth)
                line = TruncateToFit(font, line, scale, maxWidth);
            if (shadow)
                font.DrawTextShadow(batch, line, x, y, scale, r, g, b, a, 2f);
            else
                font.DrawText(batch, line, x, y, scale, r, g, b, a);
            y += lineH;
        }

        return y;
    }

    /// <summary>Get a friendly source label from a file path (e.g. "osu! lazer" instead of a hash).</summary>
    private static string GetFriendlySource(string filePath)
    {
        // Lazer hash store paths contain /files/ with hash-named files
        if (filePath.Contains("/files/") || filePath.Contains("\\files\\"))
            return "osu! lazer";

        // osu! stable Songs folder
        if (filePath.Contains("/Songs/") || filePath.Contains("\\Songs\\"))
        {
            // Try to extract the folder name (e.g. "12345 Artist - Title")
            string? dir = Path.GetDirectoryName(filePath);
            if (dir != null)
                return Path.GetFileName(dir);
        }

        return Path.GetFileName(filePath);
    }

    public override void OnEscape()
    {
        // If search is active, Escape already handled in Update.
        // Otherwise, go back to main menu.
        if (_searchActive) return;
        _background.Unload();
        StopPreview();
        Game.GoToMainMenu();
    }

    public override void Dispose()
    {
        StopPreview();
        _background?.Dispose();
    }
}
