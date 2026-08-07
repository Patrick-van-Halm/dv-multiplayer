using System;
using Multiplayer.Networking.Data.Items;
using Xunit;

namespace Multiplayer.Tests;

public class TrackedValueTests
{
    [Fact]
    public void AcceptsValue_RequiresRegisteredRuntimeType()
    {
        int current = 3;
        var value = new TrackedValue<int>(
            "test.value",
            () => current,
            next => current = next);

        Assert.True(value.AcceptsValue(4));
        Assert.False(value.AcceptsValue(4f));
        Assert.False(value.AcceptsValue(null));
    }

    [Fact]
    public void SetValueFromObject_UpdatesValueAndDirtyBaseline()
    {
        int current = 3;
        var value = new TrackedValue<int>(
            "test.value",
            () => current,
            next => current = next);

        value.SetValueFromObject(7);

        Assert.Equal(7, current);
        Assert.False(value.IsDirty);
    }

    [Fact]
    public void SetValueFromObject_RejectsWrongRuntimeType()
    {
        int current = 3;
        var value = new TrackedValue<int>(
            "test.value",
            () => current,
            next => current = next);

        Assert.Throws<ArgumentException>(
            () => value.SetValueFromObject("3"));
        Assert.Equal(3, current);
    }
}
