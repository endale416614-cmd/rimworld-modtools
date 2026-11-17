using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace BS9000
{
    public class CompProperties_DrainLance : CompProperties
    {
        // Hemogen packs yielded per point of body size, so an adult human gives this many.
        public float packsPerBodySize = 6f;

        public CompProperties_DrainLance()
        {
            compClass = typeof(CompTargetEffect_DrainLance);
        }
    }

    // Verb_CastTargetEffect runs every CompTargetEffect on the apparel and then spends a
    // charge through ReloadableCompSource, so maxCharges 1 + destroyOnEmpty breaks the
    // lance on use with no code here. Same arrangement as the vanilla psychic lances.
    public class CompTargetEffect_DrainLance : CompTargetEffect
    {
        CompProperties_DrainLance Props => (CompProperties_DrainLance)props;

        public override void DoEffectOn(Pawn user, Thing target)
        {
            if (target is Pawn victim) Drain(victim, user, Props.packsPerBodySize, parent?.def);
        }

        public static bool CanDrain(Pawn victim)
        {
            return victim != null
                && !victim.Dead
                && victim.MapHeld != null
                && victim.RaceProps.IsFlesh
                && victim.RaceProps.BloodDef != null;
        }

        public static void Drain(Pawn victim, Pawn user, float packsPerBodySize, ThingDef weapon = null)
        {
            if (!CanDrain(victim)) return;

            Map map = victim.MapHeld;
            IntVec3 cell = victim.PositionHeld;
            int packs = Mathf.Max(1, Mathf.RoundToInt(packsPerBodySize * victim.BodySize));

            // Reuse the implant's bite effecter: same sound, splash and drips.
            BS9000DefOf.BS9000_Bite.SpawnAttached(victim, map).Cleanup();
            FilthMaker.TryMakeFilth(cell, map, victim.RaceProps.BloodDef, victim.LabelShort);

            // The tank empties the subject, so the kill is the effect, not a side effect.
            victim.Kill(new DamageInfo(DamageDefOf.Cut, 99999f, 0f, -1f, user, null, weapon), null);

            int remaining = packs;
            while (remaining > 0)
            {
                int count = Mathf.Min(remaining, ThingDefOf.HemogenPack.stackLimit);
                Thing stack = ThingMaker.MakeThing(ThingDefOf.HemogenPack);
                stack.stackCount = count;
                if (!GenPlace.TryPlaceThing(stack, cell, map, ThingPlaceMode.Near))
                {
                    Log.Error("Could not drop hemogen packs near " + cell.ToString());
                    break;
                }
                remaining -= count;
            }

            if (PawnUtility.ShouldSendNotificationAbout(victim) || (user != null && PawnUtility.ShouldSendNotificationAbout(user)))
            {
                Messages.Message("BS7500_Drained".Translate(victim.Named("PAWN"), packs.Named("COUNT")),
                    new TargetInfo(cell, map), MessageTypeDefOf.NeutralEvent, historical: false);
            }
        }

        [DebugAction("Pawns", "BS-7500: drain now", requiresBiotech: true, actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void DrainNow(Pawn p) => Drain(p, null, 6f);
    }
}
