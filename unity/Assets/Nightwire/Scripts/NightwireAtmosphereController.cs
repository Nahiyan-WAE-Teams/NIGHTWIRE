using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Nightwire.Unity.UI
{
    /// <summary>
    /// Adds three independently drifting, procedural fog banks to a UI Toolkit screen.
    /// Textures are generated at runtime, so the atmosphere adds no downloaded media.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class NightwireAtmosphereController : MonoBehaviour
    {
        private const int TextureWidth = 256;
        private const int TextureHeight = 96;
        private static readonly float[] Speeds = { 1.15f, 0.72f, 0.43f };
        private static readonly float[] Heights = { 7f, 19f, 31f };
        private static readonly float[] Opacities = { 0.12f, 0.085f, 0.06f };

        private readonly VisualElement[] layers = new VisualElement[3];
        private readonly Texture2D[] textures = new Texture2D[3];
        private UIDocument document;
        private float elapsed;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null)
                return;

            var root = document.rootVisualElement;
            for (int i = 0; i < layers.Length; i++)
            {
                textures[i] = CreateFogTexture(i * 71 + 19);
                var layer = new VisualElement
                {
                    name = "nightwire-fog-" + i,
                    pickingMode = PickingMode.Ignore
                };
                layer.style.position = Position.Absolute;
                layer.style.width = Length.Percent(150);
                layer.style.height = Length.Percent(39);
                layer.style.opacity = Opacities[i];
                layer.style.backgroundImage = new StyleBackground(textures[i]);
                layer.style.unityBackgroundScaleMode = ScaleMode.StretchToFill;
                root.Insert(0, layer);
                layers[i] = layer;
            }
            PositionLayers();
        }

        private void Update()
        {
            if (layers[0] == null)
                return;

            elapsed += Time.unscaledDeltaTime;
            PositionLayers();
        }

        private void PositionLayers()
        {
            for (int i = 0; i < layers.Length; i++)
            {
                float drift = Mathf.Repeat(elapsed * Speeds[i] * 2.2f + i * 43f, 125f) - 25f;
                float sway = Mathf.Sin(elapsed * (0.11f + i * 0.035f) + i * 2.1f) * 5f;
                layers[i].style.left = Length.Percent(drift);
                layers[i].style.bottom = Length.Percent(Heights[i] + sway);
            }
        }

        private static Texture2D CreateFogTexture(int seed)
        {
            var texture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false)
            {
                name = "NIGHTWIRE procedural fog",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[TextureWidth * TextureHeight];
            for (int y = 0; y < TextureHeight; y++)
            {
                float vertical = Mathf.Sin(Mathf.PI * y / (TextureHeight - 1f));
                for (int x = 0; x < TextureWidth; x++)
                {
                    float horizontal = Mathf.Sin(Mathf.PI * x / (TextureWidth - 1f));
                    float broad = Mathf.PerlinNoise((x + seed) / 67f, (y + seed) / 39f);
                    float detail = Mathf.PerlinNoise((x - seed * 2) / 23f, (y + seed) / 17f);
                    float cloud = Mathf.Clamp01((broad * 0.72f + detail * 0.28f - 0.34f) * 2.1f);
                    float alpha = cloud * horizontal * Mathf.Lerp(0.25f, 1f, vertical) * 0.72f;
                    pixels[y * TextureWidth + x] = new Color(0.56f, 0.64f, 0.88f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private void OnDisable()
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] != null)
                    layers[i].RemoveFromHierarchy();
                layers[i] = null;
                if (textures[i] != null)
                    Destroy(textures[i]);
                textures[i] = null;
            }
        }
    }
}
