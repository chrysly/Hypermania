using System.Collections.Generic;
using Design.Configs;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Scenes.Menus.CharacterSelect
{
    /// <summary>
    /// One panel per player slot (P1 left/red, P2 right/blue). Watches its
    /// slot in the shared state and runs its own Phase/Character/Skin
    /// transitions — the directory never pushes updates onto it.
    /// </summary>
    public class PlayerPanel : MonoBehaviour
    {
        [SerializeField]
        private int _playerIndex;

        [Header("Preview elements (visible during Character phase)")]
        [SerializeField]
        private GameObject _previewRoot;

        [FormerlySerializedAs("_portrait")] [SerializeField]
        private Image _splash;

        [SerializeField]
        private TMP_Text _characterName;

        [SerializeField]
        private Image[] _mainImages;

        [SerializeField]
        private Image[] _lightImages;

        [SerializeField]
        private Image[] _accentImages;

        [Header("Options panel (visible during Options / Confirmed phases)")]
        [SerializeField]
        private PlayerOptionsPanel _optionsPanel;

        [SerializeField]
        private FadeToggle _optionsFade;

        [SerializeField]
        private FadeToggle _confirmedBanner;

        private PlayerSelectionState _state;
        private CharacterConfig[] _roster;
        private SkinConfig _randomSkin;
        private bool _isLocal;

        private int _lastCharIndex = -1;
        private int _lastSkinIndex = -1;
        private SelectPhase _lastPhase = SelectPhase.Confirmed;

        // Portrait sprites are lazily wrapped from SkinConfig.Portrait (Texture2D)
        // and cached so we don't leak a fresh Sprite on every character/skin change.
        private readonly Dictionary<Texture2D, Sprite> _splashSprites = new();

        public int PlayerIndex => _playerIndex;
        public PlayerOptionsPanel OptionsPanel => _optionsPanel;

        public void Bind(
            PlayerSelectionState state,
            CharacterConfig[] roster,
            ControlsConfig[] controlsPresets,
            SkinConfig randomSkin,
            bool isLocal
        )
        {
            _state = state;
            _roster = roster;
            _randomSkin = randomSkin;
            _isLocal = isLocal;
            _lastCharIndex = -1;
            _lastSkinIndex = -1;

            if (_optionsPanel != null)
            {
                _optionsPanel.Bind(state, roster, controlsPresets, isLocal);
            }

            // Snap fade state to the initial phase so we don't fade in/out
            // from the scene's serialized default on first frame.
            if (_optionsFade != null)
                _optionsFade.SnapTo(_state.Phase == SelectPhase.Options);
            if (_confirmedBanner != null)
                _confirmedBanner.SnapTo(_state.Phase == SelectPhase.Confirmed);
            _lastPhase = _state.Phase;
        }

        private void Update()
        {
            if (_state == null || _roster == null || _roster.Length == 0)
                return;

            if (_state.Phase != _lastPhase)
            {
                ApplyPhaseVisibility(_state.Phase);
                _lastPhase = _state.Phase;
            }

            if (_state.CharacterIndex != _lastCharIndex || _state.SkinIndex != _lastSkinIndex)
            {
                ApplyCharacterPreview();
                _lastCharIndex = _state.CharacterIndex;
                _lastSkinIndex = _state.SkinIndex;
            }
        }

        private void ApplyPhaseVisibility(SelectPhase phase)
        {
            if (_optionsFade != null)
            {
                _optionsFade.SetVisible(phase == SelectPhase.Options);
            }
            if (_confirmedBanner != null)
            {
                _confirmedBanner.SetVisible(phase == SelectPhase.Confirmed);
            }
            if (_previewRoot != null)
            {
                _previewRoot.SetActive(true); // preview stays visible in all phases as backdrop
            }
        }

        private void ApplyCharacterPreview()
        {
            // CharacterIndex == _roster.Length indicates the Random grid slot
            // — the concrete character hasn't been rolled yet. Paint the
            // panel with the shared Random skin (same asset the grid uses
            // for the Random tile) so the preview, tints, and ready marker
            // stay visually consistent while the player is on Random.
            if (_state.CharacterIndex >= _roster.Length)
            {
                if (_characterName != null)
                    _characterName.text = "Random";
                if (_splash != null)
                {
                    Sprite sprite = GetOrCreateSprite(_randomSkin.Splash);
                    _splash.sprite = sprite;
                    _splash.enabled = sprite != null;
                }
                ApplyTint(_mainImages, _randomSkin.MainColor);
                ApplyTint(_lightImages, _randomSkin.LightColor);
                ApplyTint(_accentImages, _randomSkin.AccentColor);
                return;
            }

            int charIdx = Mathf.Clamp(_state.CharacterIndex, 0, _roster.Length - 1);
            CharacterConfig config = _roster[charIdx];
            if (config == null)
                return;

            if (_characterName != null)
            {
                _characterName.text = config.Character.ToString();
            }

            SkinConfig skin = default;
            bool hasSkin = config.Skins != null && config.Skins.Length > 0;
            if (hasSkin)
            {
                int skinIdx = Mathf.Clamp(_state.SkinIndex, 0, config.Skins.Length - 1);
                skin = config.Skins[skinIdx];
            }

            if (_splash != null)
            {
                Sprite sprite = hasSkin ? GetOrCreateSprite(skin.Splash) : null;
                _splash.sprite = sprite;
                _splash.enabled = sprite != null;
            }

            if (hasSkin)
            {
                ApplyTint(_mainImages, skin.MainColor);
                ApplyTint(_lightImages, skin.LightColor);
                ApplyTint(_accentImages, skin.AccentColor);
            }
        }

        private static void ApplyTint(Image[] images, Color color)
        {
            if (images == null)
                return;
            foreach (Image img in images)
            {
                if (img != null)
                    img.color = color;
            }
        }

        private Sprite GetOrCreateSprite(Texture2D tex)
        {
            if (tex == null)
                return null;
            if (_splashSprites.TryGetValue(tex, out Sprite cached))
                return cached;
            Sprite sprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, tex.width, tex.height),
                new Vector2(0.5f, 0.5f)
            );
            _splashSprites[tex] = sprite;
            return sprite;
        }

        private void OnDestroy()
        {
            foreach (Sprite sprite in _splashSprites.Values)
            {
                if (sprite != null)
                    Destroy(sprite);
            }
            _splashSprites.Clear();
        }
    }
}
