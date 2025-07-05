using Verse;
using HarmonyLib;
using System.Reflection;
using System.Linq;
using RimWorld;

namespace AutoArm
{
    [StaticConstructorOnStartup]
    public static class AutoArmInit
    {
        static AutoArmInit()
        {
            var harmony = new Harmony("Snues.AutoArm");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[AutoArm] Initialized successfully.");
        }
    }

    [HarmonyPatch(typeof(Game), "InitNewGame")]
    public static class Game_InitNewGame_Patch
    {
        public static void Postfix(Game __instance)
        {
            GameComponentHelper.EnsureGameComponent(__instance);
        }
    }

    [HarmonyPatch(typeof(Game), "LoadGame")]
    public static class Game_LoadGame_Patch
    {
        public static void Postfix(Game __instance)
        {
            GameComponentHelper.EnsureGameComponent(__instance);
        }
    }

    public static class GameComponentHelper
    {
        public static void EnsureGameComponent(Game game)
        {
            if (game?.components == null)
                return;

            if (game.components.Any(c => c is WeaponEquipGameComponent))
            {
                Log.Message("[AutoArm] WeaponEquipGameComponent already exists");
                return;
            }

            game.components.Add(new WeaponEquipGameComponent(game));
            Log.Message("[AutoArm] WeaponEquipGameComponent added to game");
        }
    }
}