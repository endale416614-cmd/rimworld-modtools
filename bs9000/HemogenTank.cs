using System.Collections.Generic;
using LudeonTK;
using RimWorld;
using Verse;
using Verse.AI;

namespace BS9000
{
    public class CompProperties_HemogenTank : CompProperties
    {
        // One charge is one hemogen pack, so these mirror what ingesting a pack does.
        public float hemogenPerPack = 0.2f;
        public float bloodLossPerPack = 0.25f;
        public float minBloodLossToTreat = 0.25f;

        public CompProperties_HemogenTank()
        {
            compClass = typeof(Comp_HemogenTank);
        }
    }

    // Storage and manual refill come from the sibling CompApparelReloadable, which holds
    // hemogen packs as its ammo. This comp only spends what that one stores.
    public class Comp_HemogenTank : ThingComp
    {
        public CompProperties_HemogenTank Props => (CompProperties_HemogenTank)props;

        public CompApparelReloadable Reloadable => parent.GetComp<CompApparelReloadable>();

        public Pawn Wearer => (parent as Apparel)?.Wearer;

        public bool HasCharge
        {
            get
            {
                CompApparelReloadable comp = Reloadable;
                return comp != null && comp.RemainingCharges > 0;
            }
        }

        public bool TrySpendCharge()
        {
            CompApparelReloadable comp = Reloadable;
            if (comp == null || comp.RemainingCharges <= 0) return false;
            comp.UsedOnce();
            return true;
        }
    }

    public static class HemogenTankUtility
    {
        public static Comp_HemogenTank WornTank(Pawn pawn)
        {
            List<Apparel> worn = pawn?.apparel?.WornApparel;
            if (worn == null) return null;
            for (int i = 0; i < worn.Count; i++)
            {
                Comp_HemogenTank tank = worn[i].TryGetComp<Comp_HemogenTank>();
                if (tank != null && tank.HasCharge) return tank;
            }
            return null;
        }

        // ShouldConsumeHemogenNow is Value < targetValue, the threshold the player sets on
        // the hemogen gizmo. Gating on it is what stops the tank topping a pawn up forever.
        public static bool WantsHemogen(Pawn pawn)
        {
            Gene_Hemogen gene = pawn?.genes?.GetFirstGeneOfType<Gene_Hemogen>();
            return gene != null && gene.hemogenPacksAllowed && gene.ShouldConsumeHemogenNow();
        }

        public static bool WantsBlood(Pawn pawn, float minSeverity)
        {
            Hediff bloodLoss = pawn?.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.BloodLoss);
            return bloodLoss != null && bloodLoss.Severity >= minSeverity;
        }

        public static bool NeedsTransfusion(Pawn patient, CompProperties_HemogenTank props)
        {
            return WantsHemogen(patient) || WantsBlood(patient, props.minBloodLossToTreat);
        }

        // Spend one charge on the patient. Hemogen first, because a hemogenic pawn that is
        // also bleeding is better served by the resource it cannot regrow.
        public static bool Transfuse(Comp_HemogenTank tank, Pawn patient)
        {
            if (tank == null || patient == null || patient.Dead) return false;

            bool wantsHemogen = WantsHemogen(patient);
            bool wantsBlood = WantsBlood(patient, tank.Props.minBloodLossToTreat);
            if (!wantsHemogen && !wantsBlood) return false;
            if (!tank.TrySpendCharge()) return false;

            if (wantsHemogen) GeneUtility.OffsetHemogen(patient, tank.Props.hemogenPerPack, true);
            else HealthUtility.AdjustSeverity(patient, HediffDefOf.BloodLoss, -tank.Props.bloodLossPerPack);

            return true;
        }
    }

    // Worn apparel is not ticked: Pawn_ApparelTracker.ApparelTrackerTickInterval only handles
    // the nudity tale and locked-apparel wearout, and never reaches the comps. So the wearer's
    // own top-up runs from here instead of from a comp tick.
    public class MapComponent_HemogenTanks : MapComponent
    {
        const int CheckInterval = 250;

        public MapComponent_HemogenTanks(Map map) : base(map)
        {
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % CheckInterval != 0) return;

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                if (!HemogenTankUtility.WantsHemogen(pawn)) continue;

                Comp_HemogenTank tank = HemogenTankUtility.WornTank(pawn);
                if (tank == null || !tank.TrySpendCharge()) continue;

                GeneUtility.OffsetHemogen(pawn, tank.Props.hemogenPerPack, true);
            }
        }
    }

    // Mirrors the vanilla FeedHemogen giver, except the hemogen comes out of the worker's own
    // tank instead of off the floor, so no pack has to be hauled and no map search is needed.
    public class WorkGiver_TankTransfuse : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.Pawn);

        public override Danger MaxPathDanger(Pawn pawn) => Danger.Deadly;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            List<Pawn> candidates = pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction);
            for (int i = 0; i < candidates.Count; i++) yield return candidates[i];
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            return HemogenTankUtility.WornTank(pawn) == null;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Pawn patient = t as Pawn;
            if (patient == null || patient == pawn || patient.Dead) return false;

            Comp_HemogenTank tank = HemogenTankUtility.WornTank(pawn);
            if (tank == null) return false;

            if (!HemogenTankUtility.NeedsTransfusion(patient, tank.Props)) return false;

            // Downed counts as well as bedridden: a tank is field gear, and the pawn
            // bleeding out in the open is the one that needs it.
            if (!patient.Downed && !FeedPatientUtility.ShouldBeFed(patient)) return false;

            if (!pawn.CanReserve(patient, 1, -1, null, forced)) return false;

            return true;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!HasJobOnThing(pawn, t, forced)) return null;
            return JobMaker.MakeJob(BS9000DefOf.BS9000_TankTransfuse, t);
        }
    }

    public class JobDriver_TankTransfuse : JobDriver
    {
        const int TransfuseTicks = 240;

        Pawn Patient => (Pawn)job.targetA.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            this.FailOnAggroMentalState(TargetIndex.A);
            this.FailOn(() => HemogenTankUtility.WornTank(pawn) == null);
            this.FailOn(() => Patient.Dead);

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            Toil work = Toils_General.Wait(TransfuseTicks, TargetIndex.A);
            work.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            work.WithProgressBarToilDelay(TargetIndex.A);
            yield return work;

            yield return Toils_General.Do(delegate
            {
                Comp_HemogenTank tank = HemogenTankUtility.WornTank(pawn);
                if (HemogenTankUtility.Transfuse(tank, Patient) && PawnUtility.ShouldSendNotificationAbout(Patient))
                {
                    Messages.Message("BG9555_Transfused".Translate(Patient.Named("PAWN")),
                        Patient, MessageTypeDefOf.NeutralEvent, historical: false);
                }
            });
        }

        [DebugAction("Pawns", "BG-9555: transfuse from tank", requiresBiotech: true, actionType = DebugActionType.ToolMapForPawns, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        static void TransfuseNow(Pawn p) => HemogenTankUtility.Transfuse(HemogenTankUtility.WornTank(p), p);
    }
}
