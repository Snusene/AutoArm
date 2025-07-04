using Verse;
using HarmonyLib;
using System.Reflection;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    public class AutoArmMod : Mod
    {
        public AutoArmMod(ModContentPack content) : base(content)
        {
            var harmony = new Harmony("Snues.AutoArm");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[AutoArm] Initialized successfully.");
        }
    }
}