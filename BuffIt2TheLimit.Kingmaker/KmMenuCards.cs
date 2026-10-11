using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Kingmaker.UI._ConsoleUI.Common;
using Kingmaker.UI._ConsoleUI.InGameMenu;
using Kingmaker.UI._ConsoleUI.Utils.MultiNavigationTool;
using UnityEngine;
using UnityEngine.UI;

namespace BuffIt2TheLimit
{
    // A card of this mod in the gamepad menu of the game, next to Formation, Character, Inventory. The card is a copy of
    // the game's Rest card with its own pictures and title; it takes part in the carousel and in the navigation like
    // the game's cards. The window opens after the menu has closed.
    internal class MenuCard
    {
        internal string Id;
        internal Func<string> Title;
        internal Action Open;
        internal string BigResource;
        internal string SmallResource;
        internal Type[] Menus;
        internal Sprite Big;
        internal Sprite Small;
        // Colour of the card and of its small picture; the game gives every card its own.
        internal Color32 Tint = new Color32(255, 255, 255, 255);
    }

    internal static class MenuCards
    {
        internal static readonly List<MenuCard> Cards = new List<MenuCard>();
        static readonly Dictionary<string, InGameMenuEntityView> s_Views = new Dictionary<string, InGameMenuEntityView>();
        static readonly Dictionary<string, InGameMenuEntityVM> s_Vms = new Dictionary<string, InGameMenuEntityVM>();
        static Action s_Pending;
        static float s_PendingAt;
        static BaseGameMenuView s_Menu;

        static string Key(Component view, MenuCard card)
        {
            return view.GetInstanceID() + ":" + card.Id;
        }

        // The pictures are one colour with transparent cut-outs: the colour is spread over the transparent pixels so that
        // the edges do not turn dark when the picture is scaled down, and the texture gets mipmaps.
        static Sprite Load(string resource, Color32 flat)
        {
            using (var stream = typeof(MenuCards).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) return null;
                var memory = new MemoryStream();
                stream.CopyTo(memory);
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, true);
                texture.LoadImage(memory.ToArray());
                var pixels = texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    pixels[i].r = flat.r;
                    pixels[i].g = flat.g;
                    pixels[i].b = flat.b;
                }
                texture.SetPixels32(pixels);
                texture.filterMode = FilterMode.Trilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.anisoLevel = 4;
                texture.Apply(true, false);
                return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
            }
        }

        static void SetTint(Transform card, string name, Color color)
        {
            var part = card.Find("Decor/" + name);
            var image = part != null ? part.GetComponent<Image>() : null;
            if (image != null) image.color = color;
        }

        static void SetIcon(Transform card, string name, Sprite sprite)
        {
            var icon = card.Find("Decor/" + name);
            var image = icon != null ? icon.GetComponent<Image>() : null;
            if (image == null || sprite == null) return;
            image.sprite = sprite;
            image.preserveAspect = true;
        }

        // The menu view is built: add the cards to the carousel.
        internal static void Created(BaseGameMenuView view)
        {
            try
            {
                var template = Traverse.Create(view).Field("m_RestView").GetValue<InGameMenuEntityView>();
                var collection = Traverse.Create(view).Field("m_CartCollection").GetValue<CartCollectionManagerView>();
                if (template == null || collection == null) return;
                foreach (var card in Cards)
                {
                    if (Array.IndexOf(card.Menus, view.GetType()) < 0) continue;
                    if (card.Big == null) card.Big = Load(card.BigResource, new Color32(216, 176, 106, 255));
                    if (card.Small == null) card.Small = Load(card.SmallResource, new Color32(255, 255, 255, 255));
                    var go = UnityEngine.Object.Instantiate(template.gameObject, template.transform.parent, false);
                    go.name = "Console_InGameMenu_EntityView_" + card.Id;
                    SetIcon(go.transform, "BigIcon", card.Big);
                    SetIcon(go.transform, "SmallIcon", card.Small);
                    SetTint(go.transform, "CartTexture", card.Tint);
                    SetTint(go.transform, "SmallIcon", card.Tint);
                    var cardView = go.GetComponent<InGameMenuEntityView>();
                    s_Views[Key(view, card)] = cardView;
                    // After the game's cards (and the cards of other mods added before), laid over or under the card
                    // before it as the game's cards lie on each other.
                    var list = new List<ICartCollectionEntity>(Traverse.Create(collection).Field("m_CartList").GetValue<List<ICartCollectionEntity>>());
                    var previous = list.Count > 0 ? list[list.Count - 1] as Component : null;
                    list.Add(cardView);
                    if (previous != null && list.Count > 2 && list[0] is Component first && list[1] is Component second)
                        Stack(go.transform, previous.transform, second.transform.GetSiblingIndex() > first.transform.GetSiblingIndex());
                    collection.Initialize(list);
                    Main.Log("menu card " + card.Id + " added to " + view.GetType().Name + ", " + list.Count + " cards");
                }
            }
            catch (Exception e)
            {
                Main.Error(e, "MenuCards.Created");
            }
        }

        // Cards are drawn in the order of the hierarchy, a later one over an earlier one: right after the card before it
        // when the next card lies over the one before, right before it when under.
        static void Stack(Transform card, Transform previous, bool overPrevious)
        {
            card.SetAsLastSibling();
            int index = previous.GetSiblingIndex();
            card.SetSiblingIndex(overPrevious ? index + 1 : index);
            Main.Log("menu card laid " + (overPrevious ? "over " : "under ") + previous.name);
        }

        // The menu opens: bind the cards and add them to the navigation after the game's cards.
        internal static void Bound(BaseGameMenuView view, List<List<IConsoleMultiNavigationEntity>> rows, InGameMenuVM viewModel)
        {
            try
            {
                foreach (var card in Cards)
                {
                    string key = Key(view, card);
                    InGameMenuEntityView cardView;
                    if (!s_Views.TryGetValue(key, out cardView) || cardView == null) continue;
                    var captured = card;
                    var vm = new InGameMenuEntityVM(() => Select(view, viewModel, captured), card.Title(), false, true);
                    s_Vms[key] = vm;
                    // The game starts the action of the selected entity when it disposes the menu's model.
                    var entities = Traverse.Create(viewModel).Field("m_EntityVms").GetValue<List<InGameMenuEntityVM>>();
                    if (entities != null) entities.Add(vm);
                    cardView.Bind(vm);
                    Main.Log("menu card " + card.Id + " bound, row " + (rows == null ? -1 : rows.Count));
                    if (rows == null || rows.Count == 0) continue;
                    rows[0].Add(cardView);
                }
            }
            catch (Exception e)
            {
                Main.Error(e, "MenuCards.Bound");
            }
        }

        static void Select(BaseGameMenuView view, InGameMenuVM viewModel, MenuCard card)
        {
            Main.Log("menu card selected: " + card.Id);
            s_Menu = view;
            s_Pending = card.Open;
            s_PendingAt = Time.unscaledTime;
        }

        // Called every frame: open the window once the menu is gone.
        internal static void Tick()
        {
            if (s_Pending == null) return;
            bool menuOpen = s_Menu != null && s_Menu.gameObject.activeInHierarchy;
            if (menuOpen && Time.unscaledTime - s_PendingAt < 1.5f) return;
            Main.Log("menu card opens now (menu open: " + menuOpen + ")");
            var action = s_Pending;
            s_Pending = null;
            try
            {
                action();
            }
            catch (Exception e)
            {
                Main.Error(e, "MenuCards.Open");
            }
        }
    }

    [HarmonyPatch(typeof(InGameMenuView), "InitializeCartCollection")]
    static class InGameMenuCreated
    {
        static void Postfix(InGameMenuView __instance) { MenuCards.Created(__instance); }
    }

    [HarmonyPatch(typeof(InGameMenuView), "BindEntities")]
    static class InGameMenuBound
    {
        static void Postfix(InGameMenuView __instance, List<List<IConsoleMultiNavigationEntity>> __result, InGameMenuVM viewModel) { MenuCards.Bound(__instance, __result, viewModel); }
    }

    [HarmonyPatch(typeof(GlobalMapMenuView), "InitializeCartCollection")]
    static class GlobalMapMenuCreated
    {
        static void Postfix(GlobalMapMenuView __instance) { MenuCards.Created(__instance); }
    }

    [HarmonyPatch(typeof(GlobalMapMenuView), "BindEntities")]
    static class GlobalMapMenuBound
    {
        static void Postfix(GlobalMapMenuView __instance, List<List<IConsoleMultiNavigationEntity>> __result, InGameMenuVM viewModel) { MenuCards.Bound(__instance, __result, viewModel); }
    }
}
