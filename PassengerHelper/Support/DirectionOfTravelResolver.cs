namespace PassengerHelper.Support;

using System;

public enum EffectiveDOTSource
{
    NeedsInput,
    Hint,
    Inferred
}

public readonly struct EffectiveDOT
{
    public readonly DirectionOfTravel Value;
    public readonly EffectiveDOTSource Source;
    public readonly bool isLocked;

    public EffectiveDOT(DirectionOfTravel Value, EffectiveDOTSource Source, bool isLocked)
    {
        this.Value = Value;
        this.Source = Source;
        this.isLocked = isLocked;
    }
}

public static class DirectionOfTravelResolver
{
    public static EffectiveDOT Compute(DirectionOfTravel userDOT, DirectionOfTravel inferredDOT)
    {
        if (inferredDOT != DirectionOfTravel.UNKNOWN)
        {
            return new EffectiveDOT(inferredDOT, EffectiveDOTSource.Inferred, true);
        }

        if (userDOT != DirectionOfTravel.UNKNOWN)
        {
            return new EffectiveDOT(userDOT, EffectiveDOTSource.Hint, false);
        }

        return new EffectiveDOT(DirectionOfTravel.UNKNOWN, EffectiveDOTSource.NeedsInput, false);
    }
}