using TaikoNova.Engine;
using TaikoNova.Engine.GL;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Renders toast-style notifications in the bottom-right corner.
/// Supports progress bars for import operations.
/// </summary>
public sealed class NotificationOverlay
{
    private readonly GameEngine _engine;
    private readonly List<Notification> _notifications = new();
    private readonly object _lock = new();

    // ── Layout ──
    private const float ToastW = 340f;
    private const float ToastH = 62f;
    private const float Margin = 16f;
    private const float Padding = 12f;
    private const float BarH = 6f;
    private const float Gap = 8f;
    private const float SlideSpeed = 6f;
    private const float FadeSpeed = 3f;
    private const float AutoDismissTime = 4.0f;

    public NotificationOverlay(GameEngine engine)
    {
        _engine = engine;
    }

    /// <summary>Show a notification with an optional progress bar (0-1). Returns an ID for updating.</summary>
    public int Show(string title, string message, float progress = -1f, float r = 0.3f, float g = 0.7f, float b = 1f)
    {
        lock (_lock)
        {
            int id = _notifications.Count > 0
                ? _notifications.Max(n => n.Id) + 1
                : 1;

            _notifications.Add(new Notification
            {
                Id = id,
                Title = title,
                Message = message,
                Progress = progress,
                AccentR = r,
                AccentG = g,
                AccentB = b,
                Alpha = 0f,
                SlideX = ToastW + Margin,
                TimeAlive = 0,
                Dismissed = false,
                AutoDismiss = progress < 0 // only auto-dismiss if no progress bar
            });

            return id;
        }
    }

    /// <summary>Update a notification's progress (0-1). Set to 1.0 to mark complete.</summary>
    public void UpdateProgress(int id, float progress, string? message = null)
    {
        lock (_lock)
        {
            var notif = _notifications.FirstOrDefault(n => n.Id == id);
            if (notif == null) return;

            notif.Progress = progress;
            if (message != null) notif.Message = message;

            // Once complete, start auto-dismiss
            if (progress >= 1f)
            {
                notif.AutoDismiss = true;
                notif.TimeAlive = 0;
            }
        }
    }

    /// <summary>Mark a notification as complete with a final message.</summary>
    public void Complete(int id, string? message = null)
    {
        lock (_lock)
        {
            var notif = _notifications.FirstOrDefault(n => n.Id == id);
            if (notif == null) return;
            notif.Progress = 1f;
            if (message != null) notif.Message = message;
            notif.AutoDismiss = true;
            notif.TimeAlive = 0;
        }
    }

    /// <summary>Mark a notification as failed.</summary>
    public void Fail(int id, string? message = null)
    {
        lock (_lock)
        {
            var notif = _notifications.FirstOrDefault(n => n.Id == id);
            if (notif == null) return;
            notif.Progress = -1f;
            notif.AccentR = 1f;
            notif.AccentG = 0.3f;
            notif.AccentB = 0.3f;
            if (message != null) notif.Message = message;
            notif.AutoDismiss = true;
            notif.TimeAlive = 0;
        }
    }

    public void Update(double dt)
    {
        float fdt = (float)dt;

        lock (_lock)
        {
            for (int i = _notifications.Count - 1; i >= 0; i--)
            {
                var n = _notifications[i];

                if (n.Dismissed)
                {
                    // Slide out
                    n.SlideX += (ToastW + Margin - n.SlideX) * fdt * SlideSpeed * 0.5f
                                + fdt * 200f;
                    n.Alpha = MathF.Max(0f, n.Alpha - fdt * FadeSpeed);
                    if (n.Alpha <= 0.01f)
                    {
                        _notifications.RemoveAt(i);
                        continue;
                    }
                }
                else
                {
                    // Slide in
                    n.SlideX += (0f - n.SlideX) * fdt * SlideSpeed;
                    n.Alpha = MathF.Min(1f, n.Alpha + fdt * FadeSpeed);

                    // Auto-dismiss timer
                    if (n.AutoDismiss)
                    {
                        n.TimeAlive += dt;
                        if (n.TimeAlive >= AutoDismissTime)
                            n.Dismissed = true;
                    }
                }
            }
        }
    }

    public void Render(double dt)
    {
        lock (_lock)
        {
            if (_notifications.Count == 0) return;

            var batch = _engine.SpriteBatch;
            var font = _engine.Font;
            var pixel = _engine.PixelTex;
            int sw = _engine.ScreenWidth;
            int sh = _engine.ScreenHeight;

            batch.Begin(_engine.Projection);

            float yOffset = 0f;
            // Render from bottom, newest at bottom
            for (int i = _notifications.Count - 1; i >= 0; i--)
            {
                var n = _notifications[i];
                float a = n.Alpha;
                if (a < 0.01f) continue;

                float x = sw - Margin - ToastW + n.SlideX;
                float y = sh - Margin - ToastH - yOffset;

                // Background
                batch.Draw(pixel, x, y, ToastW, ToastH,
                    0.1f, 0.1f, 0.14f, 0.92f * a);

                // Left accent bar
                batch.Draw(pixel, x, y, 3f, ToastH,
                    n.AccentR, n.AccentG, n.AccentB, a);

                // Title
                font.DrawText(batch, n.Title,
                    (int)(x + Padding), (int)(y + 10),
                    0.55f, n.AccentR, n.AccentG, n.AccentB, 0.95f * a);

                // Message
                font.DrawText(batch, TruncateText(n.Message, 30),
                    (int)(x + Padding), (int)(y + 28),
                    0.45f, 0.7f, 0.7f, 0.75f, 0.85f * a);

                // Progress bar
                if (n.Progress >= 0f)
                {
                    float barY = y + ToastH - BarH - 8f;
                    float barW = ToastW - Padding * 2;

                    // Track
                    batch.Draw(pixel, x + Padding, barY, barW, BarH,
                        0.2f, 0.2f, 0.25f, 0.7f * a);

                    // Fill
                    float fillW = barW * MathF.Min(1f, n.Progress);
                    if (fillW > 0.5f)
                    {
                        batch.Draw(pixel, x + Padding, barY, fillW, BarH,
                            n.AccentR, n.AccentG, n.AccentB, 0.9f * a);
                    }

                    // Percentage text
                    int pct = (int)(n.Progress * 100f);
                    string pctStr = n.Progress >= 1f ? "Done!" : $"{pct}%";
                    font.DrawTextRight(batch, pctStr,
                        (int)(x + ToastW - Padding), (int)(y + 28),
                        0.4f, 0.6f, 0.6f, 0.65f, 0.8f * a);
                }

                yOffset += ToastH + Gap;
            }

            batch.End();
        }
    }

    private static string TruncateText(string text, int maxLen)
    {
        if (text.Length <= maxLen) return text;
        return text[..(maxLen - 2)] + "..";
    }

    private class Notification
    {
        public int Id;
        public string Title = "";
        public string Message = "";
        public float Progress;    // <0 = no bar, 0-1 = progress
        public float AccentR, AccentG, AccentB;
        public float Alpha;
        public float SlideX;      // offset for slide animation
        public double TimeAlive;
        public bool Dismissed;
        public bool AutoDismiss;
    }
}
