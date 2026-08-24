using UnityEngine;
using UnityEngine.UI;

namespace SashimiBoy
{
    [CreateAssetMenu(
        menuName = "Sashimi Boy/UI/Prototype UI Theme",
        fileName = "PrototypeUITheme")]
    public sealed class PrototypeUITheme : ScriptableObject
    {
        [Header("Surfaces")]
        public Color panel = new Color(0.025f, 0.032f, 0.038f, 0.94f);
        public Color panelSoft = new Color(0.035f, 0.045f, 0.052f, 0.86f);
        public Color panelBorder = new Color(0.2f, 0.26f, 0.29f, 1f);

        [Header("Content")]
        public Color primaryText = new Color(0.96f, 0.97f, 0.98f, 1f);
        public Color secondaryText = new Color(0.67f, 0.72f, 0.75f, 1f);
        public Color cyanAccent = new Color(0.2f, 0.82f, 0.88f, 1f);
        public Color goldAccent = new Color(1f, 0.68f, 0.2f, 1f);
        public Color dangerAccent = new Color(0.95f, 0.18f, 0.16f, 1f);

        [Header("Button")]
        public Color buttonNormal = new Color(0.15f, 0.2f, 0.22f, 1f);
        public Color buttonHighlighted = new Color(0.22f, 0.32f, 0.35f, 1f);
        public Color buttonPressed = new Color(0.08f, 0.52f, 0.58f, 1f);
        public Color buttonSelected = new Color(0.12f, 0.62f, 0.68f, 1f);
        public Color buttonDisabled = new Color(0.08f, 0.1f, 0.11f, 0.72f);

        public ColorBlock CreateButtonColors()
        {
            ColorBlock colors = ColorBlock.defaultColorBlock;
            colors.normalColor = buttonNormal;
            colors.highlightedColor = buttonHighlighted;
            colors.pressedColor = buttonPressed;
            colors.selectedColor = buttonSelected;
            colors.disabledColor = buttonDisabled;
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            return colors;
        }
    }
}
