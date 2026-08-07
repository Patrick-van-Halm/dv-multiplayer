using System;

namespace Multiplayer.Networking.Data.Train;

[Flags]
public enum CouplerInteractionType : ushort
{
    NoAction = 0,
    Start = 1,

    CouplerCouple = 2,
    CouplerPark = 4,
    CouplerDrop = 8,
    CouplerTighten = 16,
    CouplerLoosen = 32,

    HoseConnect = 64,
    HoseDisconnect = 128,

    CockOpen = 256,
    CockClose = 512,

    CoupleViaUI = 1024,
    UncoupleViaUI = 2048,

    CoupleViaRemote = 4096,
    UncoupleViaRemote = 8192,

    DragPoseUpdate = 16384,
}

internal static class CouplerInteractionRules
{
    private const CouplerInteractionType AdvancedCoupleViaUi =
        CouplerInteractionType.CoupleViaUI |
        CouplerInteractionType.HoseConnect |
        CouplerInteractionType.CockOpen;

    private const CouplerInteractionType AdvancedUncoupleViaUi =
        CouplerInteractionType.UncoupleViaUI |
        CouplerInteractionType.HoseDisconnect |
        CouplerInteractionType.CockClose;

    public static bool IsValid(CouplerInteractionType interaction)
    {
        switch (interaction)
        {
            case CouplerInteractionType.Start:
            case CouplerInteractionType.CouplerCouple:
            case CouplerInteractionType.CouplerPark:
            case CouplerInteractionType.CouplerDrop:
            case CouplerInteractionType.CouplerTighten:
            case CouplerInteractionType.CouplerLoosen:
            case CouplerInteractionType.CoupleViaUI:
            case AdvancedCoupleViaUi:
            case CouplerInteractionType.UncoupleViaUI:
            case AdvancedUncoupleViaUi:
            case CouplerInteractionType.Start |
                 CouplerInteractionType.CoupleViaRemote:
            case CouplerInteractionType.Start |
                 CouplerInteractionType.UncoupleViaRemote:
            case CouplerInteractionType.DragPoseUpdate:
                return true;
            default:
                return false;
        }
    }

    public static bool IsRemote(CouplerInteractionType interaction)
    {
        return interaction ==
                   (CouplerInteractionType.Start |
                    CouplerInteractionType.CoupleViaRemote) ||
               interaction ==
                   (CouplerInteractionType.Start |
                    CouplerInteractionType.UncoupleViaRemote);
    }

    public static bool RequiresOtherCoupler(CouplerInteractionType interaction)
    {
        return interaction == CouplerInteractionType.CouplerCouple ||
               interaction == CouplerInteractionType.CoupleViaUI ||
               interaction == AdvancedCoupleViaUi;
    }
}
