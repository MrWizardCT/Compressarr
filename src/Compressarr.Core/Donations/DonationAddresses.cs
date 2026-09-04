namespace Compressarr.Core.Donations;

public sealed record CryptoCurrency(string Name, string Glyph, string Color, string Address);

/// <summary>Donation addresses shown on the web UI's Donate page.</summary>
public static class DonationAddresses
{
    public static IReadOnlyList<CryptoCurrency> All { get; } = new List<CryptoCurrency>
    {
        new("Bitcoin", "₿", "#F7931A", "37TUnyD6GwTngbX7xwdxKMACTrv1Bnv2WF"),
        new("Litecoin", "Ł", "#345D9D", "MAJuhqgJzodjnxPvPm7AerdvoYB7796b2R"),
        new("Dogecoin", "Ð", "#C2A633", "D8ef6c1jRgTpWJhRqA8ty3bijFKFuqWgVL"),
        new("Shiba Inu", "SHIB", "#EE7C21", "0x4425aC4F1E459825A5DaE3a46Cc0eb696F9258e8"),
        new("Bitcoin Cash", "BCH", "#0AC18E", "19N9zygm6bPnLDMDEvxnSery16zdtzQysD"),
        new("Ethereum", "Ξ", "#627EEA", "0x418eF1149E7eCada8Efb6a2a7DE896Fb5B68eBb4")
    };
}
