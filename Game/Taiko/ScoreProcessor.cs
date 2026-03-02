namespace TaikoNova.Game.Taiko;

/// <summary>
/// Processes score, combo, accuracy, and HP for taiko gameplay.
/// Matches osu!taiko scoring model.
/// </summary>
public class ScoreProcessor
{
    // ── Results ──
    public long Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int CountGreat { get; private set; }
    public int CountGood { get; private set; }
    public int CountMiss { get; private set; }

    // ── HP ──
    public double HP { get; private set; } = 1.0;
    public bool HasFailed { get; private set; }

    // ── Difficulty ──
    private readonly double _hpDrain;
    private readonly double _od;

    // ── Scoring constants ──
    private const int GreatScore = 300;
    private const int GoodScore = 100;

    // HP deltas
    private const double HpGreat = 0.04;
    private const double HpGood = 0.02;
    private const double HpMiss = -0.06;

    // ── Latest judgment (for display) ──
    public HitResult LastJudgment { get; private set; } = HitResult.None;
    public double LastJudgmentTime { get; private set; }
    public bool LastJudgmentIsBig { get; private set; }

    // ── Combo animation ──
    public double ComboPopTime { get; private set; }

    public ScoreProcessor(float hpDrain, float od)
    {
        _hpDrain = hpDrain;
        _od = od;
    }

    /// <summary>
    /// Apply a hit judgment.
    /// </summary>
    public void ApplyHit(HitResult result, double time, bool isBig = false)
    {
        LastJudgment = result;
        LastJudgmentTime = time;
        LastJudgmentIsBig = isBig;

        switch (result)
        {
            case HitResult.Great:
                CountGreat++;
                Combo++;
                Score += (long)(GreatScore * GetComboMultiplier());
                if (isBig) Score += GreatScore; // Bonus for big notes
                HP = Math.Clamp(HP + HpGreat, 0, 1);
                ComboPopTime = time;
                break;

            case HitResult.Good:
                CountGood++;
                Combo++;
                Score += (long)(GoodScore * GetComboMultiplier());
                if (isBig) Score += GoodScore;
                HP = Math.Clamp(HP + HpGood, 0, 1);
                ComboPopTime = time;
                break;

            case HitResult.Miss:
                CountMiss++;
                Combo = 0;
                HP = Math.Clamp(HP + HpMiss, 0, 1);
                if (HP <= 0) HasFailed = true;
                break;
        }

        if (Combo > MaxCombo) MaxCombo = Combo;
    }

    /// <summary>
    /// Apply drumroll/denden tick.
    /// </summary>
    public void ApplyTick(double time)
    {
        Score += 30;
        HP = Math.Clamp(HP + 0.005, 0, 1);
    }

    /// <summary>
    /// Passive HP drain per update cycle.
    /// </summary>
    public void ApplyDrain(double deltaMs)
    {
        double drainRate = 0.0001 * (_hpDrain / 5.0);
        HP = Math.Clamp(HP - drainRate * deltaMs, 0, 1);
        if (HP <= 0) HasFailed = true;
    }

    /// <summary>Accuracy as a ratio (0.0 – 1.0).</summary>
    public double Accuracy
    {
        get
        {
            int total = CountGreat + CountGood + CountMiss;
            if (total == 0) return 1.0;
            return (CountGreat * 1.0 + CountGood * 0.5) / total;
        }
    }

    /// <summary>Accuracy formatted as percentage string.</summary>
    public string AccuracyDisplay => $"{Accuracy * 100:F2}%";

    /// <summary>Letter grade.</summary>
    public string Grade
    {
        get
        {
            double acc = Accuracy;
            if (CountMiss == 0 && acc >= 1.0) return "SS";
            if (acc >= 0.95) return "S";
            if (acc >= 0.90) return "A";
            if (acc >= 0.80) return "B";
            if (acc >= 0.70) return "C";
            return "D";
        }
    }

    private double GetComboMultiplier()
    {
        // Score multiplier increases with combo, capped
        return 1.0 + Math.Min(Combo, 100) * 0.02;
    }

    /// <summary>Reset all state for a new play.</summary>
    public void Reset()
    {
        Score = 0;
        Combo = 0;
        MaxCombo = 0;
        CountGreat = 0;
        CountGood = 0;
        CountMiss = 0;
        HP = 1.0;
        HasFailed = false;
        LastJudgment = HitResult.None;
    }
}
