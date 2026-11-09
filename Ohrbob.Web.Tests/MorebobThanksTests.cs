using NUnit.Framework;
using Ohrbob.Web.Models;

namespace Ohrbob.Web.Tests;

public class MorebobThanksTests
{
    [Test]
    public void For_IsStableForTheSameAccount()
    {
        var accountId = Guid.NewGuid().ToString();

        Assert.That(MorebobThanks.For(accountId), Is.EqualTo(MorebobThanks.For(accountId)));
    }

    [Test]
    public void For_VariesAcrossAccounts()
    {
        var messages = Enumerable.Range(0, 20)
            .Select(_ => MorebobThanks.For(Guid.NewGuid().ToString()))
            .Distinct()
            .ToList();

        Assert.That(messages.Count, Is.GreaterThan(1));
    }

    [Test]
    public void For_NullOrEmpty_ReturnsFirstMessage()
    {
        Assert.That(MorebobThanks.For(null), Is.Not.Empty);
        Assert.That(MorebobThanks.For(string.Empty), Is.EqualTo(MorebobThanks.For(null)));
    }
}
