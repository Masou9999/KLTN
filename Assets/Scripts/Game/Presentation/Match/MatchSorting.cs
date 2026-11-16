using System;
using UnityEngine;

namespace KLTN.Game.Presentation
{
    /// <summary>
    /// Named layers for renderers/camera canvases, with matching order bands for Overlay UI.
    /// Each group owns 100 orders; local offsets cannot escape into the next group.
    /// Keep the authored GameScene and TagManager in sync with this policy.
    /// </summary>
    public static class MatchSorting
    {
        public const string Background = "GameBackground";
        public const string Board = "GameBoard";
        public const string CardMotion = "GameCardMotion";
        public const string Effects = "GameEffects";
        public const string Hud = "GameHUD";
        public const string Targeting = "GameTargeting";
        public const string Phase = "GamePhase";
        public const string Modal = "GameModal";
        public const string Result = "GameResult";
        public const string SystemOverlay = "SystemOverlay";

        public static int Order(string layer, int offset = 0)
        {
            int basis = layer switch
            {
                Background => 0,
                Board => 100,
                CardMotion => 200,
                Effects => 300,
                Hud => 400,
                Targeting => 500,
                Phase => 600,
                Modal => 700,
                Result => 800,
                SystemOverlay => 5000,
                _ => throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown match sorting layer"),
            };
            return basis + Mathf.Clamp(offset, 0, 99);
        }

        public static void Apply(Canvas canvas, string layer, int offset = 0)
        {
            canvas.overrideSorting = true;
            canvas.sortingLayerID = SortingLayer.NameToID(layer);
            canvas.sortingOrder = Order(layer, offset);
        }
    }
}
