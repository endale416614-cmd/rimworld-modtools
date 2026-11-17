using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace LandmarkEditor
{
    public class Dialog_LandmarkEditor : Window
    {
        private const float RowHeight = 34f;
        private const float IconSize = 26f;
        private const float ButtonWidth = 84f;

        private enum Tab { Landmark, Features }

        private sealed class Row
        {
            public Def def;
            public string label;
            public string sub;
            public string search;
            public string tip;
            public bool valid;
            public bool active;
            public bool blocked;
            public string button;
        }

        private readonly Tile tile;
        private readonly List<Row> landmarkRows = new List<Row>();
        private readonly List<Row> featureRows = new List<Row>();
        private List<Row> shown = new List<Row>();

        private Tab tab = Tab.Landmark;
        private Vector2 scroll;
        private string search = "";
        private bool onlyValid;
        private Tab shownForTab = Tab.Features;
        private string shownForSearch;
        private bool shownForOnlyValid;

        public override Vector2 InitialSize => new Vector2(820f, 680f);

        public Dialog_LandmarkEditor(Tile tile)
        {
            this.tile = tile;
            doCloseX = true;
            draggable = true;
            resizeable = true;
            preventCameraMotion = false;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            Rebuild();
        }

        /// <summary>Opens the editor for a tile, replacing an editor already open for another tile.</summary>
        public static void OpenFor(Tile t)
        {
            if (t == null || Find.WindowStack == null) return;
            foreach (Dialog_LandmarkEditor open in Find.WindowStack.Windows.OfType<Dialog_LandmarkEditor>().ToList())
                open.Close(false);
            Find.WindowStack.Add(new Dialog_LandmarkEditor(t));
        }

        /// <summary>Gives the tile this landmark, replacing the one it has.</summary>
        public void SetLandmark(LandmarkDef def)
        {
            LandmarkOps.Set(tile, def);
            Rebuild();
        }

        /// <summary>Takes the tile's landmark away, along with the terrain it added.</summary>
        public void RemoveLandmark()
        {
            LandmarkOps.Clear(tile);
            Rebuild();
        }

        /// <summary>Adds a tile feature, or removes it when the tile already has it.</summary>
        public void ToggleFeature(TileMutatorDef def)
        {
            if (def == null) return;
            if (tile.Mutators != null && tile.Mutators.Contains(def)) LandmarkOps.RemoveFeature(tile, def);
            else LandmarkOps.AddFeature(tile, def);
            Rebuild();
        }

        // ---- building the two lists ----

        private void Rebuild()
        {
            BuildLandmarks();
            BuildFeatures();
            shownForSearch = null;
        }

        private void BuildLandmarks()
        {
            landmarkRows.Clear();
            LandmarkDef current = tile.Landmark?.def;

            // A tile that already has a landmark is invalid for every def, so judge validity with the
            // current landmark lifted out of the table and put straight back.
            WorldLandmarks table = Find.World?.landmarks;
            Landmark held = tile.Landmark;
            if (held != null && table != null) table.RemoveLandmark(tile.tile);
            IList<TileMutatorDef> live = tile.Mutators;
            List<int> liftedAt = null;
            List<TileMutatorDef> liftedDefs = null;
            try
            {
                // The held landmark's own mutators have to come out with it: a mutator on the tile makes every
                // def sharing its categories at no higher priority invalid, and Set strips them first anyway.
                if (held != null && live != null && live.Count > 0)
                {
                    for (int i = live.Count - 1; i >= 0; i--)
                    {
                        TileMutatorDef m = live[i];
                        if (m == null || !OwnedBy(held.def, m)) continue;
                        if (liftedAt == null) { liftedAt = new List<int>(); liftedDefs = new List<TileMutatorDef>(); }
                        liftedAt.Add(i);
                        liftedDefs.Add(m);
                        live.RemoveAt(i);
                    }
                }

                foreach (LandmarkDef def in DefDatabase<LandmarkDef>.AllDefsListForReading)
                {
                    bool valid = false;
                    try { valid = def.IsValidTile(tile.tile, tile.Layer, false); }
                    catch (Exception e) { Log.WarningOnce("Landmark Editor: " + def.defName + " validity check failed: " + e.Message, def.shortHash); }

                    string category = string.IsNullOrEmpty(def.category) ? "Uncategorized" : def.category;
                    landmarkRows.Add(new Row
                    {
                        def = def,
                        label = def.LabelCap,
                        sub = def.defName + "  ·  " + category,
                        search = (def.label + " " + def.defName + " " + category).ToLowerInvariant(),
                        valid = valid,
                        active = def == current,
                        button = def == current ? null : "Set",
                        tip = LandmarkTip(def, valid)
                    });
                }
            }
            finally
            {
                if (held != null && table != null) table[tile.tile] = held;
                if (liftedAt != null)
                    for (int i = liftedAt.Count - 1; i >= 0; i--) live.Insert(liftedAt[i], liftedDefs[i]);
            }

            landmarkRows.Sort(ByLabel);
        }

        private static bool OwnedBy(LandmarkDef landmark, TileMutatorDef mutator)
        {
            foreach (MutatorChance mc in LandmarkOps.Chances(landmark))
                if (mc?.mutator == mutator) return true;
            return false;
        }

        private void BuildFeatures()
        {
            featureRows.Clear();
            IList<TileMutatorDef> on = tile.Mutators;

            foreach (TileMutatorDef def in DefDatabase<TileMutatorDef>.AllDefsListForReading)
            {
                bool active = on != null && on.Contains(def);
                bool valid = active;
                if (!active)
                {
                    try { valid = def.IsValidTile(tile.tile, tile.Layer); }
                    catch (Exception e) { Log.WarningOnce("Landmark Editor: " + def.defName + " validity check failed: " + e.Message, def.shortHash); }
                }

                TileMutatorDef blocker = active ? null : Blocker(def, on);
                string label = def.label;
                if (WorkerCanDescribe(def)) { try { label = def.Label(tile.tile); } catch { } }
                if (string.IsNullOrEmpty(label)) label = def.defName;
                string categories = def.categories != null && def.categories.Count > 0 ? string.Join(", ", def.categories) : "no category";

                featureRows.Add(new Row
                {
                    def = def,
                    label = label.CapitalizeFirst(),
                    sub = def.defName + "  ·  " + categories,
                    search = (label + " " + def.defName + " " + categories).ToLowerInvariant(),
                    valid = valid,
                    active = active,
                    blocked = blocker != null,
                    button = active ? "Remove" : "Add",
                    tip = FeatureTip(def, active, valid, blocker)
                });
            }

            // What the tile has comes first, then the rest by label.
            featureRows.Sort((a, b) =>
            {
                if (a.active != b.active) return a.active ? -1 : 1;
                return ByLabel(a, b);
            });
        }

        private static int ByLabel(Row a, Row b)
        {
            int c = string.Compare(a.label, b.label, StringComparison.InvariantCultureIgnoreCase);
            return c != 0 ? c : string.Compare(a.def.defName, b.def.defName, StringComparison.InvariantCultureIgnoreCase);
        }

        /// <summary>The mutator already on the tile that would make this one conflict, if any.</summary>
        private static TileMutatorDef Blocker(TileMutatorDef def, IList<TileMutatorDef> on)
        {
            if (on == null || def.categories == null) return null;
            foreach (TileMutatorDef other in on)
            {
                if (other?.categories == null) continue;
                foreach (string category in def.categories)
                {
                    // Tile.AddMutator logs an error instead of adding when the new mutator loses this comparison.
                    if (other.categories.Contains(category) && def.priority < other.priority) return other;
                }
            }
            return null;
        }

        /// <summary>Vanilla's animal-habitat worker logs an error and gives no label when the tile's
        /// biome has no animal common enough for it, which underwater biomes do not.</summary>
        private bool WorkerCanDescribe(TileMutatorDef def)
        {
            if (!(def.Worker is TileMutatorWorker_AnimalHabitat)) return true;
            BiomeDef biome = tile.PrimaryBiome;
            if (biome == null) return false;
            bool coastal = tile.IsCoastal;
            foreach (PawnKindDef kind in biome.AllWildAnimals)
            {
                if (biome.CommonalityOfAnimal(kind) > 0.1f) return true;
                if (coastal && biome.CommonalityOfCoastalAnimal(kind) > 0.1f) return true;
            }
            return false;
        }

        private static string LandmarkTip(LandmarkDef def, bool valid)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(def.LabelCap).Append("  (").Append(def.defName).Append(")");
            if (!string.IsNullOrEmpty(def.description)) sb.Append("\n\n").Append(def.description);

            var mutators = new List<string>();
            foreach (MutatorChance mc in LandmarkOps.Chances(def))
            {
                if (mc?.mutator == null) continue;
                string line = mc.mutator.LabelCap;
                if (mc.required) line += " (always)";
                else if (mc.chance < 1f) line += " (" + Mathf.RoundToInt(mc.chance * 100f) + "%)";
                mutators.Add(line);
            }
            if (mutators.Count > 0) sb.Append("\n\nFeatures: ").Append(string.Join(", ", mutators));
            sb.Append(valid ? "\n\nThe game could place this landmark here." : "\n\nThe game would not place this landmark here. Setting it anyway is allowed.");
            return sb.ToString();
        }

        private string FeatureTip(TileMutatorDef def, bool active, bool valid, TileMutatorDef blocker)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(def.LabelCap).Append("  (").Append(def.defName).Append(")");
            string desc = def.description;
            if (WorkerCanDescribe(def)) { try { desc = def.Description(tile.tile); } catch { } }
            if (!string.IsNullOrEmpty(desc)) sb.Append("\n\n").Append(desc);

            if (active) sb.Append("\n\nThis tile has it. Removing it takes it off the tile.");
            else if (blocker != null) sb.Append("\n\nCannot be added: ").Append(blocker.LabelCap).Append(" is on this tile and outranks it in the same category.");
            else sb.Append(valid ? "\n\nThe game could put this on the tile." : "\n\nThe game would not put this on the tile. Adding it anyway is allowed.");
            return sb.ToString();
        }

        private void Filter()
        {
            if (shownForTab == tab && shownForSearch == search && shownForOnlyValid == onlyValid) return;
            string needle = (search ?? "").Trim().ToLowerInvariant();
            List<Row> source = tab == Tab.Landmark ? landmarkRows : featureRows;
            shown = source.Where(r =>
                (needle.Length == 0 || r.search.Contains(needle)) &&
                (!onlyValid || r.valid || r.active)).ToList();
            if (shownForTab != tab) scroll = Vector2.zero;
            shownForTab = tab;
            shownForSearch = search;
            shownForOnlyValid = onlyValid;
        }

        // ---- drawing ----

        public override void DoWindowContents(Rect inRect)
        {
            Filter();
            Landmark current = tile.Landmark;
            float y = inRect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 34f), "Tile editor");
            Text.Font = GameFont.Small;
            y += 36f;

            string biome = tile.PrimaryBiome != null ? tile.PrimaryBiome.LabelCap.ToString() : "unknown biome";
            int featureCount = tile.Mutators == null ? 0 : tile.Mutators.Count;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "Tile " + tile.tile + "  ·  " + biome + "  ·  " + LandmarkText(current) + "  ·  " + featureCount + " feature" + (featureCount == 1 ? "" : "s"));
            y += 24f;

            GUI.color = new Color(1f, 1f, 1f, 0.55f);
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f), "Changes here only affect maps generated afterwards. Maps that already exist keep their terrain.");
            GUI.color = Color.white;
            y += 30f;

            Rect box = new Rect(inRect.x, y + 32f, inRect.width, inRect.yMax - (y + 32f) - CloseButSize.y - 10f);
            Widgets.DrawMenuSection(box);
            TabDrawer.DrawTabs(box, new List<TabRecord>
            {
                new TabRecord("Landmark", () => tab = Tab.Landmark, tab == Tab.Landmark),
                new TabRecord("Features (" + featureCount + ")", () => tab = Tab.Features, tab == Tab.Features)
            });

            Rect content = box.ContractedBy(8f);
            float cy = content.y;

            if (tab == Tab.Landmark)
            {
                Rect removeRect = new Rect(content.x, cy, 180f, 30f);
                bool hasLandmark = current != null;
                if (Widgets.ButtonText(removeRect, "Remove landmark", true, true, hasLandmark) && hasLandmark) RemoveLandmark();
                TooltipHandler.TipRegion(removeRect, hasLandmark
                    ? "Remove " + LandmarkText(current) + " from this tile, along with the features it added."
                    : "This tile has no landmark to remove.");
            }
            else
            {
                GUI.color = new Color(1f, 1f, 1f, 0.55f);
                Widgets.Label(new Rect(content.x, cy + 4f, 320f, 24f), "Features the tile has are listed first.");
                GUI.color = Color.white;
            }

            Rect validRect = new Rect(content.x + 196f, cy + 3f, 220f, 24f);
            Widgets.CheckboxLabeled(validRect, "Only ones that fit here", ref onlyValid);
            TooltipHandler.TipRegion(validRect, "Hide the ones the game would never put on this tile. You can still set them with this off.");

            Rect searchRect = new Rect(content.xMax - 280f, cy, 280f, 30f);
            search = Widgets.TextField(searchRect, search ?? "");
            if (string.IsNullOrEmpty(search) && !searchRect.Contains(Event.current.mousePosition))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.4f);
                Widgets.Label(new Rect(searchRect.x + 6f, searchRect.y + 2f, searchRect.width - 12f, 26f), "Search name, defName or category");
                GUI.color = Color.white;
            }
            cy += 38f;

            DrawList(new Rect(content.x, cy, content.width, content.yMax - cy));

            Rect closeRect = new Rect(inRect.center.x - CloseButSize.x / 2f, inRect.yMax - CloseButSize.y, CloseButSize.x, CloseButSize.y);
            if (Widgets.ButtonText(closeRect, "Close")) Close();
        }

        private static string LandmarkText(Landmark l)
        {
            if (l == null) return "no landmark";
            return string.IsNullOrEmpty(l.name) ? l.def.LabelCap.ToString() : l.name + " (" + l.def.LabelCap + ")";
        }

        private void DrawList(Rect outRect)
        {
            if (shown.Count == 0)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.5f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(outRect, "Nothing matches.");
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                return;
            }

            Rect viewRect = new Rect(0f, 0f, outRect.width - 16f, shown.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            Rect visible = new Rect(0f, scroll.y, viewRect.width, outRect.height);
            Row clicked = null;

            for (int i = 0; i < shown.Count; i++)
            {
                Rect row = new Rect(0f, i * RowHeight, viewRect.width, RowHeight);
                if (!row.Overlaps(visible)) continue;
                Row e = shown[i];

                if (i % 2 == 1) Widgets.DrawAltRect(row);
                if (e.active) Widgets.DrawHighlightSelected(row);
                Widgets.DrawHighlightIfMouseover(row);
                TooltipHandler.TipRegion(row, e.tip);

                float textX = row.x + 6f;
                if (e.def is LandmarkDef landmark)
                {
                    Rect iconRect = new Rect(row.x + 4f, row.y + (RowHeight - IconSize) / 2f, IconSize, IconSize);
                    DrawIcon(iconRect, landmark);
                    textX = iconRect.xMax + 8f;
                }

                float textW = row.width - textX - ButtonWidth - 12f;
                if (!e.valid && !e.active) GUI.color = new Color(1f, 1f, 1f, 0.6f);
                Widgets.Label(new Rect(textX, row.y + 1f, textW * 0.55f, RowHeight - 2f), e.label);
                GUI.color = new Color(1f, 1f, 1f, 0.45f);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(textX + textW * 0.55f, row.y, textW * 0.45f, RowHeight), e.sub);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;

                Rect butRect = new Rect(row.xMax - ButtonWidth - 4f, row.y + 3f, ButtonWidth, RowHeight - 6f);
                if (e.button == null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.6f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(butRect, "Current");
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                else if (Widgets.ButtonText(butRect, e.button, true, true, !e.blocked) && !e.blocked)
                {
                    clicked = e;
                }
            }

            Widgets.EndScrollView();

            if (clicked == null) return;
            if (clicked.def is LandmarkDef pick) SetLandmark(pick);
            else if (clicked.def is TileMutatorDef feature) ToggleFeature(feature);
        }

        private static void DrawIcon(Rect rect, LandmarkDef def)
        {
            Texture2D tex = null;
            try { tex = def.Texture; } catch { }
            if (BaseContent.NullOrBad(tex)) return;

            // The icon path can hold an atlas of frames; draw the first one.
            float cols = Mathf.Max(1, def.atlasSize.x);
            float rows = Mathf.Max(1, def.atlasSize.z);
            GUI.DrawTextureWithTexCoords(rect, tex, new Rect(0f, 0f, 1f / cols, 1f / rows));
        }
    }
}
