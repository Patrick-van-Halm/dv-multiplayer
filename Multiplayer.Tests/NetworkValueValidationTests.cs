using Multiplayer.Utils;
using UnityEngine;
using Xunit;

namespace Multiplayer.Tests;

public class NetworkValueValidationTests
{
    [Fact]
    public void IsFinite_RejectsNonFiniteVectorComponents()
    {
        Assert.False(
            new Vector3(float.NaN, 0f, 0f).IsFinite());
        Assert.False(
            new Vector3(0f, float.PositiveInfinity, 0f).IsFinite());
        Assert.True(new Vector3(1f, 2f, 3f).IsFinite());
    }

    [Fact]
    public void IsFinite_RejectsNonFiniteQuaternionComponents()
    {
        Assert.False(
            new Quaternion(0f, 0f, float.NegativeInfinity, 1f)
                .IsFinite());
        Assert.True(Quaternion.identity.IsFinite());
    }
}
