using System.Reflection;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;
using Ohrbob.E2ETests.Infrastructure;

namespace Ohrbob.E2ETests;

/// <summary>
/// Guards the E2E sharding contract. The fake podcast host keeps a single
/// process-wide configuration, so every fixture that calls
/// <see cref="TestCasts.ConfigureAsync"/> must be serialized and assigned to
/// exactly one of the two testcasts shards. A class that forgets this would
/// otherwise run next to another shard and clobber its feed configuration.
/// </summary>
public class TestCastsShardingTests
{
    private static readonly string[] Shards = { "TestCastsA", "TestCastsB" };

    [Test]
    public void TestcastsFixtures_AreSerialAndBelongToExactlyOneShard()
    {
        var offenders = new List<string>();
        var fixtures = typeof(TestCastsShardingTests).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(PageTest).IsAssignableFrom(t))
            .OrderBy(t => t.Name)
            .ToList();

        var detected = fixtures.Where(CallsConfigureAsync).ToHashSet();
        var tagged = fixtures
            .Where(t => t.GetCustomAttributes<CategoryAttribute>()
                .Any(attribute => attribute.Name == "TestCasts"))
            .ToHashSet();

        // Keeps the IL scan honest: every tagged fixture must really touch
        // testcasts, and every fixture that touches it must be tagged.
        Assert.That(
            detected.Select(t => t.Name).OrderBy(name => name),
            Is.EquivalentTo(tagged.Select(t => t.Name).OrderBy(name => name)),
            "the [Category(\"TestCasts\")] markers and the fixtures calling TestCasts.ConfigureAsync must match");

        foreach (var type in detected)
        {
            var categories = type.GetCustomAttributes<CategoryAttribute>()
                .Select(attribute => attribute.Name)
                .ToHashSet(StringComparer.Ordinal);

            if (type.GetCustomAttributes<NonParallelizableAttribute>().FirstOrDefault() is null)
            {
                offenders.Add($"{type.Name}: missing [NonParallelizable]");
            }

            if (categories.Count(Shards.Contains) != 1)
            {
                offenders.Add($"{type.Name}: expected exactly one of {string.Join("/", Shards)}, " +
                              $"found [{string.Join(", ", categories.Where(Shards.Contains))}]");
            }
        }

        Assert.That(offenders, Is.Empty,
            "testcasts fixtures must be serial and belong to exactly one testcasts shard");
    }

    private static bool CallsConfigureAsync(Type type)
    {
        var target = typeof(TestCasts).GetMethod(nameof(TestCasts.ConfigureAsync));
        if (target is null)
        {
            return false;
        }

        var owners = new[] { type }
            .Concat(type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic));

        foreach (var owner in owners)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
                                       BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

            foreach (var method in owner.GetMethods(flags).Cast<MethodBase>()
                         .Concat(owner.GetConstructors(flags)))
            {
                byte[]? il;
                try
                {
                    il = method.GetMethodBody()?.GetILAsByteArray();
                }
                catch
                {
                    continue;
                }

                if (il is null)
                {
                    continue;
                }

                for (var i = 0; i + 4 < il.Length; i++)
                {
                    // call (0x28) and callvirt (0x6F) always carry a 4-byte metadata token.
                    if (il[i] is not (0x28 or 0x6F))
                    {
                        continue;
                    }

                    try
                    {
                        var callee = owner.Module.ResolveMethod(
                            BitConverter.ToInt32(il, i + 1),
                            owner.IsGenericType ? owner.GetGenericArguments() : null,
                            method.IsGenericMethod ? method.GetGenericArguments() : null);

                        if (callee == target)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // The byte pair was not a method token; keep scanning.
                    }
                }
            }
        }

        return false;
    }
}
