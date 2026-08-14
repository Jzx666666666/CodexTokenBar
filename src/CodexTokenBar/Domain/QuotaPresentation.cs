namespace CodexTokenBar.Domain;

public enum QuotaTone
{
    Green,
    Yellow,
    Red,
    Gray,
}

public static class QuotaPresentation
{
    public static QuotaTone GetTone(int? remainingPercent) => remainingPercent switch
    {
        null => QuotaTone.Gray,
        > 30 => QuotaTone.Green,
        >= 10 => QuotaTone.Yellow,
        _ => QuotaTone.Red,
    };
}
