using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LudeonTK;
using RimWorld;
using UnityEngine;
using VAspirE;
using Verse;

namespace AspirationControl
{
	[StaticConstructorOnStartup]
	public static class Bootstrap
	{
		static Bootstrap()
		{
			Harmony harmony = new Harmony("local.aspiration.control");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
			CharacterEditorBridge.Patch(harmony);
		}
	}

	/// <summary>Shared access to the parts of Need_Fulfillment that are not public.</summary>
	public static class FulfillmentAccess
	{
		private static readonly AccessTools.FieldRef<Need, Pawn> PawnField =
			AccessTools.FieldRefAccess<Need, Pawn>("pawn");

		private static readonly AccessTools.FieldRef<Need_Fulfillment, List<int>> TicksField =
			AccessTools.FieldRefAccess<Need_Fulfillment, List<int>>("completedTicks");

		public const int MaxSlots = 5;

		public static Pawn PawnOf(Need need)
		{
			return PawnField(need);
		}

		public static List<int> TicksOf(Need_Fulfillment need)
		{
			ref List<int> ticks = ref TicksField(need);
			if (ticks == null)
			{
				ticks = new List<int>();
			}
			return ticks;
		}

		public static void SetTicks(Need_Fulfillment need, List<int> ticks)
		{
			TicksField(need) = ticks;
		}

		public static Need_Fulfillment Of(Pawn pawn)
		{
			if (pawn == null || pawn.needs == null)
			{
				return null;
			}
			return pawn.needs.TryGetNeed<Need_Fulfillment>();
		}

		public static bool Enabled(AspirationDef def)
		{
			Dictionary<string, bool> states = VanillaAspirationsExpanded_Mod.settings?.aspirationStates;
			bool on;
			if (states == null || !states.TryGetValue(def.defName, out on))
			{
				return true;
			}
			return on;
		}

		// AspirationDef.Icon is typed against the Unity assemblies the aspirations mod was built on,
		// which cannot be referenced from here, so it is read once per def and kept as the engine type.
		private static readonly PropertyInfo IconProperty = AccessTools.Property(typeof(AspirationDef), "Icon");

		private static readonly Dictionary<AspirationDef, Texture> IconCache = new Dictionary<AspirationDef, Texture>();

		public static Texture IconOf(AspirationDef def)
		{
			Texture icon;
			if (IconCache.TryGetValue(def, out icon))
			{
				return icon;
			}
			try
			{
				icon = (IconProperty != null) ? (IconProperty.GetValue(def) as Texture) : null;
			}
			catch
			{
				icon = null;
			}
			IconCache[def] = icon;
			return icon;
		}

		public static bool Suits(AspirationDef def, Pawn pawn)
		{
			try
			{
				return def.Worker.ValidOn(pawn);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Puts the need level back in step with the completed slots.</summary>
		public static void Resync(Need_Fulfillment need, bool recheck)
		{
			List<int> ticks = TicksOf(need);
			int completed = 0;
			for (int i = 0; i < ticks.Count; i++)
			{
				if (ticks[i] != -1)
				{
					completed++;
				}
			}
			need.aspirationsForThisPawn = Mathf.Max(4, need.Aspirations.Count);
			need.CurLevel = completed;
			if (recheck)
			{
				need.CheckCompletion(true);
			}
		}
	}

	/// <summary>
	/// Lays the aspiration icons out inside the rect the need was given instead of below it.
	/// The stock version draws a fixed strip starting 10px above the bottom edge, which runs
	/// over the needs drawn after it in any list tighter than the vanilla needs tab.
	/// </summary>
	[HarmonyPatch(typeof(Need_Fulfillment), nameof(Need_Fulfillment.DrawOnGUI))]
	public static class Need_Fulfillment_DrawOnGUI_Patch
	{
		private static readonly List<AspirationDef> Hidden = new List<AspirationDef>();

		public const float MaxIconSize = 28f;
		public const float MinIconSize = 10f;
		public const float MinBarHeight = 24f;
		public const float HeightShare = 0.42f;

		/// <summary>
		/// Splits the rect the need was given into the bar above and the icon strip below.
		/// Returns false when there is no room for icons, and then leaves the rect to the bar.
		/// </summary>
		public static bool Layout(Rect rect, float customMargin, int aspirationCount, out Rect barRect, out Rect strip)
		{
			barRect = rect;
			strip = default(Rect);
			float margin = (customMargin >= 0f) ? customMargin : 29f;
			float availWidth = rect.width - margin * 2f;
			int slots = aspirationCount + ((aspirationCount < FulfillmentAccess.MaxSlots) ? 1 : 0);
			if (availWidth <= 0f || rect.height <= MinBarHeight || slots <= 0)
			{
				return false;
			}

			float size = Mathf.Min(availWidth / slots, rect.height * HeightShare, MaxIconSize);
			size = Mathf.Clamp(size, MinIconSize, rect.height - MinBarHeight);
			if (size < MinIconSize || size > availWidth / slots)
			{
				return false;
			}

			float stripWidth = size * slots;
			strip = new Rect(rect.x + margin + (availWidth - stripWidth) * 0.5f, rect.yMax - size, stripWidth, size);
			// The bar keeps the rest of the rect, so nothing is drawn outside what the caller gave us.
			barRect.height = rect.height - size;
			return true;
		}

		public static void Prefix(Need_Fulfillment __instance, ref Rect rect, float customMargin,
			ref bool doTooltip, ref Rect? rectForTooltip, out List<AspirationDef> __state)
		{
			__state = __instance.Aspirations;
			if (__state == null)
			{
				return;
			}

			Rect orig = rect;
			Rect barRect, strip;
			// The stock icon loop runs over this empty list and draws nothing, whether or not there is
			// room for a strip here. A rect too small for icons gets the bar alone rather than a spill.
			__instance.Aspirations = Hidden;
			if (!Layout(orig, customMargin, __state.Count, out barRect, out strip))
			{
				return;
			}
			rect = barRect;

			// Need.DrawOnGUI highlights and tips over rectForTooltip, and Need_Fulfillment grows it by
			// (barWidth / 10) before passing it on. Pre-shrink it so the result covers the original rect.
			float margin = (customMargin >= 0f) ? customMargin : 29f;
			Rect tip = rectForTooltip ?? orig;
			tip.height = Mathf.Max(MinBarHeight, tip.height - (rect.width - margin * 2f) / 10f);
			rectForTooltip = tip;

			if (Mouse.IsOver(strip))
			{
				doTooltip = false;
			}
			Draw(__instance, __state, strip, strip.height);
		}

		/// <summary>Runs even when the original throws, so the pawn never keeps the empty list.</summary>
		public static void Finalizer(Need_Fulfillment __instance, List<AspirationDef> __state)
		{
			if (__state != null)
			{
				__instance.Aspirations = __state;
			}
		}

		private static void Draw(Need_Fulfillment need, List<AspirationDef> aspirations, Rect strip, float size)
		{
			Pawn pawn = FulfillmentAccess.PawnOf(need);
			List<int> ticks = FulfillmentAccess.TicksOf(need);
			for (int i = 0; i < aspirations.Count; i++)
			{
				Rect slot = new Rect(strip.x + size * i, strip.y, size, size);
				AspirationDef def = aspirations[i];
				int tick = (i < ticks.Count) ? ticks[i] : -1;
				Texture icon = FulfillmentAccess.IconOf(def) ?? BaseContent.BadTex;
				if (tick != -1)
				{
					GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit);
				}
				else
				{
					GUI.DrawTexture(slot, icon, ScaleMode.ScaleToFit, true, 1f, Color.gray, 0f, 0f);
				}
				if (Mouse.IsOver(slot))
				{
					Widgets.DrawHighlight(slot);
					TooltipHandler.TipRegion(slot,
						new TipSignal(def.TooltipFor(pawn, tick) + "\n\n" + "Click to change this aspiration.",
							slot.GetHashCode()));
				}
				int index = i;
				if (Widgets.ButtonInvisible(slot))
				{
					OpenPicker(need, aspirations, index);
				}
			}

			if (aspirations.Count < FulfillmentAccess.MaxSlots)
			{
				Rect add = new Rect(strip.x + size * aspirations.Count, strip.y, size, size);
				GUI.DrawTexture(add.ContractedBy(size * 0.2f), TexButton.Plus, ScaleMode.ScaleToFit);
				if (Mouse.IsOver(add))
				{
					Widgets.DrawHighlight(add);
					TooltipHandler.TipRegion(add, new TipSignal("Add an aspiration.", add.GetHashCode()));
				}
				if (Widgets.ButtonInvisible(add))
				{
					OpenPicker(need, aspirations, aspirations.Count);
				}
			}
		}

		// Called while drawing, when the need's own list is swapped out, so the real list is passed in.
		private static void OpenPicker(Need_Fulfillment need, List<AspirationDef> current, int index)
		{
			Pawn pawn = FulfillmentAccess.PawnOf(need);
			List<DebugMenuOption> options = new List<DebugMenuOption>();
			if (index < current.Count)
			{
				options.Add(new DebugMenuOption("(remove)", DebugMenuOptionMode.Action,
					delegate { RemoveAt(need, index); }));
			}
			foreach (AspirationDef def in DefDatabase<AspirationDef>.AllDefs
				.Where((AspirationDef d) => !current.Contains(d) && FulfillmentAccess.Enabled(d))
				.OrderBy((AspirationDef d) => d.LabelCap.RawText))
			{
				AspirationDef picked = def;
				string label = FulfillmentAccess.Suits(picked, pawn)
					? picked.LabelCap.RawText
					: picked.LabelCap.RawText + " (unsuited)";
				options.Add(new DebugMenuOption(label, DebugMenuOptionMode.Action,
					delegate { SetAt(need, index, picked); }));
			}
			Find.WindowStack.Add(new Dialog_DebugOptionListLister(options, "Aspiration"));
		}

		public static void SetAt(Need_Fulfillment need, int index, AspirationDef def)
		{
			List<int> ticks = FulfillmentAccess.TicksOf(need);
			if (index < need.Aspirations.Count)
			{
				need.Aspirations[index] = def;
				ticks[index] = -1;
			}
			else if (need.Aspirations.Count < FulfillmentAccess.MaxSlots)
			{
				need.Aspirations.Add(def);
				ticks.Add(-1);
			}
			FulfillmentAccess.Resync(need, true);
		}

		public static void RemoveAt(Need_Fulfillment need, int index)
		{
			if (index < 0 || index >= need.Aspirations.Count)
			{
				return;
			}
			List<int> ticks = FulfillmentAccess.TicksOf(need);
			need.Aspirations.RemoveAt(index);
			if (index < ticks.Count)
			{
				ticks.RemoveAt(index);
			}
			FulfillmentAccess.Resync(need, false);
		}
	}

	/// <summary>
	/// Stores the aspirations in the Character Editor pawn preset. The editor writes needs as
	/// "defName|percentage" entries joined by ":" and skips any entry that is not two fields, so the
	/// three-field entry written here is ignored by the editor itself and by older builds of this mod.
	/// </summary>
	public static class CharacterEditorBridge
	{
		private const string Marker = "ASPIRATIONS";

		public static void Patch(Harmony harmony)
		{
			Type type = AccessTools.TypeByName("CharacterEditor.MindTool");
			if (type == null)
			{
				return;
			}
			MethodInfo read = AccessTools.Method(type, "GetAllNeedsAsSeparatedString");
			MethodInfo write = AccessTools.Method(type, "SetNeeds");
			if (read == null || write == null)
			{
				Log.Warning("[Aspiration Control] Character Editor is installed but its needs preset methods moved; "
					+ "aspirations will not be stored in character presets.");
				return;
			}
			harmony.Patch(read, postfix: new HarmonyMethod(typeof(CharacterEditorBridge), nameof(ReadPostfix)));
			harmony.Patch(write, postfix: new HarmonyMethod(typeof(CharacterEditorBridge), nameof(WritePostfix)));
		}

		public static void ReadPostfix(Pawn p, ref string __result)
		{
			Need_Fulfillment need = FulfillmentAccess.Of(p);
			if (need == null || need.Aspirations.NullOrEmpty())
			{
				return;
			}
			List<int> ticks = FulfillmentAccess.TicksOf(need);
			List<string> parts = new List<string>();
			for (int i = 0; i < need.Aspirations.Count; i++)
			{
				int tick = (i < ticks.Count) ? ticks[i] : -1;
				parts.Add(need.Aspirations[i].defName + "~" + tick);
			}
			string entry = "VAspirE_Fulfillment|" + Marker + "|" + string.Join(",", parts.ToArray());
			__result = string.IsNullOrEmpty(__result) ? entry : (__result + ":" + entry);
		}

		public static void WritePostfix(Pawn p, string s)
		{
			if (string.IsNullOrEmpty(s))
			{
				return;
			}
			foreach (string entry in s.Split(':'))
			{
				string[] fields = entry.Split('|');
				if (fields.Length == 3 && fields[1] == Marker)
				{
					Apply(p, fields[2]);
				}
			}
		}

		private static void Apply(Pawn p, string payload)
		{
			try
			{
				if (p == null || p.needs == null)
				{
					return;
				}
				Need_Fulfillment need = FulfillmentAccess.Of(p);
				if (need == null)
				{
					p.needs.AddOrRemoveNeedsAsAppropriate();
					need = FulfillmentAccess.Of(p);
				}
				if (need == null)
				{
					return;
				}

				List<AspirationDef> aspirations = new List<AspirationDef>();
				List<int> ticks = new List<int>();
				foreach (string part in payload.Split(','))
				{
					if (part.Length == 0)
					{
						continue;
					}
					string[] pair = part.Split('~');
					AspirationDef def = DefDatabase<AspirationDef>.GetNamedSilentFail(pair[0]);
					if (def == null)
					{
						continue;
					}
					int tick;
					if (pair.Length < 2 || !int.TryParse(pair[1], out tick))
					{
						tick = -1;
					}
					aspirations.Add(def);
					ticks.Add(tick);
				}
				if (aspirations.Count == 0)
				{
					return;
				}

				need.Aspirations = aspirations;
				FulfillmentAccess.SetTicks(need, ticks);
				FulfillmentAccess.Resync(need, false);
			}
			catch (Exception e)
			{
				Log.Warning("[Aspiration Control] could not restore aspirations: " + e);
			}
		}
	}
}
