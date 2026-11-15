import json
import sys
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from validate_unity_ui import validate_project


class NightwireUnityUiTests(unittest.TestCase):
    def test_unity_project_uses_patched_lts_version(self):
        version = (ROOT / "unity/ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
        self.assertIn("m_EditorVersion: 6000.3.28f1", version)

    def test_unity_package_manifest_is_valid(self):
        manifest = json.loads((ROOT / "unity/Packages/manifest.json").read_text(encoding="utf-8"))
        self.assertIsInstance(manifest["dependencies"], dict)

    def test_both_runtime_uxml_documents_are_valid_xml(self):
        for relative in (
            "unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml",
            "unity/Assets/Nightwire/UI/MessengerScreen.uxml",
        ):
            with self.subTest(relative=relative):
                ET.parse(ROOT / relative)

    def test_all_controller_query_names_exist_in_their_uxml(self):
        errors = validate_project()
        self.assertFalse(errors, "\\n".join(errors))

    def test_stylesheets_are_attached_to_uxml_documents(self):
        for relative in (
            "unity/Assets/Nightwire/UI/EpisodeLibraryView.uxml",
            "unity/Assets/Nightwire/UI/MessengerScreen.uxml",
        ):
            with self.subTest(relative=relative):
                source = (ROOT / relative).read_text(encoding="utf-8")
                self.assertIn("project://database/Assets/Nightwire/UI/", source)
                self.assertIn(".uss", source)

    def test_unity_assembly_references_portable_runtime(self):
        asmdef = json.loads((ROOT / "unity/Assets/Nightwire/Scripts/Nightwire.Unity.asmdef").read_text(encoding="utf-8"))
        self.assertIn("Nightwire.Runtime", asmdef["references"])

    def test_editor_bootstrapper_generates_scene_and_bundled_episode(self):
        source = (ROOT / "unity/Assets/Editor/NightwireProjectBootstrapper.cs").read_text(encoding="utf-8")
        self.assertIn("NightwireMain.unity", source)
        self.assertIn("Episode_001.zip", source)
        self.assertIn("EditorBuildSettings.scenes", source)

    def test_messenger_has_audio_and_video_playback_handlers(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        for token in ("AddAudioAttachment", "AddVideoAttachment", "VideoPlayer", "UnityWebRequestMultimedia"):
            with self.subTest(token=token):
                self.assertIn(token, source)

    def test_media_playback_uses_manifest_verified_typed_assets(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        self.assertIn('CacheVerifiedMediaAsset(assetId, "audio")', source)
        self.assertIn('CacheVerifiedMediaAsset(assetId, "video")', source)
        self.assertIn("session.OpenAsset(assetId)", source)
        self.assertIn("maximumInlineMediaBytes", source)

    def test_audio_playback_only_accepts_supported_streaming_types(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        self.assertIn('case ".mp3":', source)
        self.assertIn('case ".wav":', source)
        self.assertIn('case ".ogg":', source)
        self.assertIn("UNSUPPORTED AUDIO FORMAT", source)
        self.assertNotIn("AudioType.AIFF", source)
        self.assertNotIn("AudioType.ACC", source)

    def test_bundled_sample_seeder_is_disabled_in_release_players(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/NightwireBundledEpisodeSeeder.cs").read_text(encoding="utf-8")
        self.assertIn("#if !UNITY_EDITOR && !DEVELOPMENT_BUILD", source)
        self.assertIn("bundled sample seeding is disabled in release players", source)

    def test_procedural_fog_is_layered_animated_and_attached_to_both_screens(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/NightwireAtmosphereController.cs").read_text(encoding="utf-8")
        bootstrap = (ROOT / "unity/Assets/Editor/NightwireProjectBootstrapper.cs").read_text(encoding="utf-8")
        for token in ("CreateFogTexture", "Time.unscaledDeltaTime", "Mathf.PerlinNoise", "Speeds", "Opacities"):
            with self.subTest(token=token):
                self.assertIn(token, source)
        self.assertEqual(bootstrap.count("AddComponent<NightwireAtmosphereController>()"), 2)

    def test_messenger_exposes_localized_character_profile_overlay(self):
        uxml = (ROOT / "unity/Assets/Nightwire/UI/MessengerScreen.uxml").read_text(encoding="utf-8")
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        for token in ("profile-open-button", "profile-overlay", "profile-name", "profile-status", "profile-bio"):
            with self.subTest(token=token):
                self.assertIn(token, uxml)
        for token in ("ShowContactProfile", "HideContactProfile", 'ReadString(profile, "bio_key")'):
            with self.subTest(token=token):
                self.assertIn(token, source)

    def test_iap_service_uses_iap_55_and_requires_server_fulfillment(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/NightwireIapService.cs").read_text(encoding="utf-8")
        for token in ("PurchasingController", "CatalogProvider", "OnPurchasePending", "ConfirmPurchase",
                      "INightwirePurchaseFulfillment", "VerifyAndFulfillAsync", "CanPurchase"):
            with self.subTest(token=token):
                self.assertIn(token, source)
        self.assertIn("fulfillmentProvider is INightwirePurchaseFulfillment", source)
        self.assertIn("if (!fulfilled)", source)
        self.assertIn("The transaction remains pending", source)

    def test_group_conversations_render_a_participant_roster(self):
        uxml = (ROOT / "unity/Assets/Nightwire/UI/MessengerScreen.uxml").read_text(encoding="utf-8")
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        self.assertIn('name="group-participants"', uxml)
        self.assertIn("FindOtherParticipants", source)
        self.assertIn('contactName.text = "GROUP CHAT"', source)
        self.assertIn('string.Join("  ·  ", otherParticipants.Select(CharacterName))', source)

    def test_messenger_exposes_messages_social_feed_and_evidence_tabs(self):
        uxml = (ROOT / "unity/Assets/Nightwire/UI/MessengerScreen.uxml").read_text(encoding="utf-8")
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        for token in ("messages-tab", "feed-tab", "evidence-tab", "social-feed-list", "evidence-list"):
            with self.subTest(token=token):
                self.assertIn(token, uxml)
        for token in ("RenderSocialFeed", "RenderEvidenceLibrary", "AddDocumentAttachment",
                      "session.Package.CharacterPosts", "session.Progress.UnlockedAssetIds"):
            with self.subTest(token=token):
                self.assertIn(token, source)

    def test_document_preview_opens_only_verified_unlocked_asset(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/MessengerScreenController.cs").read_text(encoding="utf-8")
        start = source.index("private void AddDocumentAttachment")
        end = source.index("private void RenderConversation", start)
        method = source[start:end]
        self.assertIn("session?.OpenAsset(assetId)", method)
        self.assertIn("opened.Asset.SizeBytes > 1048576", method)
        self.assertIn("textDocument", method)

    def test_development_provider_does_not_refresh_remote_catalogue(self):
        source = (ROOT / "unity/Assets/Nightwire/Scripts/NightwireDevelopmentPlatformServices.cs").read_text(encoding="utf-8")
        self.assertIn("ShouldRefreshCatalogue => false", source)
        self.assertIn("disabled in release players", source)


if __name__ == "__main__":
    unittest.main()
