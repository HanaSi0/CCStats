using PvpStats.Types.Match;
using PvpStats.Types.Player;
using System.Collections.Generic;
using System.Numerics;

namespace PvpStats.Helpers;
internal static class TextureHelper {

    internal static Dictionary<Job, uint> JobIcons => new() {
        { Job.PLD, 62119 },
        { Job.WAR, 62121 },
        { Job.DRK, 62132 },
        { Job.GNB, 62137 },
        { Job.MNK, 62120 },
        { Job.DRG, 62122 },
        { Job.NIN, 62130 },
        { Job.SAM, 62134 },
        { Job.RPR, 62139 },
        { Job.VPR, 62141 },
        { Job.WHM, 62124 },
        { Job.SCH, 62128 },
        { Job.AST, 62133 },
        { Job.SGE, 62140 },
        { Job.BRD, 62123 },
        { Job.MCH, 62131 },
        { Job.DNC, 62138 },
        { Job.BLM, 62125 },
        { Job.SMN, 62127 },
        { Job.RDM, 62135 },
        { Job.PCT, 62142 },
    };

    internal static uint TeamIcon => 60563;
    internal static uint EnemyIcon => 60562;
    internal static string CCTeamIconTexture => "ui/uld/PVPMKSResult_hr1.tex";

    internal static string MedicineKitTexture => "bgcommon/world/common/vfx_for_bg/texture/mark028_o.atex";
    internal static string MedicineKitTexture2 => "bgcommon/world/common/vfx_for_bg/texture/mark027_o.atex";
    internal static string MedicineKitTexture3 => "bgcommon/world/common/vfx_for_bg/texture/mark027wo.atex";

    internal static (Vector2 UV0, Vector2 UV1) GetMedicineKitUVs() {
        return (new Vector2(0, 0.5f), new Vector2(0.5f, 1f));
    }
}
