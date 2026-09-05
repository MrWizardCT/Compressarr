using Compressarr.Core.Donations;
using Xunit;

namespace Compressarr.Core.Tests.Donations;

public class DonationAddressesTests
{
    // Guards against a typo in the split/concat fragments in DonationAddresses.cs silently
    // producing a wrong (but still plausible-looking) address - these are real donation targets.
    [Fact]
    public void All_AddressesMatchExactExpectedValues()
    {
        var byName = DonationAddresses.All.ToDictionary(c => c.Name, c => c.Address);

        Assert.Equal("37TUnyD6GwTngbX7xwdxKMACTrv1Bnv2WF", byName["Bitcoin"]);
        Assert.Equal("MAJuhqgJzodjnxPvPm7AerdvoYB7796b2R", byName["Litecoin"]);
        Assert.Equal("D8ef6c1jRgTpWJhRqA8ty3bijFKFuqWgVL", byName["Dogecoin"]);
        Assert.Equal("0x4425aC4F1E459825A5DaE3a46Cc0eb696F9258e8", byName["Shiba Inu"]);
        Assert.Equal("19N9zygm6bPnLDMDEvxnSery16zdtzQysD", byName["Bitcoin Cash"]);
        Assert.Equal("0x418eF1149E7eCada8Efb6a2a7DE896Fb5B68eBb4", byName["Ethereum"]);
    }

    [Fact]
    public void All_HasExactlySixCurrencies()
    {
        Assert.Equal(6, DonationAddresses.All.Count);
    }
}
