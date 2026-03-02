using TaikoNova.Engine;

namespace TaikoNova.Game.Screens;

/// <summary>
/// Base class for all game screens.
/// </summary>
public abstract class Screen : IDisposable
{
    protected GameEngine Engine { get; }
    protected TaikoGame Game { get; }

    protected Screen(GameEngine engine, TaikoGame game)
    {
        Engine = engine;
        Game = game;
    }

    /// <summary>Called when this screen becomes the active screen.</summary>
    public virtual void OnEnter() { }

    /// <summary>Called when leaving this screen.</summary>
    public virtual void OnExit() { }

    /// <summary>Called when Escape is pressed.</summary>
    public virtual void OnEscape() { }

    /// <summary>Update game logic.</summary>
    public abstract void Update(double deltaTime);

    /// <summary>Render the screen.</summary>
    public abstract void Render(double deltaTime);

    public virtual void Dispose() { }
}
