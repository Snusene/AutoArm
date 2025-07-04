using Verse;
using HarmonyLib;

namespace AutoArm
{
    public class AutoArmMod : Mod
    {
        public AutoArmMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("Your.Mod.UniqueID.AutoArm");
            harmony.PatchAll();
            Log.Message("[AutoArm] Loaded and PatchAll called.");
        }
    }
}
