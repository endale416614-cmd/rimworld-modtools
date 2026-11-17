using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace BS9000
{
    public class HediffComp_BS9000 : HediffComp
    {
        const int IntervalTicks = 60000;
        const float BloodlossSeverity = 0.45f;
        const float MaxBloodloss = 0.44f; // top of the moderate stage: the tap never pushes the host into severe or extreme blood loss

        bool enabled = true;

        public override void CompPostTickInterval(ref float severityAdjustment, int delta)
        {
            if (enabled && Pawn.IsHashIntervalTick(IntervalTicks, delta)) Tap(Pawn);
        }

        public override void CompExposeData() => Scribe_Values.Look(ref enabled, "enabled", true);

        public override IEnumerable<Gizmo> CompGetGizmos()
        {
            yield return new Command_Toggle
            {
                defaultLabel = "BS9000_Toggle".Translate(),
                defaultDesc = "BS9000_ToggleDesc".Translate(),
                icon = TapTex.Blood,
                isActive = () => enabled,
                toggleAction = () => enabled = !enabled
            };
        }

        // Same effect as Recipe_ExtractHemogen, minus the surgery, capped so the host stays below severe blood loss.
        public static void Tap(Pawn pawn)
        {
            Map map = pawn.MapHeld;
            if (map == null || !pawn.health.CanBleed) return;
            float current = pawn.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.BloodLoss)?.Severity ?? 0f;
            float draw = Mathf.Min(BloodlossSeverity, MaxBloodloss - current);
            if (draw <= 0f) return;
            HealthUtility.AdjustSeverity(pawn, HediffDefOf.BloodLoss, draw);
            // Bite feedback as in SanguophageUtility.DoBite: blood on the floor, plus the Bloodfeed sound and splash through the effecter.
            // SpawnAttached, not Spawn: the drip sprayer needs the pawn as its target, and Spawn(Thing, Map) only passes the cell.
            BS9000DefOf.BS9000_Bite.SpawnAttached(pawn, map).Cleanup();
            if (pawn.RaceProps.BloodDef != null) FilthMaker.TryMakeFilth(pawn.PositionHeld, map, pawn.RaceProps.BloodDef, pawn.LabelShort);
            // Each thought's requiredTraits/nullifyingTraits decide which one the host gets.
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(BS9000DefOf.BS9000_Hemorrhoids);
            pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(BS9000DefOf.BS9000_Masochist);
            if (!GenPlace.TryPlaceThing(ThingMaker.MakeThing(ThingDefOf.HemogenPack), pawn.PositionHeld, map, ThingPlaceMode.Near))
                Log.Error("Could not drop hemogen pack near " + pawn.PositionHeld.ToString());
            if (PawnUtility.ShouldSendNotificationAbout(pawn))
                Messages.Message("BS9000_Tapped".Translate(pawn.Named("PAWN")), pawn, MessageTypeDefOf.NeutralEvent, historical: false);
        }

        [DebugAction("Pawns", "Bloodsucker 9000: tap now", requiresBiotech: true, actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TapNow(Pawn p) => Tap(p);
    }

    [DefOf]
    public static class BS9000DefOf
    {
        public static EffecterDef BS9000_Bite;
        public static ThoughtDef BS9000_Hemorrhoids;
        public static ThoughtDef BS9000_Masochist;
        public static JobDef BS9000_TankTransfuse;

        static BS9000DefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(BS9000DefOf));
    }

    [StaticConstructorOnStartup]
    static class TapTex
    {
        public static readonly Texture2D Blood = ContentFinder<Texture2D>.Get("UI/Icons/Medical/Bleeding");
    }
}
