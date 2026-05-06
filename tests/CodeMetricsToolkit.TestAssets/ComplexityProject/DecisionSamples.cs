namespace ComplexityProject;

public sealed class DecisionSamples
{
    private readonly int _baseline;

    public DecisionSamples()
        : this(0)
    {
    }

    public DecisionSamples(int baseline)
    {
        _baseline = baseline;
    }

    public int Score(Order? order, IReadOnlyList<int> values)
    {
        int score = _baseline + (order?.Priority ?? 0);

        if (order is null)
        {
            return 0;
        }
        else if (order.Status is "blocked" or "failed")
        {
            score -= 10;
        }

        for (int i = 0; i < values.Count; i++)
        {
            score += values[i] > 0 ? values[i] : 0;
        }

        foreach (int value in values)
        {
            if (value is > 10 and < 100)
            {
                score++;
            }
        }

        int guard = 0;
        while (score < 100 && guard < 3)
        {
            score += guard;
            guard++;
        }

        do
        {
            score--;
        }
        while (score > 100 || order.IsExpress);

        score += order.Status switch
        {
            "new" => 1,
            "paid" when order.Total > 0 => 2,
            "shipped" => 3,
            _ => 0
        };

        try
        {
            score += ParseAdjustment(order.AdjustmentText);
        }
        catch (FormatException) when (!string.IsNullOrWhiteSpace(order.AdjustmentText))
        {
            score -= 1;
        }

        return score;

        static int ParseAdjustment(string? text)
        {
            return int.TryParse(text, out int parsed) ? parsed : 0;
        }
    }

    public string Classify(int value)
    {
        string prefix = _baseline == 0 ? string.Empty : $"{_baseline}:";

        switch (value)
        {
            case < 0:
                return $"{prefix}negative";
            case 0:
                return $"{prefix}zero";
            case > 100:
                return $"{prefix}large";
            default:
                return $"{prefix}normal";
        }
    }
}

public sealed record Order(string Status, int Priority, decimal Total, bool IsExpress, string? AdjustmentText);
