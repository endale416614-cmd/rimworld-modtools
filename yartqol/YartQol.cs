using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using YART;
using YART.Compat;
using YART.Data;
using YART.Utils;

namespace YartQol
{
    [StaticConstructorOnStartup]
    static class Startup
    {
        static Startup() => new Harmony("local.yart.qol").PatchAll(typeof(Startup).Assembly);
    }

    public class AutoResearch : GameComponent
    {
        const int CheckInterval = 250;

        public bool On;

        public AutoResearch(Game game)
        {
        }

        public override void ExposeData() => Scribe_Values.Look(ref On, "autoResearch");

        public override void GameComponentTick()
        {
            if (On && Find.TickManager.TicksGame % CheckInterval == 0) Pick();
        }

        public void Toggle()
        {
            On = !On;
            if (On) Pick();
        }

        // Fills every idle research slot, after the player's own queue. Turns itself off once no slot has anything to run.
        public void Pick()
        {
            // Colony Manager finishes a project on the load thread, before benches and slots can be judged.
            if (Current.ProgramState != ProgramState.Playing) return;
            ResearchQueueManager queue = ResearchQueueManager.Instance;
            if (queue == null || !GraphBuildPipeline.GraphReady) return;

            queue.TryStartAllHeads(playSound: false);

            bool running = false;
            foreach (ResearchChannel channel in ChannelRegistry.All)
            {
                if (channel.CurrentProject == null)
                {
                    ResearchProjectDef next = Cheapest(channel);
                    if (next != null) Find.ResearchManager.SetCurrentProject(next);
                }
                running |= channel.CurrentProject != null;
            }
            if (running) return;

            On = false;
            Messages.Message("YartQol_AutoResearchDone".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        static ResearchProjectDef Cheapest(ResearchChannel channel)
        {
            ResearchProjectDef best = null;
            foreach (ResearchNode node in ResearchGraph.Instance.AllNodes.Values)
            {
                ResearchProjectDef def = node.Def;
                // Cost before CanStartNow: that getter scans every colonist building for an appropriate bench, and a node no
                // cheaper than the best startable one so far cannot win whatever it answers. Ties still go to the first node.
                if (node.Channel != channel || (best != null && def.CostApparent >= best.CostApparent)) continue;
                if (def.CanStartNow && !Gravtech(def)) best = def;
            }
            return best;
        }

        // Gravtech advances on gravdata from flights, not at a bench, and does not fill the slot its YART channel reads.
        static bool Gravtech(ResearchProjectDef def)
        {
            System.Reflection.MethodInfo method = GravshipCompat.IsGravshipResearchMethod;
            return method != null && (bool)method.Invoke(null, new object[] { def });
        }
    }

    // Runs before YART's own postfix, so its completion letter can name the next project and the researcher keeps working.
    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    static class NextOnFinish
    {
        [HarmonyPriority(Priority.First)]
        static void Postfix()
        {
            AutoResearch auto = Current.Game?.GetComponent<AutoResearch>();
            if (auto != null && auto.On) auto.Pick();
        }
    }

    // Left of the queue cards, because a minimap window can cover the right end of the bar.
    [HarmonyPatch(typeof(MainTabWindow_YART), "DoQueueBar")]
    static class QueueBarButton
    {
        const float Width = 110f;

        static void Prefix(ref float leftInset) => leftInset += Width + 8f;

        // Drawn after the bar, whose background would cover it.
        static void Postfix(Rect rect, float leftInset)
        {
            AutoResearch auto = Current.Game.GetComponent<AutoResearch>();
            Rect button = new Rect(rect.x + leftInset - Width, rect.y + 10f, Width, rect.height - 24f);

            Color color = auto.On ? Constraints.ButtonActive : Constraints.ButtonDisabled;
            Widgets.DrawBoxSolid(button, GenColor.WithAlpha(color, 0.3f));
            GUIDrawingUtilities.DrawBorderLines(button, color, 1f);
            if (Mouse.IsOver(button)) Widgets.DrawHighlight(button);
            using (Temporary.Font(GameFont.Small))
            using (Temporary.Anchor(TextAnchor.MiddleCenter))
            {
                Widgets.Label(button, (auto.On ? "YartQol_AutoResearchOn" : "YartQol_AutoResearchOff").Translate());
            }
            TooltipHandler.TipRegion(button, "YartQol_AutoResearchTip".Translate());

            if (Widgets.ButtonInvisible(button))
            {
                auto.Toggle();
                SoundDefOf.Tick_Low.PlayOneShotOnCamera();
            }
        }
    }
}
